using System;
using System.Collections.Generic;
using Arch.Core;
using Microsoft.Xna.Framework;

namespace epoch.ECS;

public enum BorderType
{
    None,
    Bottom,
    Top,
}

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

// Signifies the entity is "dirty" and the tile may need to be updated
// Usually from surrounding tile changes
[Component]
public struct DirtyTag { }

// Regular Components
// --- GENERAL TILE ---
[Component(UseCustomFactory = true)]
public struct GraphicalTileList
{
    public GraphicalTile[] Tiles;

    public int NumTiles { get; private set; } = 0;

    // Bit 0 = Tile0, Bit 1 = Tile1, etc.
    // 0000 = no tiles
    // 0101 = Tile0 and Tile2
    public int ActiveTileMask { get; set; } = 0;

    // Turn a tile ON
    public void Set(int index, GraphicalTile tile)
    {
        if (index < 0)
            return;

        // Grow array if needed
        if (Tiles == null || index >= Tiles.Length)
        {
            var newTiles = new GraphicalTile[index + 1];
            if (Tiles != null)
                Array.Copy(Tiles, newTiles, Tiles.Length);
            Tiles = newTiles;
        }

        bool wasInactive = (ActiveTileMask & (1 << index)) == 0;
        Tiles[index] = tile;

        // Bitwise OR to switch the bit ON
        ActiveTileMask |= (1 << index);

        if (wasInactive)
            NumTiles++;
    }

    // Turn a tile OFF
    public void Remove(int index)
    {
        if (index < 0 || Tiles == null || index >= Tiles.Length)
            return;

        bool wasActive = (ActiveTileMask & (1 << index)) != 0;

        // Bitwise AND with NOT to switch the bit OFF
        ActiveTileMask &= ~(1 << index);

        if (wasActive)
            NumTiles--;
    }

    public GraphicalTileList()
    {
        Tiles = [];
    }
}

[Component]
public struct GraphicalTile
{
    public int TileId { get; set; }
    public float Scale { get; set; } = 1.0f;

    // Offset from the entity position
    public float Offset { get; set; } = 0.0f;

    // You can use these as an override for the color in the tile definition,
    // either by putting the color in the entity definition, or within the code
    // when you create the entity (merging)
    public Color? Background1Color { get; set; }
    public Color? Background2Color { get; set; }
    public Color? BaseColor { get; set; }
    public Color? AccentColor { get; set; }
    public Color? BorderColor { get; set; }

    // Draw regardless of space nearby
    public bool ForceDraw { get; set; } = false;

    // Bits in the mask that are 1 indicate a border in that direction
    // directions: north, east, south, west
    // This is calculated based on the spacemask and spacemask of adjacent tiles
    public BorderType BorderType { get; set; } = BorderType.None;
    public int BorderMask { get; set; } = 0;
    public float BorderWidth { get; set; } = 0.04f;

    // Whether or not this tiles TileId should be incremented based on the BorderMask
    public bool AutoTile { get; set; } = false;
    public int AutoTileMask { get; set; } = 0;

    // For interpolating between positions
    public bool InterpolateMovement { get; set; } = true;
    public Vector2 CurrentDrawPosition { get; set; }
    public Vector2 DrawPositionVelocity;

    public float CurrentDrawScale { get; set; }

    public GraphicalTile() { }
}

[Component]
public struct Position
{
    public Vector3 WorldCoordinate { get; set; }

    // Represents priority on the z-level, usually just 0. Always less than 1, or this will break
    public float top { get; set; } = 0;

    // Represents potential offset from parent entity, not necessary
    public Vector3 Offset { get; set; }

    // Whether this entity is passable
    public bool Passable { get; set; }

    // Whether this entity is a "block". Might change this concept in the future
    // Right now, just useful for autotiling, edge borders, etc.
    public bool IsBlock { get; set; }

    // Bits in the mask that are 1 indicate air/passable in that direction
    // 26 directions for full 3D neighborhood (3x3x3 cube minus center)
    // Coordinate system: X = East(+)/West(-), Y = South(+)/North(-), Z = Above(+)/Below(-)
    //
    // Faces (bits 0-5):
    //   0: North        (0, -1, 0)
    //   1: East         (1, 0, 0)
    //   2: South        (0, 1, 0)
    //   3: West         (-1, 0, 0)
    //   4: Above        (0, 0, 1)
    //   5: Below        (0, 0, -1)
    //
    // Edges - horizontal (bits 6-9):
    //   6: North-East   (1, -1, 0)
    //   7: South-East   (1, 1, 0)
    //   8: South-West   (-1, 1, 0)
    //   9: North-West   (-1, -1, 0)
    //
    // Edges - vertical north/south (bits 10-13):
    //   10: North-Above  (0, -1, 1)
    //   11: North-Below  (0, -1, -1)
    //   12: South-Above  (0, 1, 1)
    //   13: South-Below  (0, 1, -1)
    //
    // Edges - vertical east/west (bits 14-17):
    //   14: East-Above   (1, 0, 1)
    //   15: East-Below   (1, 0, -1)
    //   16: West-Above   (-1, 0, 1)
    //   17: West-Below   (-1, 0, -1)
    //
    // Corners - above (bits 18-21):
    //   18: North-East-Above  (1, -1, 1)
    //   19: South-East-Above  (1, 1, 1)
    //   20: South-West-Above  (-1, 1, 1)
    //   21: North-West-Above  (-1, -1, 1)
    //
    // Corners - below (bits 22-25):
    //   22: North-East-Below  (1, -1, -1)
    //   23: South-East-Below  (1, 1, -1)
    //   24: South-West-Below  (-1, 1, -1)
    //   25: North-West-Below  (-1, -1, -1)
    //
    public int SpaceMask { get; set; } = 0;

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
    public float MoveDelay { get; set; } = 0.40f;
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
