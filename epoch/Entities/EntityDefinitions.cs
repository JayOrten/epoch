using System;
using System.Collections.Generic;

class EntityDefinitions
{
    public Dictionary<string, List<Component>> Definitions { get; set; } =
        new Dictionary<string, List<Component>>();

    public EntityDefinitions()
    {
        // Constructor logic here
    }

    public static EntityDefinitions LoadFromFile(string path)
    {
        // Logic to load entity definitions from a file
        return new EntityDefinitions();
    }
}
