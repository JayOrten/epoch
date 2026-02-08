using Arch.Core;
using Arch.Core.Extensions;
using epoch.Input;
using Microsoft.Xna.Framework;

namespace epoch.ECS;

/// <summary>
/// Polls hardware input via <see cref="GameController"/> and writes the results
/// into <see cref="MovementInput"/> and <see cref="CameraInput"/> components.
/// Runs first in the update pipeline so downstream systems always see fresh input.
/// </summary>
public sealed class InputSystem : SystemBase<GameTime>
{
    public InputSystem(World world)
        : base(world) { }

    private Vector2 GetMovementDirection()
    {
        var movementDirection = Vector2.Zero;

        if (GameController.MoveDownHeld())
        {
            movementDirection += Vector2.UnitY;
        }
        if (GameController.MoveUpHeld())
        {
            movementDirection -= Vector2.UnitY;
        }
        if (GameController.MoveLeftHeld())
        {
            movementDirection -= Vector2.UnitX;
        }
        if (GameController.MoveRightHeld())
        {
            movementDirection += Vector2.UnitX;
        }

        return movementDirection;
    }

    private Vector2 GetLookDirection()
    {
        var lookDirection = Vector2.Zero;

        if (GameController.LookDownHeld())
        {
            lookDirection -= Vector2.UnitY;
        }
        if (GameController.LookUpHeld())
        {
            lookDirection += Vector2.UnitY;
        }
        if (GameController.LookLeftHeld())
        {
            lookDirection += Vector2.UnitX;
        }
        if (GameController.LookRightHeld())
        {
            lookDirection -= Vector2.UnitX;
        }

        return lookDirection;
    }

    private float AdjustZoom()
    {
        float zoomChange = 0;

        if (GameController.ZoomInHeld())
        {
            zoomChange += 1;
        }

        if (GameController.ZoomOutHeld())
        {
            zoomChange -= 1;
        }

        return zoomChange;
    }

    public override void Update(in GameTime gametime)
    {
        // Read hardware
        // Movement
        Vector2 movementDirection = GetMovementDirection();
        // Update the MovementInput component of the player
        GlobalContext.PlayerEntity.Get<MovementInput>().Direction = movementDirection;

        // Look
        Vector2 lookChange = GetLookDirection();
        GlobalContext.CameraEntity.Get<CameraInput>().LookChange = lookChange;

        // Zoom
        float zoomChange = AdjustZoom();
        GlobalContext.CameraEntity.Get<CameraInput>().ZoomChange = zoomChange;
    }
}
