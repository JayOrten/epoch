using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using Arch.Core;
using Arch.Core.Extensions;
using epoch.Engine;
using epoch.Engine.Graphics;
using epoch.Engine.Graphics.Tiles;
using epoch.Utilities;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using MonoGame.Extended;

namespace epoch.ECS;

/// World map registry for directly accessing entities on the grid
/// This is primarily useful for rendering, adjacency tests, directly talking to nearby entities, etc.
/// Local interactions
/// Need to figure out how to handle chunk loading/unloading, etc.
public class MapRegistry
{
    private World _world;

    private int _xSize;
    private int _ySize;
    private int _zSize;

    private Entity[] _entityMap;

    // 0 is passable, 1 is not passable
    private byte[] _collisionMap;

    private int GetIndex(int x, int y, int z) => x + (z * _xSize) + (y * _xSize * _zSize);

    private int GetIndex(Vector3 coord) =>
        (int)(coord.X + (coord.Z * _xSize) + (coord.Y * _xSize * _zSize));

    public MapRegistry(World world, int xSize, int ySize, int zSize)
    {
        _world = world;
        _xSize = xSize;
        _ySize = ySize;
        _zSize = zSize;

        int size = xSize * ySize * zSize;

        _entityMap = new Entity[size];
        // Create an entity with a AirTag component to represent empty space.
        Entity airEntity = world.Create(new AirTag());
        // Fill the entity map with this entity initially.
        Array.Fill(_entityMap, airEntity);

        _collisionMap = new byte[size];
    }

    public void Register(Vector3 coord, Entity entity)
    {
        int idx = GetIndex(coord);
        _entityMap[idx] = entity;

        // Get passability from entity's Position component
        ref var position = ref _world.Get<Position>(entity);
        _collisionMap[idx] = (byte)(position.Passable ? 0 : 1);
    }

    public Entity GetEntityAt(Vector3 coord)
    {
        // Check bounds of coordinate (otherwise it wraps)
        if (
            coord.X >= _xSize
            || coord.Y >= _ySize
            || coord.Z >= _zSize
            || coord.X < 0
            || coord.Y < 0
            || coord.Z < 0
        )
        {
            return Entity.Null;
        }

        int idx = GetIndex(coord);
        // If out of bounds, return Entity.Null
        if (idx < 0 || idx >= _entityMap.Length)
        {
            return Entity.Null;
        }

        Entity entity = _entityMap[idx];

        return entity;
    }

    public bool IsPassableAt(Vector3 coord)
    {
        if (
            coord.X >= _xSize
            || coord.Y >= _ySize
            || coord.Z >= _zSize
            || coord.X < 0
            || coord.Y < 0
            || coord.Z < 0
        )
        {
            return false;
        }

        int idx = GetIndex(coord);
        return _collisionMap[idx] == 0;
    }

    public int GetNumZLevels()
    {
        return _zSize;
    }
}

/// <summary>
///     The <see cref="SystemBase{T}"/> class
///     is a rudimentary basis for all systems with some important methods and properties.
/// </summary>
/// <typeparam name="T">The generic type passed to the <see cref="Update"/> method.</typeparam>
public abstract class SystemBase<T>
{
    /// <summary>
    ///     Initializes a new instance of the <see cref="SystemBase{T}"/> class.
    /// </summary>
    /// <param name="world">Its <see cref="World"/>.</param>
    protected SystemBase(World world)
    {
        World = world;
    }

    /// <summary>
    ///     The <see cref="World"/> for which this system works and must access.
    /// </summary>
    public World World { get; private set; }

    /// <summary>
    ///     Should be called within the update loop to update this system and executes its logic.
    /// </summary>
    /// <param name="state">A external state being passed to this method to be used.</param>
    public abstract void Update(in T state);
}

public sealed class InputSystem : SystemBase<GameTime>
{
    public InputSystem(World world)
        : base(world) { }

    private Vector2 GetMovementDirection()
    {
        var movementDirection = Vector2.Zero;

        if (GameController.MoveDownHeld())
        {
            movementDirection += Vector2.UnitY;
        }
        if (GameController.MoveUpHeld())
        {
            movementDirection -= Vector2.UnitY;
        }
        if (GameController.MoveLeftHeld())
        {
            movementDirection -= Vector2.UnitX;
        }
        if (GameController.MoveRightHeld())
        {
            movementDirection += Vector2.UnitX;
        }

        return movementDirection;
    }

    private Vector2 GetLookDirection()
    {
        var lookDirection = Vector2.Zero;

        if (GameController.LookDownHeld())
        {
            lookDirection -= Vector2.UnitY;
        }
        if (GameController.LookUpHeld())
        {
            lookDirection += Vector2.UnitY;
        }
        if (GameController.LookLeftHeld())
        {
            lookDirection += Vector2.UnitX;
        }
        if (GameController.LookRightHeld())
        {
            lookDirection -= Vector2.UnitX;
        }

        return lookDirection;
    }

    private float AdjustZoom()
    {
        float zoomChange = 0;

        if (GameController.ZoomInHeld())
        {
            zoomChange += 1;
        }

        if (GameController.ZoomOutHeld())
        {
            zoomChange -= 1;
        }

        return zoomChange;
    }

    public override void Update(in GameTime gametime)
    {
        // Read hardware
        // Movement
        Vector2 movementDirection = GetMovementDirection();
        // Update the MovementInput component of the player
        GlobalContext.PlayerEntity.Get<MovementInput>().Direction = movementDirection;

        // Look
        Vector2 lookChange = GetLookDirection();
        GlobalContext.CameraEntity.Get<CameraInput>().LookChange = lookChange;

        // Zoom
        float zoomChange = AdjustZoom();
        GlobalContext.CameraEntity.Get<CameraInput>().ZoomChange = zoomChange;
    }
}

/// <summary>
///     The <see cref="DrawSystem"/> class
///     ensures that all <see cref="Entity"/>s are drawn to the screen.
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
            // First add the bordermask to the tile id if auto tiling is on
            int tileId;
            if (graphicalTile.AutoTile)
            {
                tileId = tileInfo.TileIndex + graphicalTile.AutoTileMask;
            }
            else
            {
                tileId = tileInfo.TileIndex;
            }

            TextureRegion region = GlobalContext.TileManager.Tileset.GetTile(tileId);

            Vector2 basePosition =
                new Vector2(
                    position.WorldCoordinate.X * region.Width,
                    position.WorldCoordinate.Y * region.Height
                )
                * graphicalTile.Scale
                * GlobalContext.GlobalScale;

            // Depth=0 should always be the z level the player is on
            float depth = position.WorldCoordinate.Z - playerZLevel;

            // Add any given offset
            depth += graphicalTile.Offset;

            float perspectiveScale = 1.0f + (depth * _depthStrength);

            // Factored form of: VP + (basePos - VP) * pScale
            // Avoids catastrophic cancellation from the (largePos - VP) subtraction
            // that caused shimmering/gaps between adjacent tiles
            Vector2 vpOffset = _currentVanishingPoint * (1.0f - perspectiveScale);
            Vector2 finalPosition = vpOffset + (basePosition * perspectiveScale);

            // Initialize interpolation values if they haven't been set yet
            // TODO: this might be problematic
            if (graphicalTile.CurrentDrawPosition == Vector2.Zero)
            {
                graphicalTile.CurrentDrawPosition = finalPosition;
            }

            // Finally, we want to smoothly interpolate the final position from its current position to the target position
            // This prevents popping when moving up and down z levels
            if (graphicalTile.InterpolateMovement == true)
            {
                graphicalTile.CurrentDrawPosition = Vector2.Lerp(
                    graphicalTile.CurrentDrawPosition,
                    finalPosition,
                    lerpFactor
                );
            }
            else
            {
                graphicalTile.CurrentDrawPosition = finalPosition;
            }

            // Also, interpolate between scale changes
            float finalScale = graphicalTile.Scale * GlobalContext.GlobalScale * perspectiveScale;

            if (graphicalTile.CurrentDrawScale == 0.0f)
            {
                graphicalTile.CurrentDrawScale = finalScale;
            }

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
                - ((position.WorldCoordinate.Z + graphicalTile.Offset + position.top) / numZLevels);

            float layerDifference = position.WorldCoordinate.Z - playerZLevel;

            Core.TileInstancing.Draw(
                graphicalTile.CurrentDrawPosition, // Position
                sortingLevel, // Depth
                graphicalTile.CurrentDrawScale, // Scale
                0.0f, // Rotation
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

public sealed class MovementSystem : SystemBase<GameTime>
{
    public MovementSystem(World world)
        : base(world) { }

    public override void Update(in GameTime gameTime)
    {
        float delta = gameTime.GetElapsedSeconds();

        // Query all entities with a position and a movement component
        var queryDescription = new QueryDescription().WithAll<
            Position,
            MovementInput,
            Movement,
            Direction
        >();
        var query = World.Query(in queryDescription);
        foreach (ref var chunk in query.GetChunkIterator())
        {
            var entityParams = chunk.Entities;
            var references = chunk.GetFirst<Position, MovementInput, Movement, Direction>();

            foreach (var index in chunk)
            {
                // Get components for current entity
                var entity = entityParams[index];
                ref var position = ref Unsafe.Add(ref references.t0, index);
                ref var movementInput = ref Unsafe.Add(ref references.t1, index);
                ref var movement = ref Unsafe.Add(ref references.t2, index);
                ref var direction = ref Unsafe.Add(ref references.t3, index);

                // 0. Decrease the current timer, regardless of state
                if (movement.CurrentTimer > 0)
                {
                    movement.CurrentTimer -= delta;
                }

                // 1. IF the tile has valid movement input
                Vector2 movementDirection = movementInput.Direction;

                if (movementDirection == Vector2.Zero)
                {
                    movement.CurrentTimer = 0f;
                    continue; // No movement input, skip
                }

                // 2. IF the movement timer is ready
                if (movement.CurrentTimer <= 0)
                {
                    // 3. IF the tile CAN move (collision detection)
                    bool canMove = true;

                    // Check the parent tile first
                    // Logic here
                    // Get next x/y coord
                    // 5 cases:
                    // 1. It's empty, the coordinate below is not.
                    // 2. It's empty, the coordinate below is.
                    // 3. It's not passable, the coordinate above is.
                    // 4. It's not passable, the coordinate above is not passable.
                    // 5. It's passable (automatically move forward? Flat ground?)
                    // So, what we really need to know is:
                    // 1. The passability of the tile
                    // 2. The passability of the tile above it
                    // 3. The coordinate of the next non passable tile below it (but only if the original tile is passable)
                    Vector3 newCoordinate =
                        position.WorldCoordinate + new Vector3(movementDirection, 0.0f);

                    if (!GlobalContext.MapRegistry.IsPassableAt(newCoordinate))
                    {
                        // Check coordinate above
                        Vector3 coordinateAbove = newCoordinate;
                        coordinateAbove.Z++;

                        if (GlobalContext.MapRegistry.IsPassableAt(coordinateAbove))
                        {
                            // Move up
                            newCoordinate.Z++;
                        }
                        else
                        {
                            // Otherwise, you can't move.
                            canMove = false;
                        }
                    }
                    else if (GlobalContext.MapRegistry.GetEntityAt(newCoordinate).Has<AirTag>())
                    {
                        // Get coordinate below
                        Vector3 coordinateBelow = newCoordinate;
                        coordinateBelow.Z--;

                        if (GlobalContext.MapRegistry.GetEntityAt(coordinateBelow).Has<AirTag>())
                        {
                            Log.Info("uh oh, falling!");
                            // TODO: add falling. For now, it just moves down one
                            newCoordinate.Z--;
                        }
                        else
                        {
                            // Move down the slope
                            // newCoordinate.Z--;
                        }
                    }

                    // Check the children tiles at each offset
                    bool hasComposite = World.TryGet(
                        entity,
                        out CompositeControllerComponent compositeController
                    );

                    if (canMove && hasComposite)
                    {
                        foreach (Vector3 childOffset in compositeController.ChildOffsets)
                        {
                            Vector3 newChildCoordinate = childOffset + newCoordinate;

                            if (!GlobalContext.MapRegistry.IsPassableAt(newChildCoordinate))
                            {
                                canMove = false;
                                break;
                            }
                        }
                    }

                    if (canMove)
                    {
                        position.WorldCoordinate = newCoordinate;

                        // TODO: update mapregistry?

                        // If movementDirection is not 0, set faceDirection equal to it.
                        // Otherwise, faceDirection stays the same
                        direction.FaceDirection = movementDirection;

                        // Move children if they exist
                        if (hasComposite)
                        {
                            foreach (Entity childEntity in compositeController.Parts.Values)
                            {
                                ref var childPosition = ref World.Get<Position>(childEntity);
                                childPosition.WorldCoordinate =
                                    newCoordinate + childPosition.Offset;

                                // TODO: update mapregistry?
                            }
                        }
                        movement.CurrentTimer = movement.MoveDelay;
                    }
                }
            }
        }
    }
}

public sealed class CameraLogicSystem : SystemBase<GameTime>
{
    private float smoothTime = 0.45f; // Time to move camera to target (player)
    private float zoomSpeed = 0.01f; // Speed of zooming
    private float lookSpeed = 15.0f; // Speed of looking around
    private float clampLength = 500.0f; // Max length of look direction

    private Vector2 _camVelocity;
    private Vector2 _leadOffset;
    private float _leadRampUp = 2.0f; // How fast the lead engages
    private float _leadRampDown = 1.0f; // How fast the lead disengages (slower = gentler snap-back)

    public CameraLogicSystem(World world)
        : base(world) { }

    public override void Update(in GameTime gameTime)
    {
        float delta = gameTime.GetElapsedSeconds();

        ref var playerPos = ref GlobalContext.PlayerEntity.Get<Position>();
        ref var movementInput = ref GlobalContext.PlayerEntity.Get<MovementInput>();

        Vector2 playerGridPos = new Vector2(
            playerPos.WorldCoordinate.X,
            playerPos.WorldCoordinate.Y
        );

        // Predictive lead: smoothly ramp toward one tile ahead when holding a direction,
        // smoothly decay back to zero when released
        Vector2 targetLead = Vector2.Zero;
        if (movementInput.Direction != Vector2.Zero)
        {
            Vector3 predictedCoord =
                playerPos.WorldCoordinate + new Vector3(movementInput.Direction, 0);

            if (
                GlobalContext.MapRegistry.IsPassableAt(predictedCoord)
                || GlobalContext.MapRegistry.IsPassableAt(predictedCoord + new Vector3(0, 0, 1))
            )
            {
                targetLead = movementInput.Direction;
            }
        }

        float rampSpeed = (targetLead != Vector2.Zero) ? _leadRampUp : _leadRampDown;
        _leadOffset = Vector2.Lerp(_leadOffset, targetLead, rampSpeed * delta);

        Vector2 playerPosition =
            (playerGridPos + _leadOffset)
            * GlobalContext.GlobalScale
            * GlobalContext.TileManager.Tileset.TileHeight;

        Vector2 targetPosition =
            playerPosition
            - new Vector2(
                Core.Graphics.PreferredBackBufferWidth / 2,
                Core.Graphics.PreferredBackBufferHeight / 2
            );

        GlobalContext.CameraEntity.Get<CameraState>().Position = CameraUtils.SmoothDamp(
            GlobalContext.CameraEntity.Get<CameraState>().Position,
            targetPosition,
            ref _camVelocity,
            smoothTime,
            float.MaxValue,
            gameTime.GetElapsedSeconds()
        );

        // Apply zoom
        float zoomChange = GlobalContext.CameraEntity.Get<CameraInput>().ZoomChange;
        GlobalContext.CameraEntity.Get<CameraState>().ZoomAmount += (zoomChange * zoomSpeed);
        GlobalContext.CameraEntity.Get<CameraInput>().ZoomChange = 0; // Reset after applying

        // Apply Look Direction
        Vector2 currentLookDirection = GlobalContext.CameraEntity.Get<CameraState>().LookDirection;
        Vector2 lookChange = GlobalContext.CameraEntity.Get<CameraInput>().LookChange;

        // 1. Calculate the tentative new position
        Vector2 newLook = currentLookDirection + (lookChange * lookSpeed);

        // 2. CIRCULAR CLAMP
        // We check LengthSquared() because it is faster than Length() (avoids square root)
        if (newLook.LengthSquared() > clampLength * clampLength)
        {
            // Normalize gets the direction (length of 1), then we multiply by radius
            newLook = Vector2.Normalize(newLook) * clampLength;
        }

        GlobalContext.CameraEntity.Get<CameraState>().LookDirection = newLook;

        // TODO: add previous state update for different refresh rates?
    }
}

public sealed class CameraApplySystem : SystemBase<GameTime>
{
    public CameraApplySystem(World world)
        : base(world) { }

    public override void Update(in GameTime gameTime)
    {
        // Apply camera state to actual camera
        ref var cameraState = ref GlobalContext.CameraEntity.Get<CameraState>();

        // Snap camera position to pixel boundaries to prevent tile seams/flickering
        float pixelSize = GlobalContext.TileManager.Tileset.TileWidth * GlobalContext.GlobalScale;
        Vector2 snappedPosition = new Vector2(
            MathF.Round(cameraState.Position.X * pixelSize) / pixelSize,
            MathF.Round(cameraState.Position.Y * pixelSize) / pixelSize
        );
        GlobalContext.Camera.Position = snappedPosition;

        if (cameraState.ZoomAmount > 0)
        {
            GlobalContext.Camera.ZoomIn(cameraState.ZoomAmount);
        }
        else if (cameraState.ZoomAmount < 0)
        {
            GlobalContext.Camera.ZoomOut(-cameraState.ZoomAmount);
        }
        cameraState.ZoomAmount = 0; // Reset after applying
    }
}

/// Updates borderbuffer for dirty tiles
/// TODO: consider incorporating into draw system?
public sealed class TileAdjacencySystem : SystemBase<GameTime>
{
    private readonly QueryDescription _entitiesToUpdate2 = new QueryDescription().WithAll<
        Position,
        GraphicalTileList,
        DirtyTag
    >();

    // 26 directions for full 3D neighborhood (3x3x3 cube minus center)
    // Coordinate system: X = East(+)/West(-), Y = South(+)/North(-), Z = Above(+)/Below(-)
    // Bit indices correspond to array index for SpaceMask
    private static readonly Vector3[] _directions = new Vector3[]
    {
        // Faces (bits 0-5)
        new Vector3(0, -1, 0), // 0:  North
        new Vector3(1, 0, 0), // 1:  East
        new Vector3(0, 1, 0), // 2:  South
        new Vector3(-1, 0, 0), // 3:  West
        new Vector3(0, 0, 1), // 4:  Above
        new Vector3(0, 0, -1), // 5:  Below
        // Edges - horizontal (bits 6-9)
        new Vector3(1, -1, 0), // 6:  North-East
        new Vector3(1, 1, 0), // 7:  South-East
        new Vector3(-1, 1, 0), // 8:  South-West
        new Vector3(-1, -1, 0), // 9:  North-West
        // Edges - vertical north/south (bits 10-13)
        new Vector3(0, -1, 1), // 10: North-Above
        new Vector3(0, -1, -1), // 11: North-Below
        new Vector3(0, 1, 1), // 12: South-Above
        new Vector3(0, 1, -1), // 13: South-Below
        // Edges - vertical east/west (bits 14-17)
        new Vector3(1, 0, 1), // 14: East-Above
        new Vector3(1, 0, -1), // 15: East-Below
        new Vector3(-1, 0, 1), // 16: West-Above
        new Vector3(-1, 0, -1), // 17: West-Below
        // Corners - above (bits 18-21)
        new Vector3(1, -1, 1), // 18: North-East-Above
        new Vector3(1, 1, 1), // 19: South-East-Above
        new Vector3(-1, 1, 1), // 20: South-West-Above
        new Vector3(-1, -1, 1), // 21: North-West-Above
        // Corners - below (bits 22-25)
        new Vector3(1, -1, -1), // 22: North-East-Below
        new Vector3(1, 1, -1), // 23: South-East-Below
        new Vector3(-1, 1, -1), // 24: South-West-Below
        new Vector3(-1, -1, -1), // 25: North-West-Below
    };

    // Maps cardinal direction index (0-3: N, E, S, W) to the
    // above-adjacent bit index in the space mask
    private static readonly int[] _aboveAdjacentBits = { 10, 14, 12, 16 };

    public TileAdjacencySystem(World world)
        : base(world) { }

    public override void Update(in GameTime gameTime)
    {
        // TODO: first of all, this logic sucks
        var commandBuffer = new Arch.Buffer.CommandBuffer();
        var query = World.Query(in _entitiesToUpdate2);

        // Pass 1: calculate space masks for dirty tiles
        foreach (ref var chunk in query.GetChunkIterator())
        {
            var references = chunk.GetFirst<Position, GraphicalTileList, DirtyTag>();

            foreach (var entity in chunk)
            {
                ref var graphicalTileList = ref Unsafe.Add(ref references.t1, entity);
                ref var position = ref Unsafe.Add(ref references.t0, entity);

                // ====== CALCULATE SPACE MASK ======
                // The space mask represents open spaces with a marked bit (1)
                int mask = 0;

                for (int i = 0; i < _directions.Length; i++)
                {
                    Vector3 adjacentCoord = position.WorldCoordinate + _directions[i];
                    var entityAtAdjacent = GlobalContext.MapRegistry.GetEntityAt(adjacentCoord);
                    if (
                        entityAtAdjacent != Entity.Null
                        && (
                            entityAtAdjacent.Has<AirTag>()
                            || entityAtAdjacent.Get<Position>().Passable == true
                        )
                    )
                    {
                        mask |= (1 << i);
                    }
                }
                position.SpaceMask = mask;

                // ===== UPDATE BORDER MASKS =====
                // Each solid tile can have up to 3 border masks controlling
                // edge rendering on its sub-tiles:
                //   middleMask - which cardinal sides (N/E/S/W) face open space
                //   topMask    - which cardinal sides need a top edge drawn
                //   bottomMask - which cardinal sides need a bottom edge drawn
                //
                // All derived from the space mask (bits 0-5 = N/E/S/W/Above/Below,
                // bits 10/14/12/16 = North-Above/East-Above/South-Above/West-Above)
                if (position.IsBlock)
                {
                    // middleMask: bottom 4 bits of space mask = cardinal openness
                    int middleMask = mask & 0xF;
                    bool aboveIsOpen = (mask & (1 << 4)) != 0;
                    bool belowIsOpen = (mask & (1 << 5)) != 0;

                    // bottomMask: a bottom edge exists on a side only if both
                    // the tile below AND the adjacent tile are open
                    int bottomMask = belowIsOpen ? middleMask : 0;

                    // topMask: a top edge exists on a side when the tile above
                    // is open AND one of two cases holds for that direction:
                    //   Outer edge - the adjacent tile is open (exposed corner)
                    //   Inner edge - the adjacent tile is solid BUT the tile
                    //                above-adjacent is also solid (step/ledge)
                    // Combined: aboveOpen && (adjacentOpen || !aboveAdjacentOpen)
                    int topMask = 0;
                    if (aboveIsOpen)
                    {
                        for (int i = 0; i < 4; i++)
                        {
                            bool adjacentOpen = (middleMask & (1 << i)) != 0;
                            bool aboveAdjacentOpen = (mask & (1 << _aboveAdjacentBits[i])) != 0;

                            if (adjacentOpen || !aboveAdjacentOpen)
                            {
                                topMask |= (1 << i);
                            }
                        }
                    }

                    // Iterate through each tile and set masks
                    for (int i = 0; i < graphicalTileList.Tiles.Length; i++)
                    {
                        ref var tile = ref graphicalTileList.Tiles[i];

                        if (tile.BorderType != BorderType.None)
                        {
                            tile.BorderMask = tile.BorderType switch
                            {
                                BorderType.Top => topMask,
                                BorderType.Bottom => bottomMask,
                                _ => 0,
                            };
                        }

                        if (tile.AutoTile)
                            tile.AutoTileMask = middleMask;
                    }
                }

                commandBuffer.Remove<DirtyTag>(chunk.Entity(entity));
            }
        }

        commandBuffer.Playback(World, true);
    }
}
