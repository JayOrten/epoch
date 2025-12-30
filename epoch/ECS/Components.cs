using System;
using System.Collections.Generic;
using Arch.Core;
using Microsoft.Xna.Framework;

namespace epoch.ECS;

[AttributeUsage(AttributeTargets.Struct)]
public class ComponentAttribute : Attribute
{
    public bool UseCustomFactory { get; set; } = false;
}

// Tag Components

// Signifies the entity is a player
[Component]
public struct PlayerTag { }

// Signifies the entity is an empty space/air unit
[Component]
public struct AirTag { }

// Regular Components
// --- GENERAL TILE ---
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

    // Bits in the mask that are 1 indicate air in that direction
    // directions: north, east, south, west, above, below
    public int SpaceMask { get; set; } = 0;

    // Bits in the mask that are 1 indicate a border in that direction
    // directions: north, east, south, west
    // This is calcualted based on the spacemask and spacemask of adjacent tiles
    public int BorderMask { get; set; } = 0;

    public float BorderWidth { get; set; } = 0.13f;

    // Flag to check border mask updates (but could be used for other things?)
    public bool IsDirty { get; set; } = true;

    // For interpolating between positions
    public Vector2 CurrentDrawPosition { get; set; }
    public Vector2 DrawPositionVelocity;

    public float CurrentDrawScale { get; set; }

    public GraphicalTile() { }
}

[Component]
public struct Position
{
    public Vector3 WorldCoordinate { get; set; }

    // public float zLevel { get; set; }

    // Represents priority on the z-level, usually just 0. Always less than 1, or this will break
    public float top { get; set; } = 0;

    // Represents potential offset from parent entity, not necessary
    public Vector3 Offset { get; set; }

    // Whether this block is passable
    public bool Passable { get; set; }

    public Position() { }
}

// --- ORGANISM ---
[Component]
public struct Direction
{
    public Vector2 FaceDirection { get; set; }
}

[Component]
public struct MovementInput
{
    public Vector2 Direction { get; set; }
}

[Component]
public struct Movement
{
    // Speed of movement
    public float MoveDelay { get; set; } = 0.25f;
    public float CurrentTimer { get; set; } = 0f;

    public Movement() { }
}

// Body Components
[Component(UseCustomFactory = true)]
public struct CompositeControllerComponent
{
    public Dictionary<string, Entity> Parts { get; set; }
    public List<Vector3> ChildOffsets { get; set; }
}

[Component(UseCustomFactory = true)]
public struct CompositePartComponent
{
    public Entity MasterId { get; set; }
    public string PartLabel { get; set; }
}

// --- CAMERA ---
[Component]
public struct CameraInput
{
    // public Vector2 Movement;
    public Vector2 LookChange { get; set; }
    public float ZoomChange { get; set; }
}

[Component]
public struct CameraState
{
    public Vector2 Position { get; set; }
    public Vector2 LookDirection { get; set; }
    public float ZoomAmount { get; set; }
}

[Component]
public struct CameraPreviousState
{
    public Vector2 Position { get; set; }
    public float Zoom { get; set; }
}
