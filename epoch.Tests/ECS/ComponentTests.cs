using epoch.ECS;

namespace epoch.Tests.ECS;

public class ComponentTests
{
    [Fact]
    public void Set_ActivatesTile()
    {
        var list = new GraphicalTileList();
        list.Set(0, new GraphicalTile { TileId = 1 });

        Assert.Equal(1, list.NumTiles);
        Assert.True((list.ActiveTileMask & 1) != 0);
    }

    [Fact]
    public void Set_WritesToHigherIndex()
    {
        var list = new GraphicalTileList();
        list.Set(5, new GraphicalTile { TileId = 99 });

        Assert.Equal(1, list.NumTiles);
        Assert.Equal(99, list.Tiles[5].TileId);
    }

    [Fact]
    public void Set_OutOfBounds_NoOp()
    {
        var list = new GraphicalTileList();
        list.Set(GraphicalTileList.MaxTiles, new GraphicalTile { TileId = 1 });

        Assert.Equal(0, list.NumTiles);
        Assert.Equal(0, list.ActiveTileMask);
    }

    [Fact]
    public void Remove_DeactivatesTile()
    {
        var list = new GraphicalTileList();
        list.Set(0, new GraphicalTile { TileId = 1 });
        list.Remove(0);

        Assert.Equal(0, list.NumTiles);
        Assert.True((list.ActiveTileMask & 1) == 0);
    }

    [Fact]
    public void Set_AlreadyActive_NoDoubleCount()
    {
        var list = new GraphicalTileList();
        list.Set(0, new GraphicalTile { TileId = 1 });
        list.Set(0, new GraphicalTile { TileId = 2 });

        Assert.Equal(1, list.NumTiles);
        Assert.Equal(2, list.Tiles[0].TileId);
    }

    [Fact]
    public void Set_MultipleTiles_TracksAll()
    {
        var list = new GraphicalTileList();
        list.Set(0, new GraphicalTile { TileId = 1 });
        list.Set(2, new GraphicalTile { TileId = 3 });

        Assert.Equal(2, list.NumTiles);
        Assert.Equal(0b101, list.ActiveTileMask);
    }

    [Fact]
    public void Remove_NegativeIndex_NoOp()
    {
        var list = new GraphicalTileList();
        list.Set(0, new GraphicalTile { TileId = 1 });
        list.Remove(-1);

        Assert.Equal(1, list.NumTiles);
    }

    [Fact]
    public void Remove_OutOfBounds_NoOp()
    {
        var list = new GraphicalTileList();
        list.Set(0, new GraphicalTile { TileId = 1 });
        list.Remove(GraphicalTileList.MaxTiles);

        Assert.Equal(1, list.NumTiles);
    }
}
