using System.Collections.Generic;
using System.IO;
using System.Linq;
using epoch.Utilities;
using Microsoft.Xna.Framework;

namespace epoch.Engine.Graphics.Tiles;

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

        // Convert string color values to Color objects
        for (int i = 0; i < tiles.Count; i++)
        {
            var tile = tiles[i];

            Color background1Color = Color.White;
            if (!string.IsNullOrEmpty(tile.Background1ColorString))
            {
                background1Color = Utils.ParseColor(tile.Background1ColorString);
            }

            Color background2Color = Color.White;
            if (!string.IsNullOrEmpty(tile.Background2ColorString))
            {
                background2Color = Utils.ParseColor(tile.Background2ColorString);
            }

            Color baseColor = Color.White;
            if (!string.IsNullOrEmpty(tile.BaseColorString))
            {
                baseColor = Utils.ParseColor(tile.BaseColorString);
            }

            Color accentColor = Color.White;
            if (!string.IsNullOrEmpty(tile.AccentColorString))
            {
                accentColor = Utils.ParseColor(tile.AccentColorString);
            }

            Color borderColor = Color.White;
            if (!string.IsNullOrEmpty(tile.BorderColorString))
            {
                borderColor = Utils.ParseColor(tile.BorderColorString);
            }

            tiles[i] = tile with
            {
                Background1Color = background1Color,
                Background2Color = background2Color,
                BaseColor = baseColor,
                AccentColor = accentColor,
                BorderColor = borderColor,
            };
        }
        return new TileManager(tileset, tiles);
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
