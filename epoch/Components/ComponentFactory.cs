using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using epoch.Utilities;

namespace epoch.Components;

public class ComponentDefinition
{
    public string TypeName { get; set; }

    public Dictionary<string, string> Properties { get; } = new();

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

    public ComponentDefinition Merge(ComponentDefinition other)
    {
        if (other == null)
            return this;

        foreach (var kvp in other.Properties)
            Properties[kvp.Key] = kvp.Value;

        return this;
    }
}

public static class ComponentFactory
{
    private static Dictionary<string, Type> _componentTypes;

    // Call at startup to scan assembly for component classes
    public static void Initialize(Assembly assembly)
    {
        _componentTypes = assembly
            .GetTypes()
            .Where(t => t.IsClass && !t.IsAbstract && typeof(Component).IsAssignableFrom(t))
            .ToDictionary(t => t.Name, t => t);
    }

    // Create a component from its definition object
    public static Component Create(ComponentDefinition def)
    {
        if (!_componentTypes.TryGetValue(def.TypeName, out var type))
            throw new Exception($"Unkown component type: {def.TypeName}");

        // type is a Type from the Types list
        var instance = (Component)Activator.CreateInstance(type);

        var props = new Dictionary<string, string>(def.Properties);

        // set properties via reflection
        foreach (var kv in props)
        {
            var propInfo = type.GetProperty(
                kv.Key,
                BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase
            );
            if (propInfo == null)
                continue;

            var typedValue = Utils.ConvertValue(kv.Value, propInfo.PropertyType);
            propInfo.SetValue(instance, typedValue);
        }

        return instance;
    }
}
