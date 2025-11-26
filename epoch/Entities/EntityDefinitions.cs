using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Xml.Linq;
using Arch.Core;
using Arch.Core.Extensions;
using epoch;
using epoch.Components;
using epoch.Utilities.Logging;

namespace epoch.Entities;

public class EntityDefinition
{
    public string TypeName { get; set; }
    public List<ComponentDefinition> Components { get; } = new();
}

public class EntityManager
{
    private List<EntityDefinition> _entityDefs;

    private World _world;

    public EntityManager(World world, string xmlPath)
    {
        ComponentFactory.Initialize(Assembly.GetExecutingAssembly());

        _entityDefs = Parse(xmlPath);

        _world = world;
    }

    public static List<EntityDefinition> Parse(string xmlPath)
    {
        var doc = XDocument.Load(xmlPath);
        var entities = new List<EntityDefinition>();

        foreach (var entityElem in doc.Root.Elements("entity"))
        {
            var entity = new EntityDefinition { TypeName = entityElem.Attribute("name")?.Value };

            foreach (var compElem in entityElem.Elements("component"))
            {
                var compDef = new ComponentDefinition
                {
                    TypeName = compElem.Attribute("component_name")?.Value,
                };

                foreach (var attr in compElem.Attributes())
                    if (attr.Name != "component_name")
                        compDef.Properties[attr.Name.LocalName] = attr.Value;

                entity.Components.Add(compDef);
            }
            entities.Add(entity);
        }
        return entities;
    }

    public void Spawn(
        string entityName,
        Dictionary<string, Dictionary<string, string>> overrides = null
    )
    {
        overrides ??= new Dictionary<string, Dictionary<string, string>>();

        // Find the entity definition in the list matching entityName
        EntityDefinition def = _entityDefs.FirstOrDefault(e => e.TypeName == entityName);
        if (def == null)
        {
            Log.Info($"Entity definition '{entityName}' not found.");
            return;
        }

        var entity = _world.Create();

        // Get list of components
        object[] comps = def
            .Components.Select(cd =>
                (object)
                    ComponentFactory.Create(
                        cd,
                        overrides.TryGetValue(cd.TypeName, out var value) ? value : null
                    )
            )
            .ToArray();

        _world.AddRange(entity, comps.AsSpan());

        // print out component contents for debugigng
        Log.Debug(
            $"Spawned entity '{entityName}' with components: {string.Join(", ", comps.Select(c => c.GetType().Name))}"
        );
    }
}
