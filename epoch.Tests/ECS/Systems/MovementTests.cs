using Arch.Core;
using epoch.ECS;
using Microsoft.Xna.Framework;

namespace epoch.Tests.ECS.Systems;

public class MovementTests
{
    private World _world;
    private MapRegistry _registry;

    public MovementTests()
    {
        _world = World.Create();
        // 5x5x5 grid
        _registry = new MapRegistry(_world, 5, 5, 5);
    }

    private void PlaceSolid(Vector3 coord)
    {
        var entity = _world.Create(new Position { WorldCoordinate = coord, Passable = false });
        _registry.Register(coord, entity);
    }

    private void PlacePassable(Vector3 coord)
    {
        var entity = _world.Create(new Position { WorldCoordinate = coord, Passable = true });
        _registry.Register(coord, entity);
    }

    // ── ResolveMovement ─────────────────────────────────────────────

    [Fact]
    public void ResolveMovement_FlatGround()
    {
        // Target is passable (non-air, passable entity) → can move, same Z
        PlacePassable(new Vector3(2, 1, 1));

        var (canMove, newCoord) = MovementSystem.ResolveMovement(
            new Vector3(1, 1, 1),
            new Vector2(1, 0), // east
            _registry
        );

        Assert.True(canMove);
        Assert.Equal(new Vector3(2, 1, 1), newCoord);
    }

    [Fact]
    public void ResolveMovement_Wall()
    {
        // Target blocked, above target also blocked → can't move
        PlaceSolid(new Vector3(2, 1, 1));
        PlaceSolid(new Vector3(2, 1, 2));

        var (canMove, _) = MovementSystem.ResolveMovement(
            new Vector3(1, 1, 1),
            new Vector2(1, 0),
            _registry
        );

        Assert.False(canMove);
    }

    [Fact]
    public void ResolveMovement_StepUp()
    {
        // Target blocked, but tile above target is passable → step up
        PlaceSolid(new Vector3(2, 1, 1));
        PlacePassable(new Vector3(2, 1, 2));

        var (canMove, newCoord) = MovementSystem.ResolveMovement(
            new Vector3(1, 1, 1),
            new Vector2(1, 0),
            _registry
        );

        Assert.True(canMove);
        Assert.Equal(new Vector3(2, 1, 2), newCoord);
    }

    [Fact]
    public void ResolveMovement_AirAhead_GroundBelow()
    {
        // Target is air (default), tile below target is solid ground → same Z
        // Registry default is air, so (2,1,1) is already air
        PlaceSolid(new Vector3(2, 1, 0));

        var (canMove, newCoord) = MovementSystem.ResolveMovement(
            new Vector3(1, 1, 1),
            new Vector2(1, 0),
            _registry
        );

        Assert.True(canMove);
        // Ground below is not air → no fall → stay at same Z
        Assert.Equal(new Vector3(2, 1, 1), newCoord);
    }

    [Fact]
    public void ResolveMovement_Fall()
    {
        // Target is air, tile below target is also air → drop Z by 1
        // Both (2,1,1) and (2,1,0) are air (default)

        var (canMove, newCoord) = MovementSystem.ResolveMovement(
            new Vector3(1, 1, 1),
            new Vector2(1, 0),
            _registry
        );

        Assert.True(canMove);
        Assert.Equal(new Vector3(2, 1, 0), newCoord);
    }

    // ── CheckCompositeCollision ─────────────────────────────────────

    [Fact]
    public void CheckCompositeCollision_AllClear()
    {
        PlacePassable(new Vector3(2, 1, 1));
        PlacePassable(new Vector3(2, 1, 2));

        var composite = new CompositeControllerComponent
        {
            ChildOffsets = [new Vector3(0, 0, 0), new Vector3(0, 0, 1)],
        };

        bool result = MovementSystem.CheckCompositeCollision(
            composite,
            new Vector3(2, 1, 1),
            _registry
        );

        Assert.True(result);
    }

    [Fact]
    public void CheckCompositeCollision_OneBlocked()
    {
        PlacePassable(new Vector3(2, 1, 1));
        PlaceSolid(new Vector3(2, 1, 2));

        var composite = new CompositeControllerComponent
        {
            ChildOffsets = [new Vector3(0, 0, 0), new Vector3(0, 0, 1)],
        };

        bool result = MovementSystem.CheckCompositeCollision(
            composite,
            new Vector3(2, 1, 1),
            _registry
        );

        Assert.False(result);
    }
}
