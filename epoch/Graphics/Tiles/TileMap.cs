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
        string[] lines = File.ReadAllLines(tileMapPath);

        int z = 0;
        int row = 0;
        foreach (string line in lines)
        {
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
                    GenerateTileList(column, row, z, token);
                }
                column++;
            }
            row++;
        }
    }

    private static void GenerateTileList(int x, int y, int z, string token)
    {
        if (!int.TryParse(token, out int tileId))
        {
            Log.Warn("TileMap: skipping invalid token '{0}' at {1},{2},{3}", token, x, y, z);
            return;
        }

        Vector3 pos = new Vector3(x, y, z);
        GlobalContext.EntityManager.SpawnTerrain(tileId, pos);
    }
}
