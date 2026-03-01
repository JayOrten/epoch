using Arch.Core;
using epoch.ECS;
using Microsoft.Xna.Framework;

namespace epoch.Tests.ECS.Systems;

public class MovementTests
{
    private World _world;
    private ChunkRegistry _registry;

    public MovementTests()
    {
        _world = World.Create();
        _registry = new ChunkRegistry(_world, 16, 32);
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
        // Target is air (passable, missing), tile below target is solid ground → same Z
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
