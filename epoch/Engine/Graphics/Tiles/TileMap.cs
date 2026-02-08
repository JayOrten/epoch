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
                    // Log.Info(
                    //     $"Spawning tile entity at {coordinates} with TileId {character - '0'}"
                    // );
                    // Vector3 coordinates = new Vector3(column, row, z);
                    string coordinates = $"{column},{row},{z}";

                    generateTileList(coordinates, character);
                    // if (character == '5')
                    // {
                    //     generateGrass(coordinates, character);
                    // }
                    // else
                    // {
                    //     generateEmpty(coordinates, character);
                    // }
                }
                column++;
            }
            row++;
        }
    }

    // public static void generateEmpty(string coordinates, char character)
    // {
    //     EntityDefinition spawnPosition = new EntityDefinition(
    //         new ComponentDefinition(
    //             "Position",
    //             new Dictionary<string, string> { { "WorldCoordinate", coordinates } }
    //         ),
    //         new ComponentDefinition( // TODO: need a way to set this stuff dynamically at draw time.
    //             "GraphicalTile",
    //             new Dictionary<string, string>
    //             {
    //                 { "Background2Color", "30,32,48,255" },
    //                 { "BorderColor", "108,112,134,255" },
    //             }
    //         )
    //     );

    //     GlobalContext.EntityManager.Spawn(character - '0', spawnPosition);
    // }

    public static void generateTileList(string coordinates, char character)
    {
        // Define each of the sub tiles that make up the grass tile
        // ComponentDefinition tile1 = new ComponentDefinition(
        //     "GraphicalTile",
        //     new Dictionary<string, string>
        //     {
        //         { "Background1Color", "30,32,48,255" },
        //         { "BorderColor", "108,112,134,255" },
        //     }
        // );

        ComponentDefinition graphicalTileList = new ComponentDefinition( // TODO: need a way to set this stuff dynamically at draw time.
            "GraphicalTileList"
        );

        // graphicalTileList.SubCompositeParts.Add(tile1);

        EntityDefinition spawnPosition = new EntityDefinition(
            new ComponentDefinition(
                "Position",
                new Dictionary<string, string> { { "WorldCoordinate", coordinates } }
            ),
            graphicalTileList
        );

        GlobalContext.EntityManager.Spawn(character - '0', spawnPosition);
    }

    // public static void generateGrass(string coordinates, char character)
    // {
    //     // Define each of the sub tiles that make up the grass tile
    //     ComponentDefinition tile1 = new ComponentDefinition(
    //         "GraphicalTile",
    //         new Dictionary<string, string>
    //         {
    //             { "Background1Color", "30,32,48,255" },
    //             { "BorderColor", "108,112,134,255" },
    //         }
    //     );

    //     ComponentDefinition graphicalTileList = new ComponentDefinition( // TODO: need a way to set this stuff dynamically at draw time.
    //         "GraphicalTileList"
    //     );

    //     graphicalTileList.SubCompositeParts.Add(tile1);

    //     EntityDefinition spawnPosition = new EntityDefinition(
    //         new ComponentDefinition(
    //             "Position",
    //             new Dictionary<string, string> { { "WorldCoordinate", coordinates } }
    //         ),
    //         graphicalTileList
    //     );

    //     GlobalContext.EntityManager.Spawn(character - '0', spawnPosition);
    // }
}
