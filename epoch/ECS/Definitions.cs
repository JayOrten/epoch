using System;
using System.Collections.Generic;
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
    /// Returns a deep copy of this definition (all components and their properties are cloned).
    /// </summary>
    public EntityDefinition Clone()
    {
        var clone = new EntityDefinition(TypeName);
        foreach (var kvp in Components)
            clone.Components[kvp.Key] = kvp.Value.Clone();
        return clone;
    }

    /// <summary>
    /// Merges another EntityDefinition into this one (mutates <c>this</c>).
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
                // foreach (var propKvp in kvp.Value.Properties)
                // {
                //     existingComponent.Properties[propKvp.Key] = propKvp.Value;
                // }
                existingComponent.Merge(kvp.Value);
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

/// <summary>
/// Represents a definition of a component, consisting of a type name and a collection of properties.
/// Properties are stored in a dictionary and can be accessed or added dynamically.
/// </summary>
public class ComponentDefinition
{
    public string TypeName { get; set; }

    public Dictionary<string, string> Properties { get; } = new();

    public List<PartDefinition> CompositeParts { get; } = new();

    public List<ComponentDefinition> SubCompositeParts { get; } = new();

    public string this[string property]
    {
        get => Properties.TryGetValue(property, out var value) ? value : null;
        set => Properties[property] = value;
    }

    public bool TryGet(string property, out string value)
    {
        return Properties.TryGetValue(property, out value);
    }

    public ComponentDefinition(string typeName)
    {
        TypeName = typeName;
    }

    public ComponentDefinition(string typeName, Dictionary<string, string> properties)
        : this(typeName)
    {
        foreach (var kvp in properties)
            Properties[kvp.Key] = kvp.Value;
    }

    public ComponentDefinition Add(string property, string value)
    {
        Properties[property] = value;
        return this;
    }

    /// <summary>
    /// Returns a deep copy of this component definition.
    /// </summary>
    public ComponentDefinition Clone()
    {
        var clone = new ComponentDefinition(TypeName);
        foreach (var kvp in Properties)
            clone.Properties[kvp.Key] = kvp.Value;
        foreach (var part in CompositeParts)
            clone.CompositeParts.Add(new PartDefinition
            {
                Key = part.Key,
                EntityTemplate = part.EntityTemplate,
                Offset = part.Offset,
            });
        foreach (var sub in SubCompositeParts)
            clone.SubCompositeParts.Add(sub.Clone());
        return clone;
    }

    public ComponentDefinition Merge(ComponentDefinition other)
    {
        if (other == null)
            return this;

        foreach (var kvp in other.Properties)
            Properties[kvp.Key] = kvp.Value;

        // Merge the SubCompositeParts: replace the existing parts in this one with the parts from the other at the same index
        for (int i = 0; i < other.SubCompositeParts.Count; i++)
        {
            if (i < SubCompositeParts.Count)
            {
                // SubCompositeParts[i] = other.SubCompositeParts[i];
                SubCompositeParts[i].Merge(other.SubCompositeParts[i]);
            }
            else
            {
                // SubCompositeParts.Add(other.SubCompositeParts[i]);
                SubCompositeParts.Add(other.SubCompositeParts[i]);
            }
        }

        return this;
    }
}

/// <summary>
/// Represents a definition of a part, including its key, entity template, and offset.
/// A part represents a nested entity within another entity.
/// </summary>
public class PartDefinition
{
    public string Key { get; set; }
    public string EntityTemplate { get; set; }
    public Vector3 Offset { get; set; }
}
