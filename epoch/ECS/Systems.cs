using System.Collections.Generic;
using System.Linq;
using Arch.Core;
using Arch.Core.Extensions;
using epoch.Engine;
using epoch.Engine.Graphics;
using epoch.Utilities;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using MonoGame.Extended;
using MonoGame.Extended.ViewportAdapters;

namespace epoch.ECS;

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

    private readonly SpriteBatch _batch;
    private readonly TileManager _tileManager;

    /// <summary>
    ///     Initializes a new instance of the <see cref="DrawSystem"/> class.
    /// </summary>
    /// <param name="world">Its <see cref="World"/>.</param>
    /// <param name="batch">The <see cref="SpriteBatch"/> used to draw all <see cref="Entity"/>s.</param>
    public DrawSystem(World world, TileManager tileManager)
        : base(world)
    {
        _batch = Core.SpriteBatch;
        _tileManager = tileManager;
    }

    /// <summary>
    ///     Gets called to execute the draw systems logic and to draw the <see cref="Entity"/>s.
    /// </summary>
    public override void Update(in DrawContext drawContext)
    {
        // First, query all components, and collect them, seperated by layers
        // Log.Debug("DrawSystem Update started.");
        Dictionary<float, List<(Position, GraphicalTile)>> layerTiles = new();

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

                float key = position.WorldCoordinate.Z;

                if (!layerTiles.ContainsKey(key))
                {
                    layerTiles[key] = new List<(Position, GraphicalTile)>();
                }

                layerTiles[key].Add((position, graphicalTile));
            }
        }

        float minLevel = layerTiles.Keys.Min();
        float maxLevel = layerTiles.Keys.Max();

        // Iterate through all collected tiles and draw in order of layer
        for (float i = minLevel; i <= maxLevel; i++)
        {
            foreach ((Position position, GraphicalTile graphicalTile) in layerTiles[i])
            {
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
                    float xPosition =
                        position.WorldCoordinate.X
                        * tileInfo.Value.TileWidth
                        * graphicalTile.Scale
                        * drawContext.GlobalScale;

                    float yPosition =
                        position.WorldCoordinate.Y
                        * tileInfo.Value.TileHeight
                        * graphicalTile.Scale
                        * drawContext.GlobalScale;

                    float depthStrength = 0.04f;
                    Vector2 vanishingPoint = drawContext.Center;

                    float dx = xPosition - vanishingPoint.X;
                    float dy = yPosition - vanishingPoint.Y;

                    float perspectiveScale = 1.0f + (position.WorldCoordinate.Z * depthStrength);

                    float finalX = vanishingPoint.X + (dx * perspectiveScale);
                    float finalY = vanishingPoint.Y + (dy * perspectiveScale);

                    Vector2 finalPosition = new Vector2(finalX, finalY);

                    // Color should be the default in the tile definition, unless the GraphicalTile object holds an override
                    Color color = graphicalTile.Color ?? tileInfo.Value.Color;

                    // Scale color by layer: lower layers should be darker
                    color = XnaColorExtensions.Darken(
                        color,
                        1 - (1.0f / (maxLevel + 1 - position.WorldCoordinate.Z))
                    );

                    tileInfo.Value.TextureRegion.Draw(
                        _batch,
                        finalPosition,
                        color,
                        0.0f,
                        Vector2.Zero,
                        graphicalTile.Scale * drawContext.GlobalScale * perspectiveScale,
                        SpriteEffects.None,
                        0.0f
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
            }
        }

        Vector3 playerPosition = _playerEntity.Get<Position>().WorldCoordinate;

        Vector2 playerPosition2D =
            new Vector2(playerPosition.X, playerPosition.Y)
            * playerMovementContext.TileScaleModifier;

        Vector2 targetPosition =
            playerPosition2D
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
