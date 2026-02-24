using System.Runtime.CompilerServices;
using Arch.Core;
using Microsoft.Xna.Framework;
using MonoGame.Extended;

namespace epoch.ECS;

/// <summary>
/// Processes tile-based movement for entities with <see cref="MovementInput"/>.
/// Handles collision detection against the <see cref="MapRegistry"/>, including
/// slope traversal (step up/down one Z level) and composite entity checks.
/// Movement is timer-gated by <see cref="Movement.MoveDelay"/>.
/// </summary>
public sealed class MovementSystem : SystemBase<GameTime>
{
    public MovementSystem(World world)
        : base(world) { }

    /// <summary>
    /// Resolves where an entity ends up when moving in <paramref name="direction"/>.
    /// Handles step-up (blocked tile with open tile above), step-down (air with ground below),
    /// and falling (air with air below).
    /// </summary>
    /// <returns><c>(true, newCoord)</c> if movement is possible, <c>(false, _)</c> if blocked.</returns>
    public static (bool canMove, Vector3 newCoordinate) ResolveMovement(
        Vector3 currentCoordinate,
        Vector2 direction,
        MapRegistry registry
    )
    {
        Vector3 newCoordinate = currentCoordinate + new Vector3(direction, 0.0f);

        if (!registry.IsPassableAt(newCoordinate))
        {
            // Blocked — try stepping up one Z level
            Vector3 coordinateAbove = newCoordinate;
            coordinateAbove.Z++;

            if (registry.IsPassableAt(coordinateAbove))
            {
                newCoordinate.Z++;
                return (true, newCoordinate);
            }

            return (false, newCoordinate);
        }

        if (registry.GetEntityAt(newCoordinate) == Entity.Null)
        {
            // Target is empty (air) — check if ground exists below
            Vector3 coordinateBelow = newCoordinate;
            coordinateBelow.Z--;

            if (registry.GetEntityAt(coordinateBelow) == Entity.Null)
            {
                // TODO: add proper falling. For now, just drop one level
                newCoordinate.Z--;
            }
        }

        return (true, newCoordinate);
    }

    /// <summary>
    /// Checks if all child offsets of a composite entity can move to <paramref name="newCoordinate"/>.
    /// </summary>
    public static bool CheckCompositeCollision(
        CompositeControllerComponent composite,
        Vector3 newCoordinate,
        MapRegistry registry
    )
    {
        foreach (Vector3 childOffset in composite.ChildOffsets)
        {
            if (!registry.IsPassableAt(childOffset + newCoordinate))
                return false;
        }
        return true;
    }

    public override void Update(in GameTime gameTime)
    {
        float delta = gameTime.GetElapsedSeconds();

        var queryDescription = new QueryDescription().WithAll<
            Position,
            MovementInput,
            Movement,
            Direction
        >();
        var query = World.Query(in queryDescription);
        foreach (ref var chunk in query.GetChunkIterator())
        {
            var entityParams = chunk.Entities;
            var references = chunk.GetFirst<Position, MovementInput, Movement, Direction>();

            foreach (var index in chunk)
            {
                var entity = entityParams[index];
                ref var position = ref Unsafe.Add(ref references.t0, index);
                ref var movementInput = ref Unsafe.Add(ref references.t1, index);
                ref var movement = ref Unsafe.Add(ref references.t2, index);
                ref var direction = ref Unsafe.Add(ref references.t3, index);

                if (movement.CurrentTimer > 0)
                    movement.CurrentTimer -= delta;

                Vector2 movementDirection = movementInput.Direction;
                if (movementDirection == Vector2.Zero)
                {
                    movement.CurrentTimer = 0f;
                    continue;
                }

                if (movement.CurrentTimer > 0)
                    continue;

                var (canMove, newCoordinate) = ResolveMovement(
                    position.WorldCoordinate,
                    movementDirection,
                    GlobalContext.MapRegistry
                );

                bool hasComposite = World.TryGet(
                    entity,
                    out CompositeControllerComponent compositeController
                );

                if (canMove && hasComposite)
                {
                    canMove = CheckCompositeCollision(
                        compositeController,
                        newCoordinate,
                        GlobalContext.MapRegistry
                    );
                }

                if (canMove)
                {
                    position.WorldCoordinate = newCoordinate;
                    // TODO: update mapregistry?

                    direction.FaceDirection = movementDirection;

                    if (hasComposite)
                    {
                        foreach (Entity childEntity in compositeController.Parts.Values)
                        {
                            ref var childPosition = ref World.Get<Position>(childEntity);
                            childPosition.WorldCoordinate = newCoordinate + childPosition.Offset;
                            // TODO: update mapregistry?
                        }
                    }
                    movement.CurrentTimer = movement.MoveDelay;
                }
            }
        }
    }
}
