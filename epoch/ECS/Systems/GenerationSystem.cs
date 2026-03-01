using Arch.Core;
using Arch.Core.Extensions;
using Microsoft.Xna.Framework;

namespace epoch.ECS;

/// <summary>
/// Thin ECS system that drives chunk lifecycle each frame.
/// Reads player position and delegates to <see cref="ChunkRegistry.Update"/>.
/// </summary>
public sealed class GenerationSystem : SystemBase<GameTime>
{
    public GenerationSystem(World world)
        : base(world) { }

    public override void Update(in GameTime gameTime)
    {
        ref var pos = ref GlobalContext.PlayerEntity.Get<Position>();
        GlobalContext.ChunkRegistry.Update(pos.WorldCoordinate.X, pos.WorldCoordinate.Y);
    }
}
