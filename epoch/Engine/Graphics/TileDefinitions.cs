using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Content;

namespace Engine.Graphics;

public class TileDefinitions
{
    public List<Tile> Tiles { get; set; } = new List<Tile>();

    private Dictionary<string, Tile> _tilesByName;

    public TileDefinitions(List<Tile> tiles)
    {
        Tiles = tiles ?? new List<Tile>();
        _tilesByName = Tiles.ToDictionary(t => t.Name);
    }

    public static TileDefinitions FromFile(string path)
    {
        // Load and parse the tile definitions from the specified file
        List<Tile> tiles = new List<Tile>();

        // Parse tile objects from json file specified by path
        using var stream = TitleContainer.OpenStream(path);
        using var reader = new StreamReader(stream);

        string json = reader.ReadToEnd();
        tiles =
            System.Text.Json.JsonSerializer.Deserialize<List<Tile>>(
                json,
                new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true }
            )
            ?? new List<Tile>();

        return new TileDefinitions(tiles);
    }

    public Tile GetTile(int index)
    {
        if (index < 0 || index >= Tiles.Count)
            return null;

        return Tiles[index];
    }

    public Tile GetTileByName(string name)
    {
        return _tilesByName.TryGetValue(name, out var tile) ? tile : null;
    }
}
