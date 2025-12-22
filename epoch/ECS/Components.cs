using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;

namespace epoch.ECS;

public class ComponentDefinition
{
    public string TypeName { get; set; }

    public Dictionary<string, string> Properties { get; } = new();

    public string this[string property]
    {
        get => Properties.TryGetValue(property, out var value) ? value : null;
        set => Properties[property] = value;
    }

    public bool TryGet(string property, out string value)
    {
        return Properties.TryGetValue(property, out value);
    }

    public ComponentDefinition(string typeName)
    {
        TypeName = typeName;
    }

    public ComponentDefinition(string typeName, Dictionary<string, string> properties)
        : this(typeName)
    {
        foreach (var kvp in properties)
            Properties[kvp.Key] = kvp.Value;
    }

    public ComponentDefinition Add(string property, string value)
    {
        Properties[property] = value;
        return this;
    }

    public ComponentDefinition Merge(ComponentDefinition other)
    {
        if (other == null)
            return this;

        foreach (var kvp in other.Properties)
            Properties[kvp.Key] = kvp.Value;

        return this;
    }
}

[AttributeUsage(AttributeTargets.Struct)]
public class ComponentAttribute : Attribute { }

// Tag Components
[Component]
public struct PlayerTag { }

// Regular Components
[Component]
public struct GraphicalTile
{
    public int TileId { get; set; }
    public float Scale { get; set; } = 1.0f;

    // You can use this as an override for the color in the tile definition,
    // either by putting the color in the entity definition, or within the code
    // when you create the entity (merging)
    public Color? SpriteColor { get; set; }
    public Color BackgroundColor { get; set; }
    public Color BorderColor { get; set; }

    public int BorderMask { get; set; } = 0;

    public float BorderWidth { get; set; } = 0.13f;

    // Flag to check border mask updates (but could be used for other things?)
    public bool IsDirty { get; set; } = true;

    public GraphicalTile() { }
}

[Component]
public struct Position
{
    public Vector2 WorldCoordinate { get; set; }
    public float zLevel { get; set; }

    // Represents priority on the z-level, usually just 0. Always less than 1, or this will break
    public float top { get; set; } = 0;

    public Position() { }
}

[Component]
public struct Direction
{
    public Vector2 FaceDirection { get; set; }
}
