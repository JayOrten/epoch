namespace Engine.Graphics;

/// <summary>
/// Represents a single tile definition.
/// This class focuses on holding tile indices for different textures/modes
/// </summary>
public record Tile(
    int Id,
    string Name,
    char AsciiTileIndex,
    string AsciiColor,
    int GraphicalTileIndex
);

/// <summary>
/// Holds the rendering information for a tile in the tilemap.
/// This is mostly a utility struct for passing the render information elegantly from the tile manager.
public struct TileRenderInfo
{
    public TextureRegion TextureRegion;
    public string Color;

    public TileRenderInfo(TextureRegion textureRegion, string color)
    {
        TextureRegion = textureRegion;
        Color = color;
    }
}
