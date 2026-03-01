using Arch.Core;
using Arch.Core.Extensions;
using Microsoft.Xna.Framework;

namespace epoch.ECS;

/// <summary>
/// Final camera pipeline stage: applies <see cref="CameraState.Position"/> and zoom
/// to the <see cref="OrthographicCamera"/>. Pixel alignment is delegated to the
/// vertex shader's clip-space snap.
/// </summary>
public sealed class CameraApplySystem : SystemBase<GameTime>
{
    public CameraApplySystem(World world)
        : base(world) { }

    public override void Update(in GameTime gameTime)
    {
        // Apply camera state to actual camera
        ref var cameraState = ref GlobalContext.CameraEntity.Get<CameraState>();

        // Camera position is applied directly — pixel alignment is handled by the
        // vertex shader's clip-space snap (round to screen pixel), which is the
        // definitive authority. A CPU-side snap here was redundant (and was only
        // snapping to 1/32 pixel anyway, i.e. sub-pixel resolution).
        GlobalContext.Camera.Position = cameraState.Position;

        if (cameraState.ZoomAmount > 0)
        {
            GlobalContext.Camera.ZoomIn(cameraState.ZoomAmount);
        }
        else if (cameraState.ZoomAmount < 0)
        {
            GlobalContext.Camera.ZoomOut(-cameraState.ZoomAmount);
        }
        cameraState.ZoomAmount = 0; // Reset after applying
    }
}
