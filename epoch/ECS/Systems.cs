using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using Arch.Core;
using Arch.Core.Extensions;
using epoch.Engine;
using epoch.Engine.Graphics.Tiles;
using epoch.Engine.Graphics.Tiles.TileBatches;
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

        return _entityMap[idx];
    }

    public bool IsPassableAt(Vector3 coord)
    {
        int idx = GetIndex(coord);
        // If out of bounds, return false (not passable)
        if (idx < 0 || idx >= _collisionMap.Length)
        {
            return false;
        }
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
    private readonly QueryDescription _entitiesToDraw = new QueryDescription().WithAll<
        Position,
        GraphicalTile
    >();

    private readonly Effect _uberShader;
    private readonly Effect _screenEffect;

    private readonly RenderTarget2D _renderTarget2D;

    // Controls vanishing point time smoothing
    private float _smoothTime = 100.00f;
    private float _depthStrength = 0.02f;

    private Vector2 _currentVanishingPoint;
    private Vector2 _vanishingPointVelocity;

    // Controls draw position and scale smoothing
    private float _drawTime = 0.00001f;

    /// <summary>
    ///     Initializes a new instance of the <see cref="DrawSystem"/> class.
    /// </summary>
    /// <param name="world">Its <see cref="World"/>.</param>
    /// <param name="batch">The <see cref="SpriteBatch"/> used to draw all <see cref="Entity"/>s.</param>
    public DrawSystem(World world, Effect uberShader, Effect screenEffect)
        : base(world)
    {
        _uberShader = uberShader;
        _screenEffect = screenEffect;

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
        // Log.Debug("DrawSystem Update started.");

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
            -1
        );

        // Combine them (Order matters: View * Projection)
        var finalTransform = viewMatrix * projectionMatrix;

        Core.TileBatch.Begin(
            sortMode: SpriteSortMode.BackToFront,
            effect: _uberShader,
            samplerState: SamplerState.PointClamp
        );

        var transformParam = _uberShader.Parameters["WorldViewProjection"];
        var textureSizeParam = _uberShader.Parameters["TextureSize"];
        var tileSizeParam = _uberShader.Parameters["TileSize"];
        var cameraZoomParam = _uberShader.Parameters["CameraZoom"];
        var viewportParam = _uberShader.Parameters["ViewportSize"];

        if (transformParam != null)
            transformParam.SetValue(finalTransform);
        if (textureSizeParam != null)
            textureSizeParam.SetValue(
                new Vector2(
                    GlobalContext.TileManager.TileSet.Rows
                        * GlobalContext.TileManager.TileSet.TileHeight,
                    GlobalContext.TileManager.TileSet.Columns
                        * GlobalContext.TileManager.TileSet.TileWidth
                )
            );
        if (tileSizeParam != null)
            tileSizeParam.SetValue(
                new Vector2(
                    GlobalContext.TileManager.TileSet.TileHeight,
                    GlobalContext.TileManager.TileSet.TileWidth
                )
            ); // Size of ONE
        if (cameraZoomParam != null)
            cameraZoomParam.SetValue(GlobalContext.Camera.Zoom);
        if (viewportParam != null)
            viewportParam.SetValue(
                new Vector2(Core.GraphicsDevice.Viewport.Width, Core.GraphicsDevice.Viewport.Height)
            );

        // -- DRAW TILES --
        // Get player z position
        ref var pos = ref GlobalContext.PlayerEntity.Get<Position>();
        float playerZLevel = pos.WorldCoordinate.Z;

        // Get num z levels:
        // TODO: need to figure out actual drawing culling. Need to get which z are actually shown.
        float numZLevels = GlobalContext.MapRegistry.GetNumZLevels();

        // Get query for the description, targets all entities with "Positions" and "Sprite".
        var query = World.Query(in _entitiesToDraw);
        foreach (ref var chunk in query) // Iterate over each chunk that has entities that fit the query.
        {
            // Log.Debug("Processing chunk with {0} entities", chunk.Count);
            // Receive raw arrays of positions and sprites from the chunk.
            // chunk.GetArray<Position, GraphicalTile>(out var positions, out var graphicalTiles);
            var positions = chunk.GetArray<Position>();
            var graphicalTiles = chunk.GetArray<GraphicalTile>();

            // Loop over the chunk
            foreach (var index in chunk)
            {
                // Get refs to position and sprite.
                // ref var position = ref positions[index]; // IS NULL
                // ref var graphicalTile = ref graphicalTiles[index]; // IS POSITION OBJ
                var position = positions[index];
                ref var graphicalTile = ref graphicalTiles[index];

                // First, check if we actually want to draw the tile
                // Check each bit of the border mask. If any bit is set, we need to draw the tile
                if (graphicalTile.SpaceMask == 0 && graphicalTile.SpriteColor == null)
                {
                    continue; // No border to draw, skip
                    // TODO: maybe add a "force draw" flag instead?
                }

                TileRenderInfo? tileInfo = GlobalContext.TileManager.GetTile(graphicalTile.TileId);
                // tileInfo contains a TextureRegion and color string
                // If tileInfo is null, skip drawing
                if (tileInfo != null)
                {
                    Vector2 basePosition =
                        new Vector2(
                            position.WorldCoordinate.X * tileInfo.Value.TileWidth,
                            position.WorldCoordinate.Y * tileInfo.Value.TileHeight
                        )
                        * graphicalTile.Scale
                        * GlobalContext.GlobalScale;

                    Vector2 finalVanishingPoint =
                        GlobalContext.Camera.Center
                        + (GlobalContext.CameraEntity.Get<CameraState>().LookDirection);

                    // Calculate where the intermediate vanishing point is for this frame,
                    // based on where it currently is and where it should be.
                    _currentVanishingPoint = CameraUtils.SmoothDamp(
                        _currentVanishingPoint,
                        finalVanishingPoint,
                        ref _vanishingPointVelocity,
                        _smoothTime,
                        float.MaxValue,
                        gameTime.GetElapsedSeconds()
                    );

                    // This puts the current vanishing point at (0,0) for easier calculations
                    basePosition -= _currentVanishingPoint;

                    // Depth=0 should always be the z level the player is on
                    float depth = position.WorldCoordinate.Z - playerZLevel;

                    float perspectiveScale = 1.0f + (depth * _depthStrength);

                    Vector2 finalPosition =
                        _currentVanishingPoint + (basePosition * perspectiveScale);

                    // Initialize interpolation values if they haven't been set yet
                    // TODO: this might be problematic
                    if (graphicalTile.CurrentDrawPosition == Vector2.Zero)
                    {
                        graphicalTile.CurrentDrawPosition = finalPosition;
                    }

                    // Finally, we want to smoothly interpolate the final position from its current position to the target position
                    // This prevents popping when moving up and down z levels
                    // Note: this adds a "sluggish" or "sliding" effect when moving
                    // graphicalTile.CurrentDrawPosition = CameraUtils.SmoothDamp(
                    //     graphicalTile.CurrentDrawPosition,
                    //     finalPosition,
                    //     ref graphicalTile.DrawPositionVelocity,
                    //     0.075f,
                    //     float.MaxValue,
                    //     gameTime.GetElapsedSeconds()
                    // );
                    graphicalTile.CurrentDrawPosition = Vector2.Lerp(
                        graphicalTile.CurrentDrawPosition,
                        finalPosition,
                        1 - (float)Math.Pow(_drawTime, gameTime.GetElapsedSeconds())
                    );

                    // float distanceToTarget = Vector2.Distance(
                    //     graphicalTile.CurrentDrawPosition,
                    //     finalPosition
                    // );
                    // float moveAmount = 250f * gameTime.GetElapsedSeconds(); // Speed factor
                    // if (distanceToTarget <= moveAmount)
                    // {
                    //     graphicalTile.CurrentDrawPosition = finalPosition;
                    // }
                    // else
                    // {
                    //     graphicalTile.CurrentDrawPosition = Vector2.Lerp(
                    //         graphicalTile.CurrentDrawPosition,
                    //         finalPosition,
                    //         moveAmount / distanceToTarget
                    //     );
                    // }

                    // Also, interpolate between scale changes
                    float finalScale =
                        graphicalTile.Scale * GlobalContext.GlobalScale * perspectiveScale;

                    if (graphicalTile.CurrentDrawScale == 0.0f)
                    {
                        graphicalTile.CurrentDrawScale = finalScale;
                    }

                    graphicalTile.CurrentDrawScale = MathHelper.Lerp(
                        graphicalTile.CurrentDrawScale,
                        finalScale,
                        1 - (float)Math.Pow(_drawTime, gameTime.GetElapsedSeconds())
                    );

                    // Color should be the default in the tile definition, unless the GraphicalTile object holds an override
                    Color color = graphicalTile.SpriteColor ?? tileInfo.Value.Color;

                    float sortingLevel =
                        1 - ((position.WorldCoordinate.Z + position.top) / numZLevels);

                    float layerDifference = position.WorldCoordinate.Z - playerZLevel;

                    tileInfo.Value.TextureRegion.Draw(
                        Core.TileBatch,
                        graphicalTile.CurrentDrawPosition,
                        color,
                        0.0f,
                        Vector2.Zero,
                        graphicalTile.CurrentDrawScale,
                        SpriteEffects.None,
                        sortingLevel,
                        graphicalTile.BackgroundColor,
                        graphicalTile.BorderColor,
                        graphicalTile.BorderMask,
                        graphicalTile.BorderWidth,
                        layerDifference
                    );
                }
            }
        }
        Core.TileBatch.End();

        // -- Pass 2: Render Target to Screen with post-processing shader --

        Core.GraphicsDevice.SetRenderTarget(null);

        Core.SpriteBatch.Begin(effect: _screenEffect);

        var timeParam = _screenEffect.Parameters["Time"];
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
                            newCoordinate.Z--;
                        }
                    }

                    // Check the children tiles at each offset
                    if (canMove)
                    {
                        if (
                            World.TryGet(
                                entity,
                                out CompositeControllerComponent compositeController
                            )
                        )
                        {
                            foreach (Vector3 childOffset in compositeController.ChildOffsets)
                            {
                                Vector3 newChildCoordinate = childOffset + newCoordinate;

                                // Check if tile at newCoordiante is passable
                                if (!GlobalContext.MapRegistry.IsPassableAt(newChildCoordinate))
                                {
                                    canMove = false;
                                    break;
                                }
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
                        if (
                            World.TryGet(
                                entity,
                                out CompositeControllerComponent compositeController
                            )
                        )
                        {
                            for (int i = 0; i < compositeController.Parts.Count; i++)
                            {
                                Entity childEntity = compositeController.Parts.Values.ElementAt(i);
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

    public CameraLogicSystem(World world)
        : base(world) { }

    public struct GetPlayerPos : IForEach<PlayerTag, Position>
    {
        public Vector2 Result;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Update(ref PlayerTag playerTag, ref Position pos)
        {
            Result = new Vector2(pos.WorldCoordinate.X, pos.WorldCoordinate.Y);
        }
    }

    public override void Update(in GameTime gameTime)
    {
        // Apply movement (based on player)
        // Query for player entity to get position
        var playerPos = new GetPlayerPos();
        World.InlineQuery<GetPlayerPos, PlayerTag, Position>(
            in new QueryDescription().WithAll<PlayerTag, Position>(),
            ref playerPos
        );
        Vector2 playerPosition =
            playerPos.Result * GlobalContext.GlobalScale * GlobalContext.TileManager.TileHeight;

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

        GlobalContext.Camera.Position = cameraState.Position;

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
    private readonly QueryDescription _entitiesToUpdate = new QueryDescription().WithAll<
        Position,
        GraphicalTile
    >();

    private static readonly Vector3[] _directions = new Vector3[]
    {
        new Vector3(0, -1, 0), // North
        new Vector3(1, 0, 0), // East
        new Vector3(0, 1, 0), // South
        new Vector3(-1, 0, 0), // West
        new Vector3(0, 0, 1), // Above
        new Vector3(0, 0, -1), // Below
    };

    private static readonly Vector3[] _borderDirections = new Vector3[]
    {
        new Vector3(0, -1, 0), // North
        new Vector3(1, 0, 0), // East
        new Vector3(0, 1, 0), // South
        new Vector3(-1, 0, 0), // West
    };

    public TileAdjacencySystem(World world)
        : base(world) { }

    public override void Update(in GameTime gameTime)
    {
        var query = World.Query(in _entitiesToUpdate);

        // Two passes: one to calculate space masks for dirty tiles, one to update border buffer
        foreach (ref var chunk in query.GetChunkIterator())
        {
            var references = chunk.GetFirst<Position, GraphicalTile>();

            foreach (var entity in chunk)
            {
                ref var graphicalTile = ref Unsafe.Add(ref references.t1, entity);

                // If the tile is marked as dirty, run check with adjacent tiles
                // and update border buffer
                // TODO: use dirty tag, not isDirty boolean
                if (graphicalTile.IsDirty)
                {
                    ref var position = ref Unsafe.Add(ref references.t0, entity);

                    int mask = 0;

                    for (int i = 0; i < _directions.Length; i++)
                    {
                        Vector3 adjacentCoord = position.WorldCoordinate + _directions[i];
                        var entityAtAdjacent = GlobalContext.MapRegistry.GetEntityAt(adjacentCoord);
                        if (entityAtAdjacent != Entity.Null && entityAtAdjacent.Has<AirTag>())
                        {
                            mask |= (1 << i);
                        }
                    }

                    graphicalTile.SpaceMask = mask;
                }
            }
        }

        // Pass 2: update border masks

        foreach (ref var chunk in query.GetChunkIterator())
        {
            var references = chunk.GetFirst<Position, GraphicalTile>();

            foreach (var entity in chunk)
            {
                ref var graphicalTile = ref Unsafe.Add(ref references.t1, entity);

                // If the tile is marked as dirty, run check with adjacent tiles
                // and update border buffer
                if (graphicalTile.IsDirty)
                {
                    ref var position = ref Unsafe.Add(ref references.t0, entity);

                    int mask = 0;

                    // A border should be drawn if:
                    // 1. The adjacent tile is air
                    // 2. The adjacent tile's space mask is 0, indicating the neighbor is not touching air
                    for (int i = 0; i < _borderDirections.Length; i++)
                    {
                        Vector3 adjacentCoord = position.WorldCoordinate + _borderDirections[i];
                        var entityAtAdjacent = GlobalContext.MapRegistry.GetEntityAt(adjacentCoord);
                        // if (entityAtAdjacent != Entity.Null && entityAtAdjacent.Has<AirTag>())
                        if (
                            entityAtAdjacent != Entity.Null
                            && (
                                entityAtAdjacent.Has<AirTag>()
                                || (
                                    World.TryGet(
                                        entityAtAdjacent,
                                        out GraphicalTile adjacentGraphicalTile
                                    ) && (adjacentGraphicalTile.SpaceMask == 0)
                                )
                            )
                        )
                        {
                            mask |= (1 << i);
                        }
                    }

                    graphicalTile.BorderMask = mask;
                    graphicalTile.IsDirty = false;
                }
            }
        }
    }
}
