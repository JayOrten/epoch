using System;
using System.Diagnostics;
using Arch.Core;
using Arch.Core.Extensions;
using epoch.Graphics;
using epoch.Graphics.Tiles;
using epoch.Utilities;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using MonoGame.Extended;

namespace epoch.ECS;

/// <summary>
/// Two-pass tile renderer. Pass 1 draws all visible tiles to a render target using
/// GPU instancing with perspective projection (vanishing-point depth offset).
/// Pass 2 composites the render target to the screen with a post-processing shader.
/// Handles draw-rule culling via <see cref="Position.SpaceMask"/> and smooth
/// position/scale interpolation for z-level transitions.
/// </summary>
public sealed class DrawSystem : SystemBase<GameTime>
{
    private readonly QueryDescription _compositeEntitiesToDraw = new QueryDescription().WithAll<
        Position,
        GraphicalTileList
    >();

    private readonly Effect _renderShader;
    private readonly Effect _effectShader;

    private readonly RenderTarget2D _renderTarget2D;

    // Controls vanishing point time smoothing
    private float _smoothTime = 0.1f;
    private float _depthStrength = 0.06f;

    private Vector2 _currentVanishingPoint;
    private Vector2 _vanishingPointVelocity;

    // Controls draw position and scale smoothing
    private float _drawTime = 0.0001f;

    // Lightweight profiling: accumulate over N frames, log the average
    private const int ProfileInterval = 60;
    private int _profileFrame;
    private long _accumQueryTicks;
    private long _accumInstEndTicks;
    private int _accumTiles;

    /// <summary>
    ///     Initializes a new instance of the <see cref="DrawSystem"/> class.
    /// </summary>
    /// <param name="world">Its <see cref="World"/>.</param>
    /// <param name="batch">The <see cref="SpriteBatch"/> used to draw all <see cref="Entity"/>s.</param>
    public DrawSystem(World world, Effect renderShader, Effect effectShader)
        : base(world)
    {
        _renderShader = renderShader;
        _effectShader = effectShader;

        _renderTarget2D = new RenderTarget2D(
            Core.GraphicsDevice,
            Core.Graphics.PreferredBackBufferWidth,
            Core.Graphics.PreferredBackBufferHeight
        );
    }

    /// <summary>
    ///     Gets called to execute the draw systems logic and to draw the <see cref="Entity"/>s.
    /// </summary>
    public override void Update(in GameTime gameTime)
    {
        // -- SETUP --
        // If the current vanishing point is zero, set it to the center of the screen
        if (_currentVanishingPoint == Vector2.Zero)
        {
            _currentVanishingPoint = new Vector2(
                Core.GraphicsDevice.Viewport.Width / 2,
                Core.GraphicsDevice.Viewport.Height / 2
            );
        }

        // -- Pass 1: render tiles to render target --
        Core.GraphicsDevice.SetRenderTarget(_renderTarget2D);

        // Set background color
        Core.GraphicsDevice.Clear(new Color(24, 25, 38));

        // get the transformation for world -> screen space
        var viewMatrix = GlobalContext.Camera.GetViewMatrix();

        // Get projection matrix for projecting to CLIP space (-1 to 1)
        var projectionMatrix = Matrix.CreateOrthographicOffCenter(
            0,
            Core.GraphicsDevice.Viewport.Width,
            Core.GraphicsDevice.Viewport.Height,
            0,
            0,
            1
        );

        // Combine them (Order matters: View * Projection)
        var finalTransform = viewMatrix * projectionMatrix;

        Core.TileInstancing.Begin(
            // sortMode: SpriteSortMode.BackToFront,
            effect: _renderShader,
            samplerState: SamplerState.PointClamp
        );

        var transformParam = _renderShader.Parameters["WorldViewProjection"];
        var cameraZoomParam = _renderShader.Parameters["CameraZoom"];

        if (transformParam != null)
            transformParam.SetValue(finalTransform);
        if (cameraZoomParam != null)
            cameraZoomParam.SetValue(GlobalContext.Camera.Zoom);

        // TODO: just house this stuff inside the instancing?

        // -- DRAW TILES --
        // Get player z position
        ref var pos = ref GlobalContext.PlayerEntity.Get<Position>();
        float playerZLevel = pos.WorldCoordinate.Z;

        // Compute vanishing point ONCE per frame (not per tile)
        Vector2 finalVanishingPoint =
            GlobalContext.Camera.Center
            + (GlobalContext.CameraEntity.Get<CameraState>().LookDirection);

        _currentVanishingPoint = CameraUtils.SmoothDamp(
            _currentVanishingPoint,
            finalVanishingPoint,
            ref _vanishingPointVelocity,
            _smoothTime,
            float.MaxValue,
            gameTime.GetElapsedSeconds()
        );

        // Precompute per-frame values used by every tile
        float lerpFactor = 1 - (float)Math.Pow(_drawTime, gameTime.GetElapsedSeconds());

        // Hoist per-frame constants out of the hot loop
        var tileManager = GlobalContext.TileManager;
        var tileset = tileManager.Tileset;
        float globalScale = GlobalContext.GlobalScale;
        float tileWidth = tileset.TileWidth;
        float tileHeight = tileset.TileHeight;
        float depthStrength = _depthStrength;
        Vector2 vanishingPoint = _currentVanishingPoint;
        var tileInstancing = Core.TileInstancing;

        // Viewport culling: compute visible bounds in grid space with margin
        // for perspective shift and tile size
        var cameraBounds = GlobalContext.Camera.BoundingRectangle;
        float worldToGridX = 1.0f / (tileWidth * globalScale);
        float worldToGridY = 1.0f / (tileHeight * globalScale);
        // Margin accounts for perspective offset at extreme Z + one tile of padding
        float cullMargin = 15.0f;
        float cullMinX = cameraBounds.Left * worldToGridX - cullMargin;
        float cullMaxX = cameraBounds.Right * worldToGridX + cullMargin;
        float cullMinY = cameraBounds.Top * worldToGridY - cullMargin;
        float cullMaxY = cameraBounds.Bottom * worldToGridY + cullMargin;

        // Draw composite entities
        long t0 = Stopwatch.GetTimestamp();
        int drawnTiles = 0;
        var compositeQuery = World.Query(in _compositeEntitiesToDraw);
        foreach (ref var chunk in compositeQuery)
        {
            var positions = chunk.GetArray<Position>();
            var graphicalTileLists = chunk.GetArray<GraphicalTileList>();

            foreach (var index in chunk)
            {
                ref var position = ref positions[index];
                ref var graphicalTileList = ref graphicalTileLists[index];

                // ======= CHECK DRAW RULES =======
                // MiddleMask: cardinal + horizontal edge bits (N/E/S/W + NE/SE/SW/NW)
                const int MiddleMask =
                    (1 << 0)
                    | (1 << 1)
                    | (1 << 2)
                    | (1 << 3)
                    | (1 << 6)
                    | (1 << 7)
                    | (1 << 8)
                    | (1 << 9);

                bool hasMiddleExposure = (position.SpaceMask & MiddleMask) != 0;
                bool aboveIsOpen = (position.SpaceMask & (1 << 4)) != 0;

                // No exposure at all — fully buried, skip entirely
                if (!hasMiddleExposure && !aboveIsOpen)
                    continue;

                // Viewport culling: skip entities outside the visible area
                float gx = position.WorldCoordinate.X;
                float gy = position.WorldCoordinate.Y;
                if (gx < cullMinX || gx > cullMaxX || gy < cullMinY || gy > cullMaxY)
                    continue;

                int lastIndex = graphicalTileList.Tiles.Length - 1;

                for (int i = 0; i < graphicalTileList.Tiles.Length; i++)
                {
                    // Skip inactive tiles
                    if ((graphicalTileList.ActiveTileMask & (1 << i)) == 0)
                        continue;

                    bool isTop = (i == lastIndex);

                    // Top tile only draws when above is open
                    if (isTop && !aboveIsOpen)
                        continue;
                    // No middle exposure — only draw the top tile
                    if (!isTop && !hasMiddleExposure)
                        continue;

                    drawnTiles++;
                    ref var graphicalTile = ref graphicalTileList.Tiles[i];

                    Tile tileInfo = tileManager.GetTile(graphicalTile.TileId);
                    if (tileInfo == null)
                        continue;

                    // Get texture region and rotation
                    (TextureRegion region, float rotation) = tileset.GetTile(
                        tileInfo.TileIndex,
                        graphicalTile.AutoTileMask
                    );

                    // Inline perspective transform (avoids 9-param call overhead per tile)
                    Vector2 basePosition =
                        new Vector2(
                            position.WorldCoordinate.X * tileWidth,
                            position.WorldCoordinate.Y * tileHeight
                        )
                        * graphicalTile.Scale
                        * globalScale;

                    float depth =
                        (position.WorldCoordinate.Z - playerZLevel) + graphicalTile.Offset;
                    float perspectiveScale = 1.0f + (depth * depthStrength);

                    Vector2 vpOffset = vanishingPoint * (1.0f - perspectiveScale);
                    Vector2 finalPosition = vpOffset + (basePosition * perspectiveScale);
                    float finalScale = graphicalTile.Scale * globalScale * perspectiveScale;

                    // Initialize interpolation values on first draw
                    if (!graphicalTile.DrawInitialized)
                    {
                        graphicalTile.CurrentDrawPosition = finalPosition;
                        graphicalTile.CurrentDrawScale = finalScale;
                        graphicalTile.DrawInitialized = true;
                    }

                    // Smoothly interpolate position to prevent popping on z-level transitions
                    var targetPosition = graphicalTile.InterpolateMovement
                        ? Vector2.Lerp(graphicalTile.CurrentDrawPosition, finalPosition, lerpFactor)
                        : finalPosition;

                    graphicalTile.CurrentDrawPosition = targetPosition;
                    graphicalTile.CurrentDrawScale = MathHelper.Lerp(
                        graphicalTile.CurrentDrawScale,
                        finalScale,
                        lerpFactor
                    );

                    // Color: use override if set, otherwise fall back to tile definition
                    Color background1Color,
                        background2Color,
                        baseColor,
                        accentColor,
                        borderColor;
                    int colorMask = graphicalTile.ColorOverrideMask;
                    if (colorMask == 0)
                    {
                        background1Color = tileInfo.Background1Color;
                        background2Color = tileInfo.Background2Color;
                        baseColor = tileInfo.BaseColor;
                        accentColor = tileInfo.AccentColor;
                        borderColor = tileInfo.BorderColor;
                    }
                    else
                    {
                        background1Color =
                            (colorMask & (1 << 0)) != 0
                                ? graphicalTile.Background1Color
                                : tileInfo.Background1Color;
                        background2Color =
                            (colorMask & (1 << 1)) != 0
                                ? graphicalTile.Background2Color
                                : tileInfo.Background2Color;
                        baseColor =
                            (colorMask & (1 << 2)) != 0
                                ? graphicalTile.BaseColor
                                : tileInfo.BaseColor;
                        accentColor =
                            (colorMask & (1 << 3)) != 0
                                ? graphicalTile.AccentColor
                                : tileInfo.AccentColor;
                        borderColor =
                            (colorMask & (1 << 4)) != 0
                                ? graphicalTile.BorderColor
                                : tileInfo.BorderColor;
                    }

                    float sortingLevel =
                        position.WorldCoordinate.Z + graphicalTile.Offset + position.Top;

                    float layerDifference = position.WorldCoordinate.Z - playerZLevel;

                    tileInstancing.Draw(
                        graphicalTile.CurrentDrawPosition,
                        sortingLevel,
                        graphicalTile.CurrentDrawScale,
                        rotation,
                        graphicalTile.BorderMask,
                        graphicalTile.BorderWidth,
                        layerDifference,
                        region.SourceRectangle,
                        background1Color,
                        background2Color,
                        baseColor,
                        accentColor,
                        borderColor
                    );
                }
            }
        }
        long t1 = Stopwatch.GetTimestamp();

        Core.TileInstancing.End();
        long t2 = Stopwatch.GetTimestamp();

        // -- Pass 2: Render Target to Screen with post-processing shader --
        Core.GraphicsDevice.SetRenderTarget(null);

        Core.SpriteBatch.Begin(effect: _effectShader);

        var timeParam = _effectShader.Parameters["Time"];
        if (timeParam != null)
            timeParam.SetValue((float)gameTime.TotalGameTime.TotalSeconds);

        Core.SpriteBatch.Draw(
            _renderTarget2D,
            new Rectangle(
                0,
                0,
                Core.Graphics.PreferredBackBufferWidth,
                Core.Graphics.PreferredBackBufferHeight
            ),
            Color.White
        );

        Core.SpriteBatch.End();

        // Accumulate and log every ProfileInterval frames
        _accumQueryTicks += t1 - t0;
        _accumInstEndTicks += t2 - t1;
        _accumTiles += drawnTiles;
        _profileFrame++;

        if (_profileFrame >= ProfileInterval)
        {
            double avgQueryMs = _accumQueryTicks * 1000.0 / Stopwatch.Frequency / ProfileInterval;
            double avgInstEndMs =
                _accumInstEndTicks * 1000.0 / Stopwatch.Frequency / ProfileInterval;
            int avgTiles = _accumTiles / ProfileInterval;
            Log.Warn(
                "Draw (avg {0}f): query={1:F3}ms ({2} tiles, {3:F1}ns/tile)  instEnd={4:F3}ms",
                ProfileInterval,
                avgQueryMs,
                avgTiles,
                avgTiles > 0 ? (avgQueryMs * 1_000_000.0 / avgTiles) : 0,
                avgInstEndMs
            );
            _profileFrame = 0;
            _accumQueryTicks = 0;
            _accumInstEndTicks = 0;
            _accumTiles = 0;
        }
    }

    /// <summary>
    /// Pure perspective math: converts a tile's grid position into screen position and scale,
    /// applying depth-based scaling toward the vanishing point.
    /// </summary>
    public static (Vector2 position, float scale) ComputeTileTransform(
        Vector3 worldCoordinate,
        float tileScale,
        float depthOffset,
        float playerZLevel,
        float tileWidth,
        float tileHeight,
        float globalScale,
        float depthStrength,
        Vector2 vanishingPoint
    )
    {
        Vector2 basePosition =
            new Vector2(worldCoordinate.X * tileWidth, worldCoordinate.Y * tileHeight)
            * tileScale
            * globalScale;

        float depth = (worldCoordinate.Z - playerZLevel) + depthOffset;
        float perspectiveScale = 1.0f + (depth * depthStrength);

        // Factored form of: VP + (basePos - VP) * pScale
        // Avoids catastrophic cancellation from (largePos - VP) subtraction
        Vector2 vpOffset = vanishingPoint * (1.0f - perspectiveScale);
        Vector2 finalPosition = vpOffset + (basePosition * perspectiveScale);

        float finalScale = tileScale * globalScale * perspectiveScale;

        return (finalPosition, finalScale);
    }
}
