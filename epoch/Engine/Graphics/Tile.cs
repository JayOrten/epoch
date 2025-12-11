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
/// I seem to need this because I need a way to pass the actual TextureRegion (and I don't want to hold it in the main tile object?)
/// One reason I can't just keep the textureregion in Tile is because I want to pull tiles or ascii dynamically
public readonly record struct TileRenderInfo(TextureRegion TextureRegion, Color Color)
{
    public int TileWidth => TextureRegion.Width;
    public int TileHeight => TextureRegion.Height;
}
