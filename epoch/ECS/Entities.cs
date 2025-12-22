using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Xml.Linq;
using Arch.Core;
using Arch.Core.Extensions;
using epoch;
using epoch.Generated;
using Microsoft.Xna.Framework;

namespace epoch.ECS;

/// <summary>
/// Represents a flexible definition of an entity, consisting of a type name and a collection of components.
/// Components are stored in a dictionary and can be accessed or added dynamically.
/// </summary>
/// <example>
/// // Create an entity with only a type name
/// var entity = new EntityDefinition("Enemy");
///
/// // Add a component and set properties
/// entity.Add("Position", "x", "10")
///       .Add("Position", "y", "20")
///
/// // Access or create a component via indexer
/// entity["Inventory"].Properties["Slot1"] = "Sword";
///
/// // Create entity directly from components
/// var entity2 = new EntityDefinition(
///     new ComponentDefinition("Position"),
///     new ComponentDefinition("Health")
/// );
///
/// // Create entity with type name and components
/// var entity3 = new EntityDefinition("Enemy",
///     new ComponentDefinition("Position"),
///     new ComponentDefinition("Health")
/// );
/// </example>
public class EntityDefinition
{
    public string TypeName { get; init; }
    public Dictionary<String, ComponentDefinition> Components { get; } = new();

    public EntityDefinition() { }

    public EntityDefinition(string typeName)
    {
        TypeName = typeName;
    }

    public EntityDefinition(params ComponentDefinition[] components)
    {
        foreach (var component in components)
            Components[component.TypeName] = component;
    }

    public EntityDefinition(string typeName, params ComponentDefinition[] components)
        : this(components)
    {
        TypeName = typeName;
    }

    public ComponentDefinition this[string componentName]
    {
        get =>
            Components.TryGetValue(componentName, out var comp)
                ? comp
                : Components[componentName] = new ComponentDefinition(componentName);
        set => Components[componentName] = value;
    }

    public bool TryGet(string componentName, out ComponentDefinition componentDefinition)
    {
        return Components.TryGetValue(componentName, out componentDefinition);
    }

    public EntityDefinition Add(string componentName, ComponentDefinition componentDefinition)
    {
        Components[componentName] = componentDefinition;
        return this;
    }

    public EntityDefinition Add(string componentName, string property, string value)
    {
        this[componentName].Properties[property] = value;
        return this;
    }

    public EntityDefinition Add(string componentName, Dictionary<string, string> properties)
    {
        var componentDefinition = this[componentName];
        foreach (var kvp in properties)
            componentDefinition.Properties[kvp.Key] = kvp.Value;
        return this;
    }

    /// <summary>
    /// Merges another EntityDefinition into this one.
    /// Components from the other definition will overwrite or add to this definition's components.
    /// </summary>
    public EntityDefinition Merge(EntityDefinition other)
    {
        if (other == null)
            return this;

        foreach (var kvp in other.Components)
        {
            if (Components.TryGetValue(kvp.Key, out var existingComponent))
            {
                // Merge properties
                foreach (var propKvp in kvp.Value.Properties)
                {
                    existingComponent.Properties[propKvp.Key] = propKvp.Value;
                }
            }
            else
            {
                // Add new component
                Components[kvp.Key] = kvp.Value;
            }
        }

        return this;
    }
}

public class EntityManager
{
    private Dictionary<string, EntityDefinition> _entityDefs;

    private World _world;

    private MapRegistry _mapRegistry;

    public EntityManager(World world, MapRegistry mapRegistry, string xmlPath)
    {
        _entityDefs = Parse(xmlPath);

        _world = world;

        _mapRegistry = mapRegistry;
    }

    public static Dictionary<string, EntityDefinition> Parse(string xmlPath)
    {
        var doc = XDocument.Load(xmlPath);
        var entities = new Dictionary<string, EntityDefinition>();

        foreach (var entityElem in doc.Root.Elements("entity"))
        {
            var entity = new EntityDefinition { TypeName = entityElem.Attribute("name")?.Value };

            foreach (var compElem in entityElem.Elements("component"))
            {
                var compDef = new ComponentDefinition(compElem.Attribute("component_name")?.Value);

                foreach (var attr in compElem.Attributes())
                    if (attr.Name != "component_name")
                        compDef.Properties[attr.Name.LocalName] = attr.Value;

                entity.Components[compDef.TypeName] = compDef;
            }
            entities[entity.TypeName] = entity;
        }
        return entities;
    }

    public Entity Spawn(EntityDefinition entityDefinition)
    {
        var componentTypes = new ComponentType[entityDefinition.Components.Count];
        for (int i = 0; i < entityDefinition.Components.Count; i++)
        {
            componentTypes[i] = ComponentFactory.GetArchType(
                entityDefinition.Components.Values.ToList()[i].TypeName
            );
        }
        var entity = _world.Create(componentTypes);

        foreach (var componentDefinition in entityDefinition.Components.Values.ToList())
        {
            entity.SetOnEntity(_world, componentDefinition);
        }

        // Check if there is a Position component, to see if we need to register it
        // TODO: only add if it's local? in a present chunk?
        if (entity.Has<Position>())
        {
            var comp = entity.Get<Position>();

            // Register entity in the map registry at the specified position.
            _mapRegistry.Register(
                new Vector3(comp.WorldCoordinate.X, comp.WorldCoordinate.Y, comp.zLevel),
                entity
            );
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
        EntityDefinition def = _entityDefs.TryGetValue(entityName, out var value) ? value : null;

        if (def == null)
        {
            Log.Info($"Entity definition '{entityName}' not found.", entityName);
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
