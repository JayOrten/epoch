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
    private float rotationSpeed = 3.0f; // Radians per second
    private float elevationSpeed = 300.0f; // Pixels per second
    private float minVpDistance = 0f; // Directly overhead
    private float maxVpDistance = 1200f;

    private Vector2 _camVelocity;
    private Vector2 _leadOffset;
    private float _leadRampUp = 1.0f; // How fast the lead engages
    private float _leadRampDown = 3.0f; // How fast the lead disengages (slower = gentler snap-back)

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
                GlobalContext.ChunkRegistry.IsPassableAt(predictedCoord)
                || GlobalContext.ChunkRegistry.IsPassableAt(predictedCoord + new Vector3(0, 0, 1))
            )
            {
                targetLead = movementInput.Direction;
            }
        }

        float rampSpeed = (targetLead != Vector2.Zero) ? _leadRampUp : _leadRampDown;
        _leadOffset = Vector2.Lerp(_leadOffset, targetLead, rampSpeed * delta);
        if (Vector2.DistanceSquared(_leadOffset, targetLead) < 0.5f)
            _leadOffset = targetLead;

        float tileWorldSize =
            GlobalContext.GlobalScale * GlobalContext.TileManager.Tileset.TileHeight;
        Vector2 playerPosition =
            (playerGridPos + _leadOffset) * tileWorldSize
            + new Vector2(tileWorldSize * 0.5f, tileWorldSize * 0.5f);

        Vector2 targetPosition =
            playerPosition
            - new Vector2(
                Core.Graphics.PreferredBackBufferWidth / 2,
                Core.Graphics.PreferredBackBufferHeight / 2
            );

        ref var cameraState = ref GlobalContext.CameraEntity.Get<CameraState>();
        float distSq = Vector2.DistanceSquared(cameraState.Position, targetPosition);
        if (distSq > 0.5f)
        {
            cameraState.Position = CameraUtils.SmoothDamp(
                cameraState.Position,
                targetPosition,
                ref _camVelocity,
                smoothTime,
                float.MaxValue,
                delta
            );
        }
        else
        {
            cameraState.Position = targetPosition;
            _camVelocity = Vector2.Zero;
        }

        // Apply zoom
        float zoomChange = GlobalContext.CameraEntity.Get<CameraInput>().ZoomChange;
        GlobalContext.CameraEntity.Get<CameraState>().ZoomAmount += (zoomChange * zoomSpeed);
        GlobalContext.CameraEntity.Get<CameraInput>().ZoomChange = 0; // Reset after applying

        // Apply Rotation
        float rotationInput = GlobalContext.CameraEntity.Get<CameraInput>().RotationChange;
        cameraState.Rotation += rotationInput * rotationSpeed * delta;
        GlobalContext.CameraEntity.Get<CameraInput>().RotationChange = 0;

        // Apply Elevation (VP distance)
        float elevationInput = GlobalContext.CameraEntity.Get<CameraInput>().ElevationChange;
        cameraState.VpDistance = MathHelper.Clamp(
            cameraState.VpDistance + elevationInput * elevationSpeed * delta,
            minVpDistance,
            maxVpDistance
        );
        GlobalContext.CameraEntity.Get<CameraInput>().ElevationChange = 0;

        // TODO: add previous state update for different refresh rates?
    }
}
