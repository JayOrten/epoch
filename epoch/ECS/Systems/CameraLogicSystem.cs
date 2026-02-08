using Arch.Core;
using Arch.Core.Extensions;
using epoch.Utilities;
using Microsoft.Xna.Framework;
using MonoGame.Extended;

namespace epoch.ECS;

/// <summary>
/// Computes the camera's target position each frame by smooth-damping toward the player,
/// applying a predictive lead offset in the movement direction, and processing
/// look/zoom input from <see cref="CameraInput"/>.
/// </summary>
public sealed class CameraLogicSystem : SystemBase<GameTime>
{
    private float smoothTime = 0.45f; // Time to move camera to target (player)
    private float zoomSpeed = 0.01f; // Speed of zooming
    private float lookSpeed = 15.0f; // Speed of looking around
    private float clampLength = 500.0f; // Max length of look direction

    private Vector2 _camVelocity;
    private Vector2 _leadOffset;
    private float _leadRampUp = 2.0f; // How fast the lead engages
    private float _leadRampDown = 1.0f; // How fast the lead disengages (slower = gentler snap-back)

    public CameraLogicSystem(World world)
        : base(world) { }

    public override void Update(in GameTime gameTime)
    {
        float delta = gameTime.GetElapsedSeconds();

        ref var playerPos = ref GlobalContext.PlayerEntity.Get<Position>();
        ref var movementInput = ref GlobalContext.PlayerEntity.Get<MovementInput>();

        Vector2 playerGridPos = new Vector2(
            playerPos.WorldCoordinate.X,
            playerPos.WorldCoordinate.Y
        );

        // Predictive lead: smoothly ramp toward one tile ahead when holding a direction,
        // smoothly decay back to zero when released
        Vector2 targetLead = Vector2.Zero;
        if (movementInput.Direction != Vector2.Zero)
        {
            Vector3 predictedCoord =
                playerPos.WorldCoordinate + new Vector3(movementInput.Direction, 0);

            if (
                GlobalContext.MapRegistry.IsPassableAt(predictedCoord)
                || GlobalContext.MapRegistry.IsPassableAt(predictedCoord + new Vector3(0, 0, 1))
            )
            {
                targetLead = movementInput.Direction;
            }
        }

        float rampSpeed = (targetLead != Vector2.Zero) ? _leadRampUp : _leadRampDown;
        _leadOffset = Vector2.Lerp(_leadOffset, targetLead, rampSpeed * delta);

        Vector2 playerPosition =
            (playerGridPos + _leadOffset)
            * GlobalContext.GlobalScale
            * GlobalContext.TileManager.Tileset.TileHeight;

        Vector2 targetPosition =
            playerPosition
            - new Vector2(
                Core.Graphics.PreferredBackBufferWidth / 2,
                Core.Graphics.PreferredBackBufferHeight / 2
            );

        GlobalContext.CameraEntity.Get<CameraState>().Position = CameraUtils.SmoothDamp(
            GlobalContext.CameraEntity.Get<CameraState>().Position,
            targetPosition,
            ref _camVelocity,
            smoothTime,
            float.MaxValue,
            gameTime.GetElapsedSeconds()
        );

        // Apply zoom
        float zoomChange = GlobalContext.CameraEntity.Get<CameraInput>().ZoomChange;
        GlobalContext.CameraEntity.Get<CameraState>().ZoomAmount += (zoomChange * zoomSpeed);
        GlobalContext.CameraEntity.Get<CameraInput>().ZoomChange = 0; // Reset after applying

        // Apply Look Direction
        Vector2 currentLookDirection = GlobalContext.CameraEntity.Get<CameraState>().LookDirection;
        Vector2 lookChange = GlobalContext.CameraEntity.Get<CameraInput>().LookChange;

        // 1. Calculate the tentative new position
        Vector2 newLook = currentLookDirection + (lookChange * lookSpeed);

        // 2. CIRCULAR CLAMP
        // We check LengthSquared() because it is faster than Length() (avoids square root)
        if (newLook.LengthSquared() > clampLength * clampLength)
        {
            // Normalize gets the direction (length of 1), then we multiply by radius
            newLook = Vector2.Normalize(newLook) * clampLength;
        }

        GlobalContext.CameraEntity.Get<CameraState>().LookDirection = newLook;

        // TODO: add previous state update for different refresh rates?
    }
}
