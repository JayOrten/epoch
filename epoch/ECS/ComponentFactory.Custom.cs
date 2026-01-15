using System.Collections.Generic;
using Arch.Core;
using Microsoft.Xna.Framework;

namespace epoch.ECS
{
    public static partial class ComponentFactory
    {
        static partial void TrySetCustom(
            Entity entity,
            World world,
            ComponentDefinition def,
            ref bool handled
        )
        {
            switch (def.TypeName)
            {
                case "CompositeControllerComponent":
                {
                    var component = new epoch.ECS.CompositeControllerComponent();

                    var childParts = new Dictionary<string, Entity>();

                    var childOffsets = new List<Vector3>();

                    foreach (var part in def.CompositeParts)
                    {
                        // Spawn the part entity
                        Entity partEntity = GlobalContext.EntityManager.Spawn(part.EntityTemplate);

                        // Set the part entity's Position component offset if it has one
                        if (world.Has<Position>(partEntity))
                        {
                            var parentPos = world.Get<Position>(entity);
                            ref var pos = ref world.Get<Position>(partEntity);
                            pos.WorldCoordinate = parentPos.WorldCoordinate + part.Offset;
                            pos.Offset = part.Offset;
                            pos.top = parentPos.top;
                        }

                        world.Add<CompositePartComponent>(
                            partEntity,
                            new CompositePartComponent { MasterId = entity, PartLabel = part.Key }
                        );

                        childParts[part.Key] = partEntity;

                        childOffsets.Add(part.Offset);
                    }
                    component.Parts = childParts;
                    component.ChildOffsets = childOffsets;
                    world.Set<epoch.ECS.CompositeControllerComponent>(entity, component);

                    handled = true;
                    break;
                }
                case "GraphicalTileList":
                {
                    var component = new epoch.ECS.GraphicalTileList();

                    int index = 0;
                    foreach (var subpart in def.SubCompositeParts)
                    {
                        // Call the component factory to create the subpart component
                        GraphicalTile tile = ComponentFactory.Create<GraphicalTile>(subpart);
                        component.Set(index, tile);

                        index++;
                    }

                    world.Set<epoch.ECS.GraphicalTileList>(entity, component);

                    handled = true;

                    break;
                }
            }
        }
    }
}
