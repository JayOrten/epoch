using System.Collections.Generic;
using System.IO;
using Arch.Core;
using epoch.ECS;
using Microsoft.Xna.Framework;

namespace epoch.Graphics.Tiles;

/// <summary>
/// Parses a text-based tilemap file into ECS entities. Each character maps to an entity
/// template ID; layers are separated by <c>"-"</c> lines (incrementing Z).
/// Periods (<c>.</c>) are treated as empty space and skipped.
/// </summary>
public static class TileMap
{
    /// <summary>
    /// Reads <paramref name="tileMapPath"/> and spawns entities via <see cref="GlobalContext.EntityManager"/>.
    /// </summary>
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
                    string coordinates = $"{column},{row},{z}";
                    GenerateTileList(coordinates, character);
                }
                column++;
            }
            row++;
        }
    }

    private static void GenerateTileList(string coordinates, char character)
    {
        if (character < '0' || character > '9')
        {
            Log.Warn("TileMap: skipping invalid character '{0}' at {1}", character, coordinates);
            return;
        }

        ComponentDefinition graphicalTileList = new ComponentDefinition("GraphicalTileList");

        EntityDefinition spawnPosition = new EntityDefinition(
            new ComponentDefinition(
                "Position",
                new Dictionary<string, string> { { "WorldCoordinate", coordinates } }
            ),
            graphicalTileList
        );

        GlobalContext.EntityManager.Spawn(character - '0', spawnPosition);
    }
}
