using Arch.Core;
using epoch.Engine.Graphics.Tiles;
using Microsoft.Xna.Framework;
using MonoGame.Extended;

namespace epoch.ECS;

public static class GlobalContext
{
    // Global Settings
    public static float GlobalScale { get; set; } = 4.0f;

    // Object/entity references
    public static OrthographicCamera Camera { get; set; }
    public static Entity CameraEntity { get; set; }
    public static Entity PlayerEntity { get; set; }
    public static MapRegistry MapRegistry { get; set; }
    public static TileManager TileManager { get; set; }
    public static EntityManager EntityManager { get; set; }
}
