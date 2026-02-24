using Arch.Core;
using Arch.Core.Extensions;
using epoch.ECS;
using Microsoft.Xna.Framework;

namespace epoch.Tests.ECS;

public class MapRegistryTests
{
    private World _world;
    private MapRegistry _registry;

    public MapRegistryTests()
    {
        _world = World.Create();
        _registry = new MapRegistry(_world, 4, 16);
    }

    [Fact]
    public void Register_GetEntityAt_Roundtrip()
    {
        var coord = new Vector3(3, 4, 2);
        var entity = _world.Create(new Position { WorldCoordinate = coord, Passable = true });
        _registry.Register(coord, entity);

        var result = _registry.GetEntityAt(coord);
        Assert.Equal(entity, result);
    }

    [Fact]
    public void GetEntityAt_Empty_ReturnsNull()
    {
        var result = _registry.GetEntityAt(new Vector3(5, 5, 5));
        Assert.Equal(Entity.Null, result);
    }

    [Fact]
    public void IsPassableAt_PassableEntity()
    {
        var coord = new Vector3(1, 1, 1);
        var entity = _world.Create(new Position { WorldCoordinate = coord, Passable = true });
        _registry.Register(coord, entity);

        Assert.True(_registry.IsPassableAt(coord));
    }

    [Fact]
    public void IsPassableAt_BlockedEntity()
    {
        var coord = new Vector3(2, 2, 1);
        var entity = _world.Create(new Position { WorldCoordinate = coord, Passable = false });
        _registry.Register(coord, entity);

        Assert.False(_registry.IsPassableAt(coord));
    }

    [Fact]
    public void IsPassableAt_MissingColumn_ReturnsTrue()
    {
        // No column loaded → passable (empty space)
        Assert.True(_registry.IsPassableAt(new Vector3(99, 99, 5)));
    }

    [Fact]
    public void IsPassableAt_EmptyColumn_ReturnsTrue()
    {
        // Column exists but no entity → passable (open sky)
        _registry.EnsureColumn(0, 0);
        Assert.True(_registry.IsPassableAt(new Vector3(0, 0, 0)));
    }

    [Fact]
    public void Unregister_RemovesEntity()
    {
        var coord = new Vector3(1, 2, 3);
        var entity = _world.Create(new Position { WorldCoordinate = coord, Passable = true });
        _registry.Register(coord, entity);
        _registry.Unregister(coord);

        Assert.Equal(Entity.Null, _registry.GetEntityAt(coord));
    }

    [Fact]
    public void NegativeXY_Coordinates_Work()
    {
        var coord = new Vector3(-3, -5, 1);
        var entity = _world.Create(new Position { WorldCoordinate = coord, Passable = true });
        _registry.Register(coord, entity);

        Assert.Equal(entity, _registry.GetEntityAt(coord));
        Assert.True(_registry.IsPassableAt(coord));
    }

    [Fact]
    public void CrossColumn_Lookup()
    {
        // chunkSize=4, so (3,0,0) and (4,0,0) are in different columns
        var coordA = new Vector3(3, 0, 0);
        var coordB = new Vector3(4, 0, 0);

        var entityA = _world.Create(new Position { WorldCoordinate = coordA, Passable = true });
        var entityB = _world.Create(new Position { WorldCoordinate = coordB, Passable = false });

        _registry.Register(coordA, entityA);
        _registry.Register(coordB, entityB);

        Assert.Equal(entityA, _registry.GetEntityAt(coordA));
        Assert.Equal(entityB, _registry.GetEntityAt(coordB));
        Assert.True(_registry.IsPassableAt(coordA));
        Assert.False(_registry.IsPassableAt(coordB));
    }

    [Fact]
    public void RemoveColumn_ClearsAndDestroysEntities()
    {
        var entities = new List<Entity>();
        for (int x = 0; x < 4; x++)
        {
            var coord = new Vector3(x, 0, 0);
            var entity = _world.Create(new Position { WorldCoordinate = coord, Passable = true });
            _registry.Register(coord, entity);
            entities.Add(entity);
        }

        _registry.RemoveColumn(0, 0);

        // All slots should be empty
        for (int x = 0; x < 4; x++)
            Assert.Equal(Entity.Null, _registry.GetEntityAt(new Vector3(x, 0, 0)));

        // All entities should be destroyed
        foreach (var e in entities)
            Assert.False(_world.IsAlive(e));
    }

    [Fact]
    public void RemoveColumn_NonexistentColumn_DoesNotThrow()
    {
        _registry.RemoveColumn(99, 99);
    }

    [Fact]
    public void RemoveColumn_DestroysAcrossZLevels()
    {
        var entityA = _world.Create(new Position { WorldCoordinate = new Vector3(0, 0, 0), Passable = true });
        _registry.Register(new Vector3(0, 0, 0), entityA);

        var entityB = _world.Create(new Position { WorldCoordinate = new Vector3(0, 0, 4), Passable = true });
        _registry.Register(new Vector3(0, 0, 4), entityB);

        _registry.RemoveColumn(0, 0);

        Assert.Equal(Entity.Null, _registry.GetEntityAt(new Vector3(0, 0, 0)));
        Assert.Equal(Entity.Null, _registry.GetEntityAt(new Vector3(0, 0, 4)));
        Assert.False(_world.IsAlive(entityA));
        Assert.False(_world.IsAlive(entityB));
    }
}
