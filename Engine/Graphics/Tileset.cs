using System;
using System.IO;
using System.Collections.Generic;
using Microsoft.Xna.Framework.Graphics;
using System.Text.Json;

namespace Engine.Graphics;

public class Tileset
{
    private readonly TextureRegion[] _tiles;

    private Dictionary<string, int> _namedTiles;
    
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
    public Tileset(TextureRegion textureRegion, int tileWidth, int tileHeight, int padding=0, string path=null)
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
            _tiles[i] = new TextureRegion(textureRegion.Texture, textureRegion.SourceRectangle.X + x, textureRegion.SourceRectangle.Y + y, tileWidth, tileHeight);
        }

        // Load named tiles if a path is provided
        if (path != null)
            LoadNamesFromFile(path);
    }

    public void LoadNamesFromFile(string path)
    {
        _namedTiles = new Dictionary<string, int>();
        var json = File.ReadAllText(path);
        var data = JsonSerializer.Deserialize<Dictionary<string, int>>(json);
        foreach (var kvp in data)
        {
            _namedTiles[kvp.Key] = kvp.Value;
        }
        
    }

    /// <summary>
    /// Gets the texture region for the tile from this tileset at the given index.
    /// </summary>
    /// <param name="index">The index of the texture region in this tile set.</param>
    /// <returns>The texture region for the tile form this tileset at the given index.</returns>
    public TextureRegion GetTile(int index) => _tiles[index];

    /// <summary>
    /// Gets the texture region for the tile from this tileset at the given location.
    /// </summary>
    /// <param name="column">The column in this tileset of the texture region.</param>
    /// <param name="row">The row in this tileset of the texture region.</param>
    /// <returns>The texture region for the tile from this tileset at given location.</returns>
    public TextureRegion GetTile(int column, int row)
    {
        int index = row * Columns + column;
        return GetTile(index);
    }

    public TextureRegion GetTile(string name)
    {
        if (_namedTiles != null && _namedTiles.TryGetValue(name, out int index))
        {
            return GetTile(index);
        }
        throw new KeyNotFoundException($"Tile with name '{name}' not found in tileset.");
    }

}


