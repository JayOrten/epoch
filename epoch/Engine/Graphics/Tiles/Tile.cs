using System.Text.Json.Serialization;
using Microsoft.Xna.Framework;

namespace epoch.Engine.Graphics.Tiles;

/// <summary>
/// Represents a single tile definition.
/// This class focuses on holding tile indices for different textures/modes
/// </summary>
public record Tile(
    int Id,
    string Name,
    int TileIndex,
    string Background1ColorString,
    string Background2ColorString,
    string BaseColorString,
    string AccentColorString,
    string BorderColorString,
    [property: JsonIgnore] TextureRegion TextureRegion,
    [property: JsonIgnore] Color Background1Color,
    [property: JsonIgnore] Color Background2Color,
    [property: JsonIgnore] Color BaseColor,
    [property: JsonIgnore] Color AccentColor,
    [property: JsonIgnore] Color BorderColor
);
