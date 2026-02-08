using System;
using System.Collections.Generic;
using Arch.Core;
using Microsoft.Xna.Framework;

namespace epoch.ECS;

/// <summary>Determines which border style is applied to a tile's edges.</summary>
public enum BorderType
{
    None,
    Bottom,
    Top,
}

/// <summary>
/// Marks a struct as an ECS component for the source generator.
/// Set <see cref="UseCustomFactory"/> to skip auto-generation and use hand-written factory logic.
/// </summary>
[AttributeUsage(AttributeTargets.Struct)]
public class ComponentAttribute : Attribute
{
    public bool UseCustomFactory { get; set; } = false;
}

// ── Tag Components ──────────────────────────────────────────────────

/// <summary>Marks the entity as the player.</summary>
[Component]
public struct PlayerTag { }

/// <summary>Marks the entity as empty space (air). Used as the default fill in <see cref="MapRegistry"/>.</summary>
[Component]
public struct AirTag { }

/// <summary>Flags an entity for re-evaluation (e.g. adjacency recalc after a neighbor changed).</summary>
[Component]
public struct DirtyTag { }

// ── Tile Components ─────────────────────────────────────────────────

/// <summary>
/// Variable-length list of <see cref="GraphicalTile"/> layers for an entity.
/// Uses a bitmask (<see cref="ActiveTileMask"/>) to track which slots are active,
/// enabling O(1) enable/disable without shifting array elements.
/// </summary>
[Component(UseCustomFactory = true)]
public struct GraphicalTileList
{
    public GraphicalTile[] Tiles;

    public int NumTiles { get; private set; } = 0;

    // Bit 0 = Tile0, Bit 1 = Tile1, etc.
    // 0000 = no tiles
    // 0101 = Tile0 and Tile2
    public int ActiveTileMask { get; set; } = 0;

    /// <summary>Activates a tile at <paramref name="index"/>, growing the array if needed.</summary>
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

    /// <summary>Deactivates the tile at <paramref name="index"/> without removing the array slot.</summary>
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

/// <summary>
/// Visual description of a single tile layer: sprite ID, color overrides, border settings,
/// and interpolation state for smooth z-level transitions.
/// </summary>
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
    public bool DrawInitialized { get; set; } = false;
    public Vector2 CurrentDrawPosition { get; set; }
    public Vector2 DrawPositionVelocity;

    public float CurrentDrawScale { get; set; }

    public GraphicalTile() { }
}

/// <summary>
/// Grid position and spatial metadata for an entity.
/// <see cref="SpaceMask"/> encodes a 26-bit neighborhood (3x3x3 cube minus center)
/// used for adjacency, border, and autotiling calculations.
/// </summary>
[Component]
public struct Position
{
    public Vector3 WorldCoordinate { get; set; }

    /// <summary>Z-layer sub-priority for draw sorting. Must be in [0, 1).</summary>
    public float Top { get; set; } = 0;

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

// ── Organism Components ─────────────────────────────────────────────

/// <summary>The direction an entity is facing (for sprites/AI).</summary>
[Component]
public struct Direction
{
    public Vector2 FaceDirection { get; set; }
}

/// <summary>Raw movement input vector written by <see cref="InputSystem"/>.</summary>
[Component]
public struct MovementInput
{
    public Vector2 Direction { get; set; }
}

/// <summary>
/// Movement timing. The entity moves one tile every <see cref="MoveDelay"/> seconds;
/// <see cref="CurrentTimer"/> counts down between moves.
/// </summary>
[Component]
public struct Movement
{
    public float MoveDelay { get; set; } = 0.40f;
    public float CurrentTimer { get; set; } = 0f;

    public Movement() { }
}

// ── Composite Body Components ───────────────────────────────────────

/// <summary>
/// Controls a multi-entity "body" (e.g. player with separate head/torso/legs entities).
/// Maps part labels to child entities and tracks their offsets.
/// </summary>
[Component(UseCustomFactory = true)]
public struct CompositeControllerComponent
{
    public Dictionary<string, Entity> Parts { get; set; }
    public List<Vector3> ChildOffsets { get; set; }
}

/// <summary>Back-reference from a child part to its parent controller entity.</summary>
[Component(UseCustomFactory = true)]
public struct CompositePartComponent
{
    public Entity MasterId { get; set; }
    public string PartLabel { get; set; }
}

// ── Camera Components ───────────────────────────────────────────────

/// <summary>Per-frame camera input deltas (look direction change, zoom change).</summary>
[Component]
public struct CameraInput
{
    public Vector2 LookChange { get; set; }
    public float ZoomChange { get; set; }
}

/// <summary>Current camera state: world position, look offset, and zoom level.</summary>
[Component]
public struct CameraState
{
    public Vector2 Position { get; set; }
    public Vector2 LookDirection { get; set; }
    public float ZoomAmount { get; set; }
}

/// <summary>Snapshot of previous frame's camera state for interpolation across refresh rates.</summary>
[Component]
public struct CameraPreviousState
{
    public Vector2 Position { get; set; }
    public float Zoom { get; set; }
}
