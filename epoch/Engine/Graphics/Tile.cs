using System.Text.Json.Serialization;
using Microsoft.Xna.Framework;

namespace epoch.Engine.Graphics;

/// <summary>
/// Represents a single tile definition.
/// This class focuses on holding tile indices for different textures/modes
/// </summary>
public record Tile(
    int Id,
    string Name,
    int AsciiTileIndex,
    string ColorString,
    [property: JsonIgnore] Color Color,
    int GraphicalTileIndex
);

/// <summary>
/// Holds the rendering information for a tile in the tilemap.
/// This is mostly a utility struct for passing the render information elegantly from the tile manager.
public readonly record struct TileRenderInfo(TextureRegion TextureRegion, Color Color)
{
    public int TileWidth => TextureRegion.Width;
    public int TileHeight => TextureRegion.Height;
}
