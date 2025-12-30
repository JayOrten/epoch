using System.Collections.Generic;
using System.IO;
using Arch.Core;
using epoch.ECS;
using Microsoft.Xna.Framework;

namespace epoch.Engine.Graphics.Tiles;

public static class TileMap
{
    public static void LoadTileMap(string tileMapPath, World world)
    {
        // Iterate through each row of the tilemap txt file,
        // adding a new entity for each index
        string[] lines = File.ReadAllLines(tileMapPath);

        int z = 0;
        int row = 0;
        foreach (string line in lines)
        {
            // If the line is empty, go to the next layer
            if (line == "-")
            {
                z++;
                row = 0;
                continue;
            }

            int column = 0;
            foreach (char character in line)
            {
                // If the character is a period, skip it
                // OR if character is new line
                if (character != '.' && character != '\n' && character != '\r')
                {
                    // Vector3 coordinates = new Vector3(column, row, z);
                    string coordinates = $"{column},{row},{z}";
                    // Log.Info(
                    //     $"Spawning tile entity at {coordinates} with TileId {character - '0'}"
                    // );
                    EntityDefinition spawnPosition = new EntityDefinition(
                        new ComponentDefinition(
                            "Position",
                            new Dictionary<string, string> { { "WorldCoordinate", coordinates } }
                        ),
                        new ComponentDefinition(
                            "GraphicalTile",
                            new Dictionary<string, string>
                            {
                                { "BackgroundColor", "30,32,48,255" },
                                { "BorderColor", "75,75,75,150" },
                            }
                        )
                    );

                    GlobalContext.EntityManager.Spawn(character - '0', spawnPosition);
                }
                column++;
            }
            row++;
        }
    }
}
