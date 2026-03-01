using Arch.Core;
using epoch.Graphics.Tiles;
using Microsoft.Xna.Framework;
using MonoGame.Extended;

namespace epoch.ECS;

/// <summary>
/// Static service locator holding shared game state: camera, player, tile manager, etc.
/// Populated once during <see cref="Scenes.WorldScene"/> initialization.
/// </summary>
/// <remarks>
/// This is effectively a global mutable singleton — convenient but blocks testability.
/// A future DI/context-object pass could replace it.
/// </remarks>
public static class GlobalContext
{
    /// <summary>Uniform scale multiplier applied to all tile rendering.</summary>
    public static float GlobalScale { get; set; } = 2.0f;

    public static OrthographicCamera Camera { get; set; }
    public static Entity CameraEntity { get; set; }
    public static Entity PlayerEntity { get; set; }
    public static ChunkRegistry ChunkRegistry { get; set; }
    public static TileManager TileManager { get; set; }
    public static EntityManager EntityManager { get; set; }

    /// <summary>Maximum Z level the world can store [0, MaxZ).</summary>
    public static int MaxZ { get; set; } = 128;
}
