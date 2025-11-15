using Arch.Core;
using epoch.Components;
using epoch.Engine;
using epoch.Engine.Graphics;
using epoch.Utilities;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace epoch.Systems;

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
public sealed class DrawSystem : SystemBase<GameTime>
{
    private readonly QueryDescription _globalSettingsQuery =
        new QueryDescription().WithAll<GlobalSettings>();

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
    /// <param name="time">The <see cref="GameTime"/> being passed from outside the system.</param>
    public override void Update(in GameTime time)
    {
        Log.Debug("DrawSystem Update started.");
        // Get global scale from GlobalSettings singleton
        float globalScale = 1.0f;
        World.Query(
            in _globalSettingsQuery,
            (ref GlobalSettings settings) =>
            {
                globalScale = settings.GlobalScale;
            }
        );
        Log.Debug("Global scale is {0}", globalScale);
        // _batch.Begin();

        // Get query for the description, targets all entities with "Positions" and "Sprite".
        var query = World.Query(in _entitiesToDraw);
        foreach (ref var chunk in query) // Iterate over each chunk that has entities that fit the query.
        {
            // Receive raw arrays of positions and sprites from the chunk.
            chunk.GetSpan<Position, GraphicalTile>(out var positions, out var graphicalTiles);

            // Loop over the chunk
            foreach (var index in chunk)
            {
                // Get refs to position and sprite.
                ref var position = ref positions[index];
                ref var graphicalTile = ref graphicalTiles[index];
                // graphicalTile contains a name, referencing a tile in the TileManager,
                // and a color
                Log.Debug("Drawing tile {0} at position {1}", graphicalTile.Name, position.Vec2);

                TileRenderInfo? tileInfo = _tileManager.GetTile(graphicalTile.Name);
                // tileInfo contains a TextureRegion and color string
                // If tileInfo is null, skip drawing
                if (tileInfo != null)
                {
                    tileInfo.Value.TextureRegion.Draw(
                        _batch,
                        position.Vec2,
                        graphicalTile.Color,
                        0.0f,
                        Vector2.Zero,
                        graphicalTile.Scale * globalScale,
                        SpriteEffects.None,
                        0.0f
                    );
                }
            }
        }

        // _batch.End();
    }
}
