using Microsoft.Xna.Framework;

namespace epoch.Graphics.Tiles;

/// <summary>
/// Represents a single tile definition with color data for rendering.
/// </summary>
public record Tile(
    int Id,
    string Name,
    int TileIndex,
    Color Background1Color,
    Color Background2Color,
    Color BaseColor,
    Color AccentColor,
    Color BorderColor
);
