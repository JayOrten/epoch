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

            string[] tokens = line.Split(' ', System.StringSplitOptions.RemoveEmptyEntries);
            int column = 0;
            foreach (string token in tokens)
            {
                if (token != ".")
                {
                    string coordinates = $"{column},{row},{z}";
                    GenerateTileList(coordinates, token);
                }
                column++;
            }
            row++;
        }
    }

    private static void GenerateTileList(string coordinates, string token)
    {
        if (!int.TryParse(token, out int tileId))
        {
            Log.Warn("TileMap: skipping invalid token '{0}' at {1}", token, coordinates);
            return;
        }

        // ComponentDefinition graphicalTileList = new ComponentDefinition("GraphicalTileList");

        EntityDefinition spawnPosition = new EntityDefinition(
            new ComponentDefinition(
                "Position",
                new Dictionary<string, string> { { "WorldCoordinate", coordinates } }
            )
        // graphicalTileList
        );

        GlobalContext.EntityManager.Spawn(tileId, spawnPosition);
    }
}
