namespace epoch.Engine.Graphics;

public enum TileMode
{
    Ascii,
    Graphical,
}

/// <summary>
/// Holds the tile definitions, and the tileset for either ascii or graphical mode.
/// Connects the tile data with the tile graphics.
/// That way, the tilemap can use a unified tile idx regardless of mode.
public class TileManager
{
    public Tileset TileSet { get; set; }
    public TileDefinitions TileDefinitions { get; set; }
    public TileMode Mode { get; set; }

    public TileManager(Tileset tileSet, TileDefinitions tileDefinitions, TileMode mode)
    {
        TileSet = tileSet;
        TileDefinitions = tileDefinitions;
        Mode = mode;
    }

    public int TileWidth => TileSet.TileWidth;

    public int TileHeight => TileSet.TileHeight;

    public TileRenderInfo? GetTile(string name)
    {
        Tile tile = TileDefinitions.GetTileByName(name);
        return GetTile(tile);
    }

    public TileRenderInfo? GetTile(int index)
    {
        Tile tile = TileDefinitions.GetTile(index);
        return GetTile(tile);
    }

    public TileRenderInfo? GetTile(Tile tile)
    {
        if (tile == null)
            return null;

        return Mode switch
        {
            TileMode.Ascii => new TileRenderInfo(TileSet.GetTile(tile.AsciiTileIndex), tile.Color),
            TileMode.Graphical => new TileRenderInfo(
                TileSet.GetTile(tile.GraphicalTileIndex),
                tile.Color
            ),
            _ => null,
        };
    }
}
