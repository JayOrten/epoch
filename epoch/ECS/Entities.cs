using System;
using System.Collections.Generic;
using System.Linq;
using System.Xml.Linq;
using Arch.Core;
using Arch.Core.Extensions;
using Microsoft.Xna.Framework;

namespace epoch.ECS;

public class EntityManager
{
    private Dictionary<string, int> _entityNames;
    private Dictionary<int, EntityDefinition> _entityDefs;

    private World _world;

    public EntityManager(World world, string xmlPath)
    {
        // Load and parse the XML file
        Parse(xmlPath);

        _world = world;
    }

    private void Parse(string xmlPath)
    {
        var doc = XDocument.Load(xmlPath);
        _entityNames = new Dictionary<string, int>();
        _entityDefs = new Dictionary<int, EntityDefinition>();

        foreach (var entityElem in doc.Root.Elements("entity"))
        {
            int entityId = int.Parse(entityElem.Attribute("id")?.Value ?? "0");
            var entity = new EntityDefinition { TypeName = entityElem.Attribute("name")?.Value };

            foreach (var compElem in entityElem.Elements("component"))
            {
                var compDef = new ComponentDefinition(compElem.Attribute("component_name")?.Value);

                foreach (var attr in compElem.Attributes())
                    if (attr.Name != "component_name")
                        compDef.Properties[attr.Name.LocalName] = attr.Value;

                var partsDef = compElem.Element("parts");
                if (partsDef != null)
                {
                    foreach (var partDef in partsDef.Elements("part"))
                    {
                        Vector3 offset = ParseVector3(
                            partDef.Attribute("offset")?.Value ?? "0,0,0"
                        );

                        compDef.CompositeParts.Add(
                            new PartDefinition
                            {
                                Key = partDef.Attribute("key")?.Value,
                                EntityTemplate = partDef.Attribute("entity_template")?.Value,
                                Offset = offset,
                            }
                        );
                    }
                }
                entity.Components[compDef.TypeName] = compDef;
            }
            // Store the definition
            _entityNames[entity.TypeName] = entityId;
            _entityDefs[entityId] = entity;
        }
    }

    // Simple helper to parse "0,0,1"
    private static Vector3 ParseVector3(string s)
    {
        var parts = s.Split(',');
        if (parts.Length != 3)
            return Vector3.Zero;
        return new Vector3(float.Parse(parts[0]), float.Parse(parts[1]), float.Parse(parts[2]));
    }

    public Entity Spawn(EntityDefinition entityDefinition)
    {
        // Get the component type for each expected component
        var componentTypes = new ComponentType[entityDefinition.Components.Count];
        for (int i = 0; i < entityDefinition.Components.Count; i++)
        {
            componentTypes[i] = ComponentFactory.GetArchType(
                entityDefinition.Components.Values.ToList()[i].TypeName
            );
        }

        // Create an entity based on all of the component types
        var entity = _world.Create(componentTypes);

        // Set each component with the given definition
        foreach (var componentDefinition in entityDefinition.Components.Values.ToList())
        {
            entity.SetOnEntity(_world, componentDefinition);
        }

        // Check if there is a Position component, to see if we need to register it
        // TODO: only add if it's local? in a present chunk?
        if (entity.Has<Position>())
        {
            // If the entity has the PlayerTag component, skip
            if (entity.Has<PlayerTag>())
            {
                return entity;
            }
            var comp = entity.Get<Position>();

            // Register entity in the map registry at the specified position.
            GlobalContext.MapRegistry.Register(comp.WorldCoordinate, entity);
        }

        return entity;

        // Get list of components
        // object[] components = entityDefinition
        //     .Components.Values.Select(component => (object)ComponentFactory.Create(component))
        //     .ToArray();

        // _world.AddRange(entity, components.AsSpan());

        // print out component contents for debugigng
        // Log.Debug(
        //     $"Spawned entity with components: {string.Join(", ", components.Select(c => c.GetType().Name))}"
        // );
    }

    public Entity Spawn(string entityName, EntityDefinition entityDefinitionOverride = null)
    {
        // Find the entity definition in the list matching entityName
        int entityId = _entityNames.TryGetValue(entityName, out var id) ? id : -1;
        EntityDefinition def = _entityDefs.TryGetValue(entityId, out var value) ? value : null;

        Entity entity = Spawn(entityId, entityDefinitionOverride);

        return entity;
    }

    public Entity Spawn(int entityId, EntityDefinition entityDefinitionOverride = null)
    {
        // Find the entity definition in the list matching entityId
        EntityDefinition def = _entityDefs.TryGetValue(entityId, out var value) ? value : null;

        if (def == null)
        {
            Log.Info($"Entity definition ID '{entityId}' not found.", entityId);
            var ex = new InvalidOperationException("Entity definition not found");
            throw ex;
        }

        // If an override definition is provided, merge it with the found definition
        if (entityDefinitionOverride != null)
        {
            def = def.Merge(entityDefinitionOverride);
        }

        Entity entity = Spawn(def);

        return entity;
    }
}
