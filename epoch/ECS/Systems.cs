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
    private Dictionary<Vector3, Entity> _grid = new();

    public void Register(Vector3 coord, Entity entity) => _grid[coord] = entity;

    public bool TryGet(Vector3 coord, out Entity entity) => _grid.TryGetValue(coord, out entity);

    public float GetMaxZLevel() => _grid.Keys.Max(v => v.Z);

    public float GetMinZLevel() => _grid.Keys.Min(v => v.Z);

    public float GetNumZLevels() => _grid.Keys.Select(v => v.Z).Distinct().Count();
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

    private float smoothTime = 100.00f;
    private float depthStrength = 0.03f;

    private Vector2 _currentVanishingPoint;
    private Vector2 _vanishingPointVelocity;

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
        float playerZLevel = pos.zLevel;

        // Get num z levels:
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
                var graphicalTile = graphicalTiles[index];

                // graphicalTile contains a name, referencing a tile in the TileManager,
                // and a color
                // Log.Debug("Drawing tile {0} at position {1}", graphicalTile.Name, position.Vec2);

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
                        smoothTime,
                        float.MaxValue,
                        gameTime.GetElapsedSeconds()
                    );

                    basePosition -= _currentVanishingPoint;

                    float perspectiveScale = 1.0f + (position.zLevel * depthStrength);

                    Vector2 finalPosition =
                        _currentVanishingPoint + (basePosition * perspectiveScale);

                    // Color should be the default in the tile definition, unless the GraphicalTile object holds an override
                    Color color = graphicalTile.SpriteColor ?? tileInfo.Value.Color;

                    float sortingLevel = 1 - ((position.zLevel + position.top) / numZLevels);

                    float layerDifference = position.zLevel - playerZLevel;

                    tileInfo.Value.TextureRegion.Draw(
                        Core.TileBatch,
                        finalPosition,
                        color,
                        0.0f,
                        Vector2.Zero,
                        graphicalTile.Scale * GlobalContext.GlobalScale * perspectiveScale,
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
    private float _moveDelay = 0.20f;
    private float _currentTimer = 0f;

    public MovementSystem(World world)
        : base(world) { }

    public override void Update(in GameTime gameTime)
    {
        float delta = gameTime.GetElapsedSeconds();

        if (_currentTimer > 0)
        {
            _currentTimer -= delta;
        }

        // Query all entities with a position and a movement component
        var queryDescription = new QueryDescription().WithAll<Position, MovementInput, Direction>();
        // TODO: check potential children?
        var query = World.Query(in queryDescription);
        foreach (ref var chunk in query.GetChunkIterator())
        {
            var entityParams = chunk.Entities;
            var references = chunk.GetFirst<Position, MovementInput, Direction>();

            foreach (var index in chunk)
            {
                var entity = entityParams[index];
                ref var position = ref Unsafe.Add(ref references.t0, index);
                ref var movementInput = ref Unsafe.Add(ref references.t1, index);
                ref var direction = ref Unsafe.Add(ref references.t2, index);

                bool canMove = true;

                if (World.TryGet(entity, out CompositeControllerComponent compositeController))
                {
                    foreach (var partID in compositeController.Parts.Values)
                    {
                        // TODO: random access/cache miss issues?
                        ref var childPos = ref World.Get<Position>(partID);
                        // TODO: add collision detection
                    }
                }
                // TODO: add collision detection

                if (canMove)
                {
                    Vector2 movementDirection = movementInput.Direction;

                    if (movementDirection == Vector2.Zero)
                    {
                        _currentTimer = 0f;
                        continue; // No movement input, skip
                    }

                    if (_currentTimer <= 0)
                    {
                        position.WorldCoordinate += movementDirection;

                        // TODO: update mapregistry?

                        // If movementDirection is not 0, set faceDirection equal to it.
                        // Otherwise, faceDirection stays the same
                        direction.FaceDirection = movementDirection;

                        // Move children if they exist
                        if (compositeController.Parts != null)
                        {
                            foreach (var partID in compositeController.Parts.Values)
                            {
                                ref var childPos = ref World.Get<Position>(partID);
                                childPos.WorldCoordinate += movementDirection;

                                // TODO: update mapregistry?
                            }
                        }
                        _currentTimer = _moveDelay;
                    }
                }
            }
        }
    }
}

public sealed class CameraLogicSystem : SystemBase<GameTime>
{
    private float smoothTime = 0.20f; // Time to move camera to target (player)
    private float zoomSpeed = 0.01f; // Speed of zooming
    private float lookSpeed = 15.0f; // Speed of looking around
    private float clampLength = 350.0f; // Max length of look direction

    private Vector2 _camVelocity;

    public CameraLogicSystem(World world)
        : base(world) { }

    public struct GetPlayerPos : IForEach<PlayerTag, Position>
    {
        public Vector2 Result;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Update(ref PlayerTag playerTag, ref Position pos)
        {
            Result = pos.WorldCoordinate;
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

    public TileAdjacencySystem(World world)
        : base(world) { }

    public override void Update(in GameTime gameTime)
    {
        var query = World.Query(in _entitiesToUpdate);

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

                    // For each direction, check if it's empty. If it is, there should be a border there.
                    // TODO: add logic for checking if the adjacent tile does not touch air. It it doesn't, we don't even render it, and there should also be a border here.
                    Vector2 currentCoord = position.WorldCoordinate;
                    Vector2[] directions = new Vector2[]
                    {
                        new Vector2(0, -1), // North
                        new Vector2(1, 0), // East
                        new Vector2(0, 1), // South
                        new Vector2(-1, 0), // West
                    };

                    for (int i = 0; i < directions.Length; i++)
                    {
                        Vector2 adjacentCoord = currentCoord + directions[i];
                        if (
                            !GlobalContext.MapRegistry.TryGet(
                                new Vector3(adjacentCoord.X, adjacentCoord.Y, position.zLevel),
                                out Entity _
                            )
                        )
                        {
                            // No tile in this direction, set the corresponding bit in the mask
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
