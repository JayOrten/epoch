using System;
using Arch.Core;
using Arch.Core.Extensions;
using Microsoft.Xna.Framework;

namespace epoch.ECS;

/// <summary>
/// Final camera pipeline stage: snaps <see cref="CameraState.Position"/> to pixel boundaries
/// (preventing sub-pixel tile seams) and applies zoom to the <see cref="OrthographicCamera"/>.
/// </summary>
public sealed class CameraApplySystem : SystemBase<GameTime>
{
    public CameraApplySystem(World world)
        : base(world) { }

    public override void Update(in GameTime gameTime)
    {
        // Apply camera state to actual camera
        ref var cameraState = ref GlobalContext.CameraEntity.Get<CameraState>();

        // Snap camera position to pixel boundaries to prevent tile seams/flickering
        float pixelSize = GlobalContext.TileManager.Tileset.TileWidth * GlobalContext.GlobalScale;
        Vector2 snappedPosition = new Vector2(
            MathF.Round(cameraState.Position.X * pixelSize) / pixelSize,
            MathF.Round(cameraState.Position.Y * pixelSize) / pixelSize
        );
        GlobalContext.Camera.Position = snappedPosition;

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
