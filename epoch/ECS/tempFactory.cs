using System;
using System.Collections.Generic;
using Arch.Core;
using epoch.Utilities;
using Microsoft.Xna.Framework;

namespace epoch.ECS
{
    public static class ComponentFactory
    {
        public static ComponentType GetArchType(string typeName)
        {
            switch (typeName)
            {
                case "PlayerTag":
                    return Component<epoch.ECS.PlayerTag>.ComponentType;
                case "GraphicalTile":
                    return Component<epoch.ECS.GraphicalTile>.ComponentType;
                case "Position":
                    return Component<epoch.ECS.Position>.ComponentType;
                case "Direction":
                    return Component<epoch.ECS.Direction>.ComponentType;
                case "MovementInput":
                    return Component<epoch.ECS.MovementInput>.ComponentType;
                case "CompositeControllerComponent":
                    return Component<epoch.ECS.CompositeControllerComponent>.ComponentType;
                case "CompositePartComponent":
                    return Component<epoch.ECS.CompositePartComponent>.ComponentType;
                case "CameraInput":
                    return Component<epoch.ECS.CameraInput>.ComponentType;
                case "CameraState":
                    return Component<epoch.ECS.CameraState>.ComponentType;
                case "CameraPreviousState":
                    return Component<epoch.ECS.CameraPreviousState>.ComponentType;
                default:
                    throw new ArgumentException($"Unknown component: {typeName}");
            }
        }

        public static void SetOnEntity(this Entity entity, World world, ComponentDefinition def)
        {
            switch (def.TypeName)
            {
                case "PlayerTag":
                {
                    var component = new epoch.ECS.PlayerTag();
                    world.Set<epoch.ECS.PlayerTag>(entity, component);
                    break;
                }
                case "GraphicalTile":
                {
                    var component = new epoch.ECS.GraphicalTile();
                    if (def.TryGet("TileId", out var val_TileId))
                        component.TileId = ComponentParsers.ParseInt(val_TileId);
                    if (def.TryGet("Scale", out var val_Scale))
                        component.Scale = ComponentParsers.ParseFloat(val_Scale);
                    if (def.TryGet("SpriteColor", out var val_SpriteColor))
                        component.SpriteColor = ComponentParsers.ParseColor(val_SpriteColor);
                    if (def.TryGet("BackgroundColor", out var val_BackgroundColor))
                        component.BackgroundColor = ComponentParsers.ParseColor(
                            val_BackgroundColor
                        );
                    if (def.TryGet("BorderColor", out var val_BorderColor))
                        component.BorderColor = ComponentParsers.ParseColor(val_BorderColor);
                    if (def.TryGet("BorderMask", out var val_BorderMask))
                        component.BorderMask = ComponentParsers.ParseInt(val_BorderMask);
                    if (def.TryGet("BorderWidth", out var val_BorderWidth))
                        component.BorderWidth = ComponentParsers.ParseFloat(val_BorderWidth);
                    if (def.TryGet("IsDirty", out var val_IsDirty))
                        component.IsDirty = ComponentParsers.ParseBool(val_IsDirty);
                    world.Set<epoch.ECS.GraphicalTile>(entity, component);
                    break;
                }
                case "Position":
                {
                    var component = new epoch.ECS.Position();
                    if (def.TryGet("WorldCoordinate", out var val_WorldCoordinate))
                        component.WorldCoordinate = ComponentParsers.ParseVector2(
                            val_WorldCoordinate
                        );
                    if (def.TryGet("zLevel", out var val_zLevel))
                        component.zLevel = ComponentParsers.ParseFloat(val_zLevel);
                    if (def.TryGet("top", out var val_top))
                        component.top = ComponentParsers.ParseFloat(val_top);
                    if (def.TryGet("Offset", out var val_Offset))
                        component.Offset = ComponentParsers.ParseVector3(val_Offset);
                    world.Set<epoch.ECS.Position>(entity, component);
                    break;
                }
                case "Direction":
                {
                    var component = new epoch.ECS.Direction();
                    if (def.TryGet("FaceDirection", out var val_FaceDirection))
                        component.FaceDirection = ComponentParsers.ParseVector2(val_FaceDirection);
                    world.Set<epoch.ECS.Direction>(entity, component);
                    break;
                }
                case "MovementInput":
                {
                    var component = new epoch.ECS.MovementInput();
                    if (def.TryGet("Direction", out var val_Direction))
                        component.Direction = ComponentParsers.ParseVector2(val_Direction);
                    world.Set<epoch.ECS.MovementInput>(entity, component);
                    break;
                }
                case "CompositeControllerComponent":
                {
                    var component = new epoch.ECS.CompositeControllerComponent();

                    var childParts = new Dictionary<string, Entity>();

                    foreach (var part in def.CompositeParts)
                    {
                        // Spawn the part entity
                        Entity partEntity = GlobalContext.EntityManager.Spawn(part.EntityTemplate);

                        // Set the part entity's Position component offset if it has one
                        if (world.Has<Position>(partEntity))
                        {
                            var parentPos = world.Get<Position>(entity);
                            ref var pos = ref world.Get<Position>(partEntity);
                            pos.WorldCoordinate =
                                parentPos.WorldCoordinate
                                + new Vector2(part.Offset.X, part.Offset.Y);
                            pos.zLevel = parentPos.zLevel + part.Offset.Z;
                            pos.Offset = part.Offset;
                            pos.top = parentPos.top;
                        }

                        world.Add<CompositePartComponent>(
                            partEntity,
                            new CompositePartComponent { MasterId = entity, PartLabel = part.Key }
                        );

                        childParts[part.Key] = partEntity;
                    }
                    component.Parts = childParts;
                    world.Set<epoch.ECS.CompositeControllerComponent>(entity, component);
                    break;
                }
                case "CameraInput":
                {
                    var component = new epoch.ECS.CameraInput();
                    if (def.TryGet("LookChange", out var val_LookChange))
                        component.LookChange = ComponentParsers.ParseVector2(val_LookChange);
                    if (def.TryGet("ZoomChange", out var val_ZoomChange))
                        component.ZoomChange = ComponentParsers.ParseFloat(val_ZoomChange);
                    world.Set<epoch.ECS.CameraInput>(entity, component);
                    break;
                }
                case "CameraState":
                {
                    var component = new epoch.ECS.CameraState();
                    if (def.TryGet("Position", out var val_Position))
                        component.Position = ComponentParsers.ParseVector2(val_Position);
                    if (def.TryGet("LookDirection", out var val_LookDirection))
                        component.LookDirection = ComponentParsers.ParseVector2(val_LookDirection);
                    if (def.TryGet("ZoomAmount", out var val_ZoomAmount))
                        component.ZoomAmount = ComponentParsers.ParseFloat(val_ZoomAmount);
                    world.Set<epoch.ECS.CameraState>(entity, component);
                    break;
                }
                case "CameraPreviousState":
                {
                    var component = new epoch.ECS.CameraPreviousState();
                    if (def.TryGet("Position", out var val_Position))
                        component.Position = ComponentParsers.ParseVector2(val_Position);
                    if (def.TryGet("Zoom", out var val_Zoom))
                        component.Zoom = ComponentParsers.ParseFloat(val_Zoom);
                    world.Set<epoch.ECS.CameraPreviousState>(entity, component);
                    break;
                }
                default:
                    throw new ArgumentException($"Unknown component: {def.TypeName}");
            }
        }
    }
}
