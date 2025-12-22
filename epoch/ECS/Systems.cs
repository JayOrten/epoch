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

/// <summary>
///     The <see cref="DrawSystem"/> class
///     ensures that all <see cref="Entity"/>s are drawn to the screen.
/// </summary>
public sealed class DrawSystem : SystemBase<DrawContext>
{
    private readonly QueryDescription _entitiesToDraw = new QueryDescription().WithAll<
        Position,
        GraphicalTile
    >();

    private readonly TileBatch _batch;
    private readonly TileManager _tileManager;
    private readonly Entity _playerEntity;
    private readonly OrthographicCamera _camera;
    private readonly MapRegistry _mapRegistry;

    private float smoothTime = 100.00f;
    private Vector2 _currentVanishingPoint;
    private Vector2 _vanishingPointVelocity;

    /// <summary>
    ///     Initializes a new instance of the <see cref="DrawSystem"/> class.
    /// </summary>
    /// <param name="world">Its <see cref="World"/>.</param>
    /// <param name="batch">The <see cref="SpriteBatch"/> used to draw all <see cref="Entity"/>s.</param>
    public DrawSystem(
        World world,
        TileManager tileManager,
        Entity playerEntity,
        OrthographicCamera camera,
        MapRegistry mapRegistry
    )
        : base(world)
    {
        _batch = Core.TileBatch;
        _tileManager = tileManager;
        _playerEntity = playerEntity;
        _camera = camera;
        _mapRegistry = mapRegistry;

        _currentVanishingPoint = _camera.Center;
    }

    /// <summary>
    ///     Gets called to execute the draw systems logic and to draw the <see cref="Entity"/>s.
    /// </summary>
    public override void Update(in DrawContext drawContext)
    {
        // Log.Debug("DrawSystem Update started.");

        // Get player z position
        ref var pos = ref _playerEntity.Get<Position>();
        float playerZLevel = pos.zLevel;

        // Get num z levels:
        float numZLevels = _mapRegistry.GetNumZLevels();

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

                // Only draw if the tile is on the current level.
                // if (position.WorldCoordinate.Z != drawContext.ZLevel)
                // {
                //     continue;
                // }
                // graphicalTile contains a name, referencing a tile in the TileManager,
                // and a color
                // Log.Debug("Drawing tile {0} at position {1}", graphicalTile.Name, position.Vec2);

                TileRenderInfo? tileInfo = _tileManager.GetTile(graphicalTile.TileId);
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
                        * drawContext.GlobalScale;

                    float depthStrength = 0.01f;

                    // Find vanishing point, based on direction
                    // Vector2 finalVanishingPoint =
                    //     _camera.Center + (400 * -1 * _playerEntity.Get<Direction>().FaceDirection);

                    // Find vanishing point, based on where pointer is
                    // 1. Get the mouse position in World Space immediately
                    Vector2 mouseWorld = _camera.ScreenToWorld(
                        GameController.MousePosition().ToVector2()
                    );

                    // 2. Calculate the vector from the Camera Center to the Mouse
                    Vector2 direction = mouseWorld - _camera.Center;

                    // 3. Invert (-1) and scale (1.5) the direction from the center
                    Vector2 finalVanishingPoint = _camera.Center - (direction * 1.5f);

                    // Calculate where the intermediate vanishing point is for this frame,
                    // based on where it currently is and where it should be.
                    // _currentVanishingPoint = Vector2.Lerp(
                    //     _currentVanishingPoint,
                    //     finalVanishingPoint,
                    //     0.000005f // fixed 10% movement per frame
                    // );

                    _currentVanishingPoint = CameraUtils.SmoothDamp(
                        _currentVanishingPoint,
                        finalVanishingPoint,
                        ref _vanishingPointVelocity,
                        smoothTime,
                        float.MaxValue,
                        drawContext.GameTime.GetElapsedSeconds()
                    );

                    basePosition -= _currentVanishingPoint;

                    float perspectiveScale = 1.0f + (position.zLevel * depthStrength);

                    Vector2 finalPosition =
                        _currentVanishingPoint + (basePosition * perspectiveScale);

                    // Color should be the default in the tile definition, unless the GraphicalTile object holds an override
                    Color color = graphicalTile.SpriteColor ?? tileInfo.Value.Color;

                    // Scale color by layer: lower layers should be darker
                    // color = XnaColorExtensions.Darken(
                    //     color,
                    //     1 - (1.0f / (_mapRegistry.GetMaxZLevel() + 1 - position.zLevel))
                    // );

                    float sortingLevel = 1 - ((position.zLevel + position.top) / numZLevels);

                    float layerDifference = position.zLevel - playerZLevel;

                    tileInfo.Value.TextureRegion.Draw(
                        _batch,
                        finalPosition,
                        color,
                        0.0f,
                        Vector2.Zero,
                        graphicalTile.Scale * drawContext.GlobalScale * perspectiveScale,
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
    }
}

public sealed class PlayerMovementSystem : SystemBase<PlayerMovementContext>
{
    private OrthographicCamera _camera;

    private Entity _playerEntity;

    private float _moveDelay = 0.20f;
    private float smoothTime = 0.20f;

    private float _currentTimer = 0f;

    private Vector2 _camVelocity;

    // TODO: don't love this here, need a seperate input system?
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

    public PlayerMovementSystem(World world, OrthographicCamera camera, Entity playerEntity)
        : base(world)
    {
        _camera = camera;
        _playerEntity = playerEntity;
    }

    public override void Update(in PlayerMovementContext playerMovementContext)
    {
        float delta = playerMovementContext.GameTime.GetElapsedSeconds();
        if (_currentTimer > 0)
        {
            _currentTimer -= delta;
        }

        if (_currentTimer <= 0)
        {
            Vector2 movementDirection = GetMovementDirection();

            if (movementDirection != Vector2.Zero)
            {
                ref var pos = ref _playerEntity.Get<Position>();
                var coord = pos.WorldCoordinate;
                coord.X += movementDirection.X;
                coord.Y += movementDirection.Y;
                pos.WorldCoordinate = coord;

                _currentTimer = _moveDelay;

                // If movementDirection is not 0, set faceDirection equal to it.
                // Otherwise, faceDirection stays the same
                _playerEntity.Get<Direction>().FaceDirection = movementDirection;
            }
        }

        // Move camera to follow player
        Vector2 playerPosition =
            _playerEntity.Get<Position>().WorldCoordinate * playerMovementContext.TileScaleModifier;

        Vector2 targetPosition =
            playerPosition
            - new Vector2(
                Core.Graphics.PreferredBackBufferWidth / 2,
                Core.Graphics.PreferredBackBufferHeight / 2
            );

        _camera.Position = CameraUtils.SmoothDamp(
            _camera.Position,
            targetPosition,
            ref _camVelocity,
            smoothTime,
            float.MaxValue,
            delta
        );
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

    private readonly MapRegistry _mapRegistry;

    public TileAdjacencySystem(World world, MapRegistry mapRegistry)
        : base(world)
    {
        _mapRegistry = mapRegistry;
    }

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
                            !_mapRegistry.TryGet(
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

public static class XnaColorExtensions
{
    public static Color Lighten(this Color color, float amount)
    {
        // 0.0f = no change, 1.0f = completey white
        return Color.Lerp(color, Color.White, amount);
    }

    public static Color Darken(this Color color, float amount)
    {
        // 0.0f = no change, 1.0f = completely black
        return Color.Lerp(color, Color.Black, amount);
    }
}
