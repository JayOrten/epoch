using System;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using Arch.Core;
using Arch.Core.Extensions;
using epoch.Graphics.Tiles;
using epoch.Graphics.Tiles.TileInstancing;
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
    // Secondary query for composite parts (player body parts that move with the camera)
    private readonly QueryDescription _compositePartsQuery = new QueryDescription().WithAll<
        CompositePartComponent,
        Position,
        GraphicalTileList
    >();

    private readonly Effect _renderShader;
    private readonly Effect _effectShader;

    private readonly RenderTarget2D _renderTarget2D;

    // Smoothed player Z for z-level transition animation
    private float _displayPlayerZ;
    private bool _playerZInitialized;

    // One-shot cache diagnostic: fires after first ProfileInterval frames
    private bool _cacheDiagDone;

    // Lightweight profiling: accumulate over N frames, log the average
    private const int ProfileInterval = 60;
    private int _profileFrame;
    private long _accumQueryTicks;
    private long _accumTileWorkTicks;
    private long _accumSortTicks;
    private long _accumUploadTicks;
    private int _accumTiles;
    private int _accumEntitiesVisited;
    private int _accumViewportCulled;

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
        // -- Pass 1: render tiles to render target --
        Core.GraphicsDevice.SetRenderTarget(_renderTarget2D);

        // Set background color
        Core.GraphicsDevice.Clear(new Color(24, 25, 38));

        // Interpolate camera state between previous and current fixed-update snapshots
        ref var cameraState = ref GlobalContext.CameraEntity.Get<CameraState>();
        ref var prevCam = ref GlobalContext.CameraEntity.Get<CameraPreviousState>();
        float alpha = Core.InterpolationAlpha;

        Vector2 drawPosition = Vector2.Lerp(prevCam.Position, cameraState.Position, alpha);
        float drawZoom = MathHelper.Lerp(prevCam.Zoom, GlobalContext.Camera.Zoom, alpha);
        float drawRotation = MathHelper.Lerp(prevCam.Rotation, cameraState.Rotation, alpha);
        float drawVpDistance = MathHelper.Lerp(prevCam.VpDistance, cameraState.VpDistance, alpha);

        // Temporarily apply interpolated values for view matrix and culling
        Vector2 savedPosition = GlobalContext.Camera.Position;
        float savedZoom = GlobalContext.Camera.Zoom;
        float savedRotation = GlobalContext.Camera.Rotation;
        GlobalContext.Camera.Position = drawPosition;
        GlobalContext.Camera.Zoom = drawZoom;
        GlobalContext.Camera.Rotation = drawRotation;

        // get the transformation for world -> screen space
        var viewMatrix = GlobalContext.Camera.GetViewMatrix();

        float rot = drawRotation;

        var tuning = TuningConfig.Instance;

        /// Compute elevation squash factor from VP distance
        float vpRatio = MathHelper.Clamp(drawVpDistance / tuning.MaxVpDistance, 0f, 1f);
        float zScale = MathHelper.Lerp(1.0f, tuning.MaxZScale, vpRatio);

        // Get projection matrix for projecting to CLIP space (-1 to 1)
        // Expand vertical range by zScale so the viewport sees more area along the
        // VP axis. Combined with the per-sprite stretch in the shader, tile bodies
        // stay the same size while Z-layer gaps compress — simulating a lower camera.
        float halfExtra = Core.GraphicsDevice.Viewport.Height * (zScale - 1f) / 2f;
        var projectionMatrix = Matrix.CreateOrthographicOffCenter(
            0,
            Core.GraphicsDevice.Viewport.Width,
            Core.GraphicsDevice.Viewport.Height + halfExtra,
            -halfExtra,
            0,
            1
        );

        // Combine them (Order matters: View * Projection)
        var finalTransform = viewMatrix * projectionMatrix;

        Core.TileInstancing.Begin(
            // sortMode: SpriteSortMode.BackToFront,
            effect: _renderShader,
            samplerState: SamplerState.LinearClamp
        );

        var transformParam = _renderShader.Parameters["WorldViewProjection"];
        var cameraZoomParam = _renderShader.Parameters["CameraZoom"];

        if (transformParam != null)
            transformParam.SetValue(finalTransform);
        if (cameraZoomParam != null)
            cameraZoomParam.SetValue(GlobalContext.Camera.Zoom);

        // -- DRAW TILES --
        // Get player z position and smooth z-level transitions
        ref var pos = ref GlobalContext.PlayerEntity.Get<Position>();
        float playerZLevel = pos.WorldCoordinate.Z;

        if (!_playerZInitialized)
        {
            _displayPlayerZ = playerZLevel;
            _playerZInitialized = true;
        }
        else
        {
            float lerpFactor = 1 - (float)Math.Pow(tuning.ZLerpRate, gameTime.GetElapsedSeconds());
            _displayPlayerZ = MathHelper.Lerp(_displayPlayerZ, playerZLevel, lerpFactor);
        }

        // VP orbits in world space with the camera rotation.
        // Combined with view matrix rotation, this keeps the VP at a fixed
        // screen-space position (below center) while the warp direction
        // tracks the viewing angle.
        Vector2 vanishingPoint =
            GlobalContext.Camera.Center
            + drawVpDistance * new Vector2(MathF.Sin(rot), MathF.Cos(rot));

        // Set perspective uniforms on the shader
        _renderShader.Parameters["VanishingPoint"]?.SetValue(vanishingPoint);
        _renderShader.Parameters["DepthStrength"]?.SetValue(tuning.DepthStrength);

        // Hoist globalScale early — needed for stack height and tile loop
        float globalScale = GlobalContext.GlobalScale;

        // Orthographic stack uniforms
        Vector2 stackDirection = new Vector2(-MathF.Sin(rot), -MathF.Cos(rot));
        float stackHeight = vpRatio * tuning.MaxStackHeight * globalScale;

        _renderShader.Parameters["StackDirection"]?.SetValue(stackDirection);
        _renderShader.Parameters["StackHeight"]?.SetValue(stackHeight);
        _renderShader.Parameters["VpBlend"]?.SetValue(tuning.VpBlendFactor);

        // Depth uniforms — shader computes perspectiveDepth and layerDifference from raw Z
        _renderShader.Parameters["DisplayPlayerZ"]?.SetValue(_displayPlayerZ);
        _renderShader.Parameters["PlayerZLevel"]?.SetValue(playerZLevel);
        _renderShader.Parameters["ZScale"]?.SetValue(zScale);

        // Hoist per-frame constants out of the hot loop
        var tileManager = GlobalContext.TileManager;
        var tileset = tileManager.Tileset;
        float tileWidth = tileset.TileWidth;
        float tileHeight = tileset.TileHeight;
        var tileInstancing = Core.TileInstancing;

        // Viewport culling: compute rotation-aware AABB in grid space.
        // BoundingRectangle doesn't account for view rotation, so we compute
        // the axis-aligned bounding box of the rotated viewport manually.
        float zoom = GlobalContext.Camera.Zoom;
        float halfW = Core.GraphicsDevice.Viewport.Width / (2f * zoom);
        float halfH = Core.GraphicsDevice.Viewport.Height * zScale / (2f * zoom);
        float absCos = MathF.Abs(MathF.Cos(cameraState.Rotation));
        float absSin = MathF.Abs(MathF.Sin(cameraState.Rotation));
        float rotHalfW = halfW * absCos + halfH * absSin;
        float rotHalfH = halfW * absSin + halfH * absCos;
        Vector2 camCenter = GlobalContext.Camera.Center;

        float worldToGridX = 1.0f / (tileWidth * globalScale);
        float worldToGridY = 1.0f / (tileHeight * globalScale);
        // Margin accounts for perspective offset at extreme Z + one tile of padding
        float cullMargin = 6.0f;
        float cullMinX = (camCenter.X - rotHalfW) * worldToGridX - cullMargin;
        float cullMaxX = (camCenter.X + rotHalfW) * worldToGridX + cullMargin;
        float cullMinY = (camCenter.Y - rotHalfH) * worldToGridY - cullMargin;
        float cullMaxY = (camCenter.Y + rotHalfH) * worldToGridY + cullMargin;

        // Compute visible chunk range from viewport AABB
        var registry = GlobalContext.ChunkRegistry;
        int chunkSize = registry.ChunkSize;
        int chunkMinX = (int)MathF.Floor(cullMinX / chunkSize);
        int chunkMaxX = (int)MathF.Floor(cullMaxX / chunkSize);
        int chunkMinY = (int)MathF.Floor(cullMinY / chunkSize);
        int chunkMaxY = (int)MathF.Floor(cullMaxY / chunkSize);

        // Viewport cull bounds in world-pixel space for draw cache entries
        float pixelCullMinX = cullMinX * tileWidth * globalScale;
        float pixelCullMaxX = cullMaxX * tileWidth * globalScale;
        float pixelCullMinY = cullMinY * tileHeight * globalScale;
        float pixelCullMaxY = cullMaxY * tileHeight * globalScale;

        long t0 = Stopwatch.GetTimestamp();
        long tileWorkTicks = 0;
        int drawnTiles = 0;
        int drawCacheEntries = 0;
        int viewportCulled = 0;

        // --- Primary loop: terrain via draw cache (pre-filtered, contiguous) ---
        for (int cx = chunkMinX; cx <= chunkMaxX; cx++)
        {
            for (int cy = chunkMinY; cy <= chunkMaxY; cy++)
            {
                var cache = registry.GetDrawCache(cx, cy, out int count);
                drawCacheEntries += count;

                for (int i = 0; i < count; i++)
                {
                    ref readonly var entry = ref cache[i];

                    if (entry.BasePosition.X < pixelCullMinX || entry.BasePosition.X > pixelCullMaxX ||
                        entry.BasePosition.Y < pixelCullMinY || entry.BasePosition.Y > pixelCullMaxY)
                    { viewportCulled++; continue; }

                    drawnTiles++;

                    tileInstancing.Draw(
                        entry.BasePosition,
                        entry.RawZ,
                        entry.SortDepth,
                        entry.BaseScale,
                        entry.Rotation,
                        entry.BorderMask,
                        entry.BorderWidth,
                        entry.EntityZ,
                        entry.SourceRect,
                        entry.Bg1,
                        entry.Bg2,
                        entry.Base,
                        entry.Accent,
                        entry.Border
                    );
                }
            }
        }

        // --- Secondary loop: composite parts (player body parts) ---
        // These move with the player and aren't in any chunk's packed list.
        // Uses uncached path since they move every frame.
        var compositeQuery = World.Query(in _compositePartsQuery);
        foreach (ref var chunk in compositeQuery)
        {
            var positions = chunk.GetArray<Position>();
            var graphicalTileLists = chunk.GetArray<GraphicalTileList>();

            foreach (var index in chunk)
            {
                ref var position = ref positions[index];
                ref var graphicalTileList = ref graphicalTileLists[index];

                long tileStart = Stopwatch.GetTimestamp();
                int drawn = DrawEntityTilesUncached(
                    ref position,
                    ref graphicalTileList,
                    globalScale,
                    tileWidth, tileHeight,
                    tileManager, tileset,
                    tileInstancing
                );
                tileWorkTicks += Stopwatch.GetTimestamp() - tileStart;
                drawnTiles += drawn;
            }
        }

        long t1 = Stopwatch.GetTimestamp();

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

        // Restore actual camera state (CameraApplySystem owns the authoritative values)
        GlobalContext.Camera.Position = savedPosition;
        GlobalContext.Camera.Zoom = savedZoom;
        GlobalContext.Camera.Rotation = savedRotation;

        // Accumulate and log every ProfileInterval frames
        long queryTicks = t1 - t0;
        _accumQueryTicks += queryTicks;
        _accumTileWorkTicks += tileWorkTicks;
        _accumSortTicks += tileInstancing.LastSortTicks;
        _accumUploadTicks += tileInstancing.LastUploadDrawTicks;
        _accumTiles += drawnTiles;
        _accumEntitiesVisited += drawCacheEntries;
        _accumViewportCulled += viewportCulled;
        _profileFrame++;

        if (_profileFrame >= ProfileInterval)
        {
            double toMs = 1000.0 / Stopwatch.Frequency / ProfileInterval;
            double avgQueryMs = _accumQueryTicks * toMs;
            double avgTileWorkMs = _accumTileWorkTicks * toMs;
            double avgIterMs = avgQueryMs - avgTileWorkMs;
            double avgSortMs = _accumSortTicks * toMs;
            double avgUploadMs = _accumUploadTicks * toMs;
            int avgTiles = _accumTiles / ProfileInterval;
            int avgCacheEntries = _accumEntitiesVisited / ProfileInterval;
            int avgViewCulled = _accumViewportCulled / ProfileInterval;
            Log.Warn(
                $"Draw (avg {ProfileInterval}f): "
                + $"cache: {avgCacheEntries} ({avgViewCulled} view-culled)  tiles: {avgTiles}  |  "
                + $"iterate={avgIterMs:F3}ms  "
                + $"tileWork={avgTileWorkMs:F3}ms  "
                + $"sort={avgSortMs:F3}ms  upload={avgUploadMs:F3}ms  "
                + $"total={avgQueryMs:F3}ms"
            );
            _profileFrame = 0;
            _accumQueryTicks = 0;
            _accumTileWorkTicks = 0;
            _accumSortTicks = 0;
            _accumUploadTicks = 0;
            _accumTiles = 0;
            _accumEntitiesVisited = 0;
            _accumViewportCulled = 0;

            // One-shot: report draw cache stats
            if (!_cacheDiagDone)
            {
                _cacheDiagDone = true;
                int totalCacheEntries = 0;
                var diagRegistry = GlobalContext.ChunkRegistry;
                foreach (var (cx, cy) in diagRegistry.LoadedChunks)
                {
                    diagRegistry.GetDrawCache(cx, cy, out int cacheCount);
                    totalCacheEntries += cacheCount;
                }
                Log.Warn($"[CacheDiag] draw cache: {totalCacheEntries} entries across {diagRegistry.LoadedChunks.Count} chunks");
            }
        }
    }

    /// <summary>
    /// Draws tiles for an entity using uncached computation (composite parts path).
    /// These entities move every frame so caching doesn't help.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static int DrawEntityTilesUncached(
        ref Position position,
        ref GraphicalTileList graphicalTileList,
        float globalScale,
        float tileWidth, float tileHeight,
        TileManager tileManager, Tileset tileset,
        TileInstancing tileInstancing)
    {
        const int MiddleMask =
            (1 << 0) | (1 << 1) | (1 << 2) | (1 << 3)
            | (1 << 6) | (1 << 7) | (1 << 8) | (1 << 9);

        bool hasMiddleExposure = (position.SpaceMask & MiddleMask) != 0;
        bool aboveIsOpen = (position.SpaceMask & (1 << 4)) != 0;

        if (!hasMiddleExposure && !aboveIsOpen)
            return 0;

        int lastIndex =
            graphicalTileList.ActiveTileMask == 0
                ? -1
                : 31 - System.Numerics.BitOperations.LeadingZeroCount(
                    (uint)graphicalTileList.ActiveTileMask);

        int drawnTiles = 0;
        int mask = graphicalTileList.ActiveTileMask;
        while (mask != 0)
        {
            int i = System.Numerics.BitOperations.TrailingZeroCount(mask);
            mask &= mask - 1;

            bool isTop = (i == lastIndex);
            if (isTop && !aboveIsOpen) continue;
            if (!isTop && !hasMiddleExposure) continue;

            drawnTiles++;
            ref var graphicalTile = ref graphicalTileList.Tiles[i];

            Tile tileInfo = tileManager.GetTile(graphicalTile.TileId);
            if (tileInfo == null) continue;

            (Rectangle sourceRect, float rotation) = tileset.GetTileRect(
                tileInfo.TileIndex, graphicalTile.AutoTileMask);

            Vector2 basePosition =
                new Vector2(
                    position.WorldCoordinate.X * tileWidth,
                    position.WorldCoordinate.Y * tileHeight
                ) * graphicalTile.Scale * globalScale;

            float baseScale = graphicalTile.Scale * globalScale;

            // Pass raw Z — shader computes perspectiveDepth and layerDifference
            float rawZ = position.WorldCoordinate.Z + graphicalTile.Offset;

            Color bg1, bg2, baseColor, accentColor, borderColor;
            int colorMask = graphicalTile.ColorOverrideMask;
            if (colorMask == 0)
            {
                bg1 = tileInfo.Background1Color;
                bg2 = tileInfo.Background2Color;
                baseColor = tileInfo.BaseColor;
                accentColor = tileInfo.AccentColor;
                borderColor = tileInfo.BorderColor;
            }
            else
            {
                bg1 = (colorMask & (1 << 0)) != 0
                    ? graphicalTile.Background1Color : tileInfo.Background1Color;
                bg2 = (colorMask & (1 << 1)) != 0
                    ? graphicalTile.Background2Color : tileInfo.Background2Color;
                baseColor = (colorMask & (1 << 2)) != 0
                    ? graphicalTile.BaseColor : tileInfo.BaseColor;
                accentColor = (colorMask & (1 << 3)) != 0
                    ? graphicalTile.AccentColor : tileInfo.AccentColor;
                borderColor = (colorMask & (1 << 4)) != 0
                    ? graphicalTile.BorderColor : tileInfo.BorderColor;
            }

            float sortDepth = position.WorldCoordinate.Z + graphicalTile.Offset + position.Top;
            float entityZ = position.WorldCoordinate.Z;

            tileInstancing.Draw(
                basePosition,
                rawZ,
                sortDepth,
                baseScale,
                rotation,
                graphicalTile.BorderMask,
                graphicalTile.BorderWidth,
                entityZ,
                sourceRect,
                bg1, bg2, baseColor, accentColor, borderColor
            );
        }
        return drawnTiles;
    }
}
