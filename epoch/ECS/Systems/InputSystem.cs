using System;
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
        // Movement — rotate by camera so controls are relative to the view
        Vector2 movementDirection = GetMovementDirection();
        if (movementDirection != Vector2.Zero)
        {
            float rot = GlobalContext.CameraEntity.Get<CameraState>().Rotation;
            float cos = MathF.Cos(-rot);
            float sin = MathF.Sin(-rot);
            Vector2 rotated = new Vector2(
                movementDirection.X * cos - movementDirection.Y * sin,
                movementDirection.X * sin + movementDirection.Y * cos
            );
            movementDirection = new Vector2(
                MathF.Round(rotated.X),
                MathF.Round(rotated.Y)
            );
        }
        GlobalContext.PlayerEntity.Get<MovementInput>().Direction = movementDirection;

        // Zoom
        float zoomChange = AdjustZoom();
        GlobalContext.CameraEntity.Get<CameraInput>().ZoomChange = zoomChange;

        // Rotation
        float rotationChange = 0;
        if (GameController.RotateLeftHeld()) rotationChange -= 1;
        if (GameController.RotateRightHeld()) rotationChange += 1;
        GlobalContext.CameraEntity.Get<CameraInput>().RotationChange = rotationChange;

        // Elevation
        float elevationChange = 0;
        if (GameController.ElevationUpHeld()) elevationChange -= 1;
        if (GameController.ElevationDownHeld()) elevationChange += 1;
        GlobalContext.CameraEntity.Get<CameraInput>().ElevationChange = elevationChange;
    }
}
