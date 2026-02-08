using System;
using System.Collections.Generic;
using System.Linq;
using System.Xml.Linq;
using Arch.Core;
using Arch.Core.Extensions;
using Microsoft.Xna.Framework;

namespace epoch.ECS;

/// <summary>
/// Loads entity templates from XML and spawns entities into the ECS world.
/// Templates are keyed by both name and numeric ID; spawning supports
/// merging override definitions on top of the base template.
/// </summary>
public class EntityManager
{
    private Dictionary<string, int> _entityNames;
    private Dictionary<int, EntityDefinition> _entityDefs;

    private World _world;

    /// <param name="world">The Arch ECS world to spawn entities into.</param>
    /// <param name="xmlPath">Path to the entity definitions XML file.</param>
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
                var compDef = ParseComponentElement(compElem);
                entity.Components[compDef.TypeName] = compDef;
            }

            _entityNames[entity.TypeName] = entityId;
            _entityDefs[entityId] = entity;
        }
    }

    /// <summary>
    /// Parses a single <c>&lt;component&gt;</c> XML element into a <see cref="ComponentDefinition"/>,
    /// including any nested <c>&lt;parts&gt;</c> and <c>&lt;subparts&gt;</c>.
    /// </summary>
    internal static ComponentDefinition ParseComponentElement(XElement compElem)
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

        var subpartsDef = compElem.Element("subparts");
        if (subpartsDef != null)
        {
            foreach (var subpartDef in subpartsDef.Elements("part"))
            {
                var subcompDef = new ComponentDefinition(
                    subpartDef.Attribute("component_name")?.Value
                );

                foreach (var attr in subpartDef.Attributes())
                    if (attr.Name != "component_name")
                        subcompDef.Properties[attr.Name.LocalName] = attr.Value;

                compDef.SubCompositeParts.Add(subcompDef);
            }
        }

        return compDef;
    }

    // Simple helper to parse "0,0,1"
    // TODO: move this to utilities
    private static Vector3 ParseVector3(string s)
    {
        var parts = s.Split(',');
        if (parts.Length != 3)
            return Vector3.Zero;
        return new Vector3(float.Parse(parts[0]), float.Parse(parts[1]), float.Parse(parts[2]));
    }

    /// <summary>
    /// Spawns a new entity from a fully-resolved <see cref="EntityDefinition"/>.
    /// Registers non-player entities with the <see cref="MapRegistry"/> and
    /// adds a <see cref="DirtyTag"/> for initial adjacency calculation.
    /// </summary>
    /// <returns>The newly created entity.</returns>
    /// <remarks>
    /// Alternative approach considered: batch-create via AddRange:
    /// <code>
    /// object[] components = entityDefinition
    ///     .Components.Values.Select(c => (object)ComponentFactory.Create(c))
    ///     .ToArray();
    /// _world.AddRange(entity, components.AsSpan());
    /// </code>
    /// </remarks>
    public Entity Spawn(EntityDefinition entityDefinition)
    {
        // Get the component type for each expected component
        var componentValues = entityDefinition.Components.Values;
        var componentTypes = new ComponentType[componentValues.Count];
        int i = 0;
        foreach (var comp in componentValues)
        {
            componentTypes[i++] = ComponentFactory.GetArchType(comp.TypeName);
        }

        // Create an entity based on all of the component types
        var entity = _world.Create(componentTypes);

        // Set each component with the given definition
        foreach (var componentDefinition in componentValues)
        {
            entity.SetOnEntity(_world, componentDefinition);
        }

        // Add the dirty tag to the new entity
        entity.Add<DirtyTag>();

        // Check if there is a Position component, to see if we need to register it
        // TODO: only add if it's local? in a present chunk?
        if (entity.Has<Position>())
        {
            // Skip player registration — player position is tracked separately
            if (entity.Has<PlayerTag>())
                return entity;

            var pos = entity.Get<Position>();
            GlobalContext.MapRegistry.Register(pos.WorldCoordinate, entity);
        }

        return entity;
    }

    /// <summary>Spawns an entity by template name, optionally merging overrides.</summary>
    public Entity Spawn(string entityName, EntityDefinition entityDefinitionOverride = null)
    {
        int entityId = _entityNames.TryGetValue(entityName, out var id) ? id : -1;
        return Spawn(entityId, entityDefinitionOverride);
    }

    /// <summary>Spawns an entity by template ID, optionally merging overrides. Throws if ID not found.</summary>
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

        // Clone before merging so the stored template isn't mutated
        if (entityDefinitionOverride != null)
        {
            def = def.Clone().Merge(entityDefinitionOverride);
        }

        Entity entity = Spawn(def);

        return entity;
    }
}
