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
        _registry = new MapRegistry(_world, 10, 10, 5);
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
    public void GetEntityAt_OutOfBounds_ReturnsNull()
    {
        var result = _registry.GetEntityAt(new Vector3(-1, 0, 0));
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
    public void Register_OutOfBounds_Silent()
    {
        var coord = new Vector3(-1, -1, -1);
        var entity = _world.Create(new Position { WorldCoordinate = coord, Passable = true });

        // Should not throw
        _registry.Register(coord, entity);
    }

    [Fact]
    public void IsInBounds_Origin()
    {
        Assert.True(_registry.IsInBounds(new Vector3(0, 0, 0)));
    }

    [Fact]
    public void IsInBounds_MaxEdge()
    {
        // 10x10x5 → valid range is 0..9, 0..9, 0..4
        Assert.True(_registry.IsInBounds(new Vector3(9, 9, 4)));
    }

    [Fact]
    public void IsInBounds_JustOutside()
    {
        Assert.False(_registry.IsInBounds(new Vector3(10, 0, 0)));
        Assert.False(_registry.IsInBounds(new Vector3(0, 10, 0)));
        Assert.False(_registry.IsInBounds(new Vector3(0, 0, 5)));
    }

    [Fact]
    public void IsPassableAt_OutOfBounds_ReturnsFalse()
    {
        Assert.False(_registry.IsPassableAt(new Vector3(-1, 0, 0)));
    }

    [Fact]
    public void GetNumZLevels_ReturnsCorrectValue()
    {
        Assert.Equal(5, _registry.GetNumZLevels());
    }

    [Fact]
    public void DefaultFill_IsAir()
    {
        // Registry pre-fills with air entities — air entities should have AirTag
        var entity = _registry.GetEntityAt(new Vector3(0, 0, 0));
        Assert.NotEqual(Entity.Null, entity);
        Assert.True(entity.Has<AirTag>());
    }
}
