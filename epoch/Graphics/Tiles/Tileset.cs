using System;
using System.IO;
using System.Text.Json;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Content;
using Microsoft.Xna.Framework.Graphics;

namespace epoch.Graphics.Tiles;

/// <summary>
/// Represents a tileset, a collection of tiles extracted from a single texture region.
/// This class is focused on loading from file and splitting up the texture into a grid
/// The tiles are then accessed by index or by column/row.
/// </summary>
public class Tileset
{
    private readonly TextureRegion[] _tiles;

    /// <summary>
    /// Gets the width, in pixels, of each tile in this tileset.
    /// </summary>
    public int TileWidth { get; }

    /// <summary>
    /// Gets the height, in pixels, of each tile in this tileset.
    /// </summary>
    public int TileHeight { get; }

    /// <summary>
    /// Gets the total number of columns in this tileset.
    /// </summary>
    public int Columns { get; }

    /// <summary>
    /// Gets the total number of rows in this tileset.
    /// </summary>
    public int Rows { get; }

    /// <summary>
    /// Gets the total number of tiles in this tileset.
    /// </summary>
    public int Count { get; }

    /// <summary>
    /// Creates a new tileset based on the given texture region with the specified
    /// tile width and height.
    /// </summary>
    /// <param name="textureRegion">The texture region that contains the tiles for the tileset.</param>
    /// <param name="tileWidth">The width of each tile in the tileset.</param>
    /// <param name="tileHeight">The height of each tile in the tileset.</param>
    /// <param name="padding">The amount of padding, in pixels, between tiles in the tileset. This is assuming the border also has this padding</param>
    public Tileset(TextureRegion textureRegion, int tileWidth, int tileHeight, int padding = 0)
    {
        TileWidth = tileWidth;
        TileHeight = tileHeight;
        Columns = (textureRegion.Width - padding) / (tileWidth + padding); // Subtract one padding for end, and add padding for each column
        Rows = (textureRegion.Height - padding) / (tileHeight + padding);
        Count = Columns * Rows;

        // Create the texture regions that make up each individual tile
        _tiles = new TextureRegion[Count];

        for (int i = 0; i < Count; i++)
        {
            int x = (i % Columns) * (tileWidth + padding) + padding;
            int y = (i / Columns) * (tileHeight + padding) + padding;
            _tiles[i] = new TextureRegion(
                textureRegion.Texture,
                textureRegion.SourceRectangle.X + x,
                textureRegion.SourceRectangle.Y + y,
                tileWidth,
                tileHeight
            );
        }
    }

    /// <summary>
    /// Loads a tileset from a JSON definition file that specifies texture path,
    /// region bounds, tile dimensions, and padding.
    /// </summary>
    public static Tileset FromFile(ContentManager contentManager, string tilesetPath)
    {
        using var stream = TitleContainer.OpenStream(tilesetPath);
        using var reader = new StreamReader(stream);

        // Load the tileset definition from a JSON file
        string json = reader.ReadToEnd();
        var tiledef = JsonSerializer.Deserialize<TilesetDefinition>(json);

        Texture2D texture = contentManager.Load<Texture2D>(tiledef.file);

        // Load the texture region for the tileset
        TextureRegion textureRegion = new TextureRegion(
            texture,
            tiledef.region.x,
            tiledef.region.y,
            tiledef.region.width,
            tiledef.region.height
        );

        // Create and return the tileset
        return new Tileset(textureRegion, tiledef.tile_width, tiledef.tile_height, tiledef.padding);
    }

    // Custom mapping for index and rotation in the tileset.
    (int, float)[] rotationMap =
    [
        (0, 0.0f),
        (1, 180.0f),
        (1, 270.0f),
        (2, 180.0f),
        (1, 0.0f),
        (3, 0.0f),
        (2, 270.0f),
        (4, 180.0f),
        (1, 90.0f),
        (2, 90.0f),
        (3, 90.0f),
        (4, 90.0f),
        (2, 0.0f),
        (4, 0.0f),
        (4, 270.0f),
        (5, 0.0f),
    ];

    /// <summary>
    /// Gets the texture region for the tile from this tileset at the given index.
    /// </summary>
    /// <param name="index">The index of the texture region in this tile set.</param>
    /// <returns>The texture region for the tile form this tileset at the given index.</returns>
    public TextureRegion GetTile(int index) => _tiles[index];

    /// <summary>
    /// Gets the texture region for the tile from this tileset at the given index.
    /// </summary>
    /// <param name="index">The index of the texture region in this tile set.</param>
    /// <param name="autoTileMask">The mask for autotiling.</param>
    /// <returns>The texture region for the tile form this tileset at the given index.</returns>
    public (TextureRegion textureRegion, float rotation) GetTile(int index, int autoTileMask)
    {
        // Add the bordermask to the tile id if auto tiling is on
        (int increment, float rotation) = rotationMap[autoTileMask];
        int tileId = index + increment;
        return (_tiles[tileId], rotation);
    }
}

/// <summary>JSON DTO: pixel bounds within the tileset texture.</summary>
public class Region
{
    public int x { get; set; }
    public int y { get; set; }
    public int width { get; set; }
    public int height { get; set; }
}

/// <summary>JSON DTO: tileset definition file schema (texture path, region, tile size, padding).</summary>
public class TilesetDefinition
{
    public string file { get; set; }
    public Region region { get; set; }
    public int tile_width { get; set; }
    public int tile_height { get; set; }
    public int padding { get; set; }
}
