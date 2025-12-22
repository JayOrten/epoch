using System.IO;
using Arch.Core;
using epoch.ECS;
using Microsoft.Xna.Framework;

namespace epoch.Engine.Graphics.Tiles;

public static class TileMap
{
    public static void LoadTileMap(string tileMapPath, World world, MapRegistry mapRegistry)
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
                if (character != '.')
                {
                    Vector3 coordinates = new Vector3(column, row, z);
                    var tile = world.Create(
                        new GraphicalTile
                        {
                            TileId = character - '0',
                            BackgroundColor = new Color(30, 32, 48, 255),
                            // BackgroundColor = Color.Black,
                            BorderColor = new Color(75, 75, 75, 150),
                            // SpriteColor = new Color(150, 150, 150, 255),
                        },
                        new Position { WorldCoordinate = new Vector2(column, row), zLevel = z }
                    );

                    mapRegistry.Register(coordinates, tile);
                }
                column++;
            }
            row++;
        }
    }
}
