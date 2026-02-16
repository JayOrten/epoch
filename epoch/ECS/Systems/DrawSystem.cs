using System;
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
    private float _depthStrength = 0.03f;

    private Vector2 _currentVanishingPoint;
    private Vector2 _vanishingPointVelocity;

    // Controls draw position and scale smoothing
    private float _drawTime = 0.0001f;

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
        float numZLevels = GlobalContext.MapRegistry.GetNumZLevels();
        float lerpFactor = 1 - (float)Math.Pow(_drawTime, gameTime.GetElapsedSeconds());

        // Draw composite entities
        var compositeQuery = World.Query(in _compositeEntitiesToDraw);
        foreach (ref var chunk in compositeQuery)
        {
            var positions = chunk.GetArray<Position>();
            var graphicalTileLists = chunk.GetArray<GraphicalTileList>();

            foreach (var index in chunk)
            {
                var position = positions[index];
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

                    DrawTile(
                        ref graphicalTileList.Tiles[i],
                        ref position,
                        playerZLevel,
                        numZLevels,
                        lerpFactor
                    );
                }
            }
        }

        Core.TileInstancing.End();

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

    private void DrawTile(
        ref GraphicalTile graphicalTile,
        ref Position position,
        float playerZLevel,
        float numZLevels,
        float lerpFactor
    )
    {
        Tile? tileInfo = GlobalContext.TileManager.GetTile(graphicalTile.TileId);
        // tileInfo contains a TextureRegion and color string
        // If tileInfo is null, skip drawing
        if (tileInfo != null)
        {
            // Get texture region and rotation
            (TextureRegion region, float rotation) = GlobalContext.TileManager.Tileset.GetTile(
                tileInfo.TileIndex,
                graphicalTile.AutoTileMask
            );

            var (finalPosition, finalScale) = ComputeTileTransform(
                position.WorldCoordinate,
                graphicalTile.Scale,
                graphicalTile.Offset,
                playerZLevel,
                region.Width,
                region.Height,
                GlobalContext.GlobalScale,
                _depthStrength,
                _currentVanishingPoint
            );

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

            // Color should be the default in the tile definition, unless the GraphicalTile object holds an override
            Color background1Color = graphicalTile.Background1Color ?? tileInfo.Background1Color;
            Color background2Color = graphicalTile.Background2Color ?? tileInfo.Background2Color;
            Color baseColor = graphicalTile.BaseColor ?? tileInfo.BaseColor;
            Color accentColor = graphicalTile.AccentColor ?? tileInfo.AccentColor;
            Color borderColor = graphicalTile.BorderColor ?? tileInfo.BorderColor;

            float sortingLevel =
                1
                - ((position.WorldCoordinate.Z + graphicalTile.Offset + position.Top) / numZLevels);

            float layerDifference = position.WorldCoordinate.Z - playerZLevel;

            Core.TileInstancing.Draw(
                graphicalTile.CurrentDrawPosition, // Position
                sortingLevel, // Depth
                graphicalTile.CurrentDrawScale, // Scale
                rotation, // Rotation
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
