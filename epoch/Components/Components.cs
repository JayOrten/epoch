using Microsoft.Xna.Framework;

namespace epoch.Components;

public abstract class Component { }

public class GlobalSettings : Component
{
    public float GlobalScale { get; set; } = 8.0f;
}

public class GraphicalTile : Component
{
    public required string Name { get; set; }
    public required Color Color { get; set; }
    public float Scale { get; set; } = 1.0f;
}

public class Position : Component
{
    public Vector2 Vec2 { get; set; } = Vector2.Zero;
}
