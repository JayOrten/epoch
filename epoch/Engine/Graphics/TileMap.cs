using System.IO;
using Arch.Core;
using epoch.ECS;
using Microsoft.Xna.Framework;

namespace epoch.Engine.Graphics;

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
            if (string.IsNullOrEmpty(line))
            {
                z++;
                row = 0;
                continue;
            }

            int column = 0;
            foreach (char character in line)
            {
                Vector3 coordinates = new Vector3(column, row, z);
                var tile = world.Create(
                    new GraphicalTile { TileId = character - '0' },
                    new Position { WorldCoordinate = coordinates }
                );

                column++;
            }
            row++;
        }
    }
}
