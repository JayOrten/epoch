using Microsoft.Xna.Framework;

namespace epoch.Components;

public abstract class Component { }

public class GraphicalTile : Component
{
    public required string Name { get; set; }
    public float Scale { get; set; } = 1.0f;

    // You can use this as an override for the color in the tile definition,
    // either by putting the color in the entity definition, or within the code
    // when you create the entity (merging)
    public Color? Color { get; set; }
}

public class Position : Component
{
    public Vector3 WorldCoordinate { get; set; } = Vector3.Zero;
}
