using System.Collections.Generic;
using System.IO;
using System.Linq;
using epoch.Utilities;
using Microsoft.Xna.Framework;

namespace epoch.Graphics.Tiles;

/// <summary>
/// Registry of <see cref="Tile"/> definitions loaded from JSON. Provides lookup by index
/// or name and owns the <see cref="Tileset"/> used for texture regions.
/// Color strings in the JSON are parsed to <see cref="Color"/> at load time.
/// </summary>
public class TileManager
{
    public List<Tile> Tiles { get; set; } = new List<Tile>();

    private Dictionary<string, Tile> _tilesByName;

    public Tileset Tileset;

    public TileManager(Tileset tileset, List<Tile> tiles)
    {
        Tileset = tileset;
        Tiles = tiles ?? new List<Tile>();
        _tilesByName = Tiles.ToDictionary(t => t.Name);
    }

    /// <summary>Loads tile definitions from a JSON file and parses color strings into <see cref="Color"/> values.</summary>
    public static TileManager FromFile(Tileset tileset, string path)
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

        // Parse color strings into Color values
        static Color ParseOrDefault(string s) =>
            string.IsNullOrEmpty(s) ? Color.White : Utils.ParseColor(s);

        for (int i = 0; i < tiles.Count; i++)
        {
            var tile = tiles[i];
            tiles[i] = tile with
            {
                Background1Color = ParseOrDefault(tile.Background1ColorString),
                Background2Color = ParseOrDefault(tile.Background2ColorString),
                BaseColor = ParseOrDefault(tile.BaseColorString),
                AccentColor = ParseOrDefault(tile.AccentColorString),
                BorderColor = ParseOrDefault(tile.BorderColorString),
            };
        }
        return new TileManager(tileset, tiles);
    }

    /// <summary>Returns the tile at <paramref name="index"/>, or <c>null</c> if out of range.</summary>
    public Tile GetTile(int index)
    {
        if (index < 0 || index >= Tiles.Count)
            return null;

        return Tiles[index];
    }

    /// <summary>Returns the tile with the given <paramref name="name"/>, or <c>null</c> if not found.</summary>
    public Tile GetTileByName(string name)
    {
        return _tilesByName.TryGetValue(name, out var tile) ? tile : null;
    }
}
