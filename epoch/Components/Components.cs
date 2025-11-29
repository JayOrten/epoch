using Microsoft.Xna.Framework;

namespace epoch.Components;

public abstract class Component { }

public class GraphicalTile : Component
{
    public required string Name { get; set; }
    public required Color Color { get; set; }
    public float Scale { get; set; } = 1.0f;
}

public class Position : Component
{
    public Vector3 WorldCoordinate { get; set; } = Vector3.Zero;
}
