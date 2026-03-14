using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using epoch.Utilities;
using Microsoft.Xna.Framework;

namespace epoch.Graphics.Tiles;

/// <summary>
/// Registry of <see cref="Tile"/> definitions loaded from XML. Provides lookup by index
/// or name and owns the <see cref="Tileset"/> used for texture regions.
/// </summary>
public class TileManager
{
    public Tile[] Tiles { get; set; } = Array.Empty<Tile>();

    private Dictionary<string, Tile> _tilesByName;

    public Tileset Tileset;

    public TileManager(Tileset tileset, Tile[] tiles)
    {
        Tileset = tileset;
        Tiles = tiles ?? Array.Empty<Tile>();
        _tilesByName = Tiles.ToDictionary(t => t.Name);
    }

    /// <summary>Loads tile definitions from an XML file and parses color attributes into <see cref="Color"/> values.</summary>
    public static TileManager FromFile(Tileset tileset, string path)
    {
        using var stream = TitleContainer.OpenStream(path);
        var doc = XDocument.Load(stream);

        static Color ParseOrDefault(string s) =>
            string.IsNullOrEmpty(s) ? Color.White : Utils.ParseColor(s);

        var tiles = doc.Root.Elements("tile").Select(el => new Tile(
            Id: int.Parse(el.Attribute("id")?.Value ?? "0"),
            Name: el.Attribute("name")?.Value ?? "",
            TileIndex: int.Parse(el.Attribute("index")?.Value ?? "0"),
            Background1Color: ParseOrDefault(el.Attribute("bg1")?.Value),
            Background2Color: ParseOrDefault(el.Attribute("bg2")?.Value),
            BaseColor: ParseOrDefault(el.Attribute("base")?.Value),
            AccentColor: ParseOrDefault(el.Attribute("accent")?.Value),
            BorderColor: ParseOrDefault(el.Attribute("border")?.Value)
        )).ToArray();

        return new TileManager(tileset, tiles);
    }

    /// <summary>Returns the tile at <paramref name="index"/>, or <c>null</c> if out of range.</summary>
    public Tile GetTile(int index)
    {
        if (index < 0 || index >= Tiles.Length)
            return null;

        return Tiles[index];
    }

    /// <summary>Returns the tile with the given <paramref name="name"/>, or <c>null</c> if not found.</summary>
    public Tile GetTileByName(string name)
    {
        return _tilesByName.TryGetValue(name, out var tile) ? tile : null;
    }
}
