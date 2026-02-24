using System;
using System.Collections.Generic;
using Arch.Core;
using Arch.Core.Extensions;
using epoch.Utilities;
using Microsoft.Xna.Framework;

namespace epoch.ECS;

public sealed class ProceduralGenerationSystem : SystemBase<GameTime>
{
    private int _chunkSize;
    private int _chunkDistance;

    private HashSet<(int, int)> _loadedChunks = new();

    private FastNoiseLite _noise = new FastNoiseLite();

    public ProceduralGenerationSystem(World world, int chunkSize, int chunkDistance)
        : base(world)
    {
        _chunkSize = chunkSize;
        _chunkDistance = chunkDistance;

        _noise.SetNoiseType(FastNoiseLite.NoiseType.Cellular);
        _noise.SetFrequency(0.02f);
        _noise.SetFractalType(FastNoiseLite.FractalType.PingPong);
        _noise.SetFractalOctaves(3);
        _noise.SetFractalLacunarity(1.0f);
        _noise.SetFractalGain(0.35f);
        _noise.SetFractalWeightedStrength(1.3f);
        _noise.SetFractalPingPongStrength(3.0f);
        _noise.SetDomainWarpType(FastNoiseLite.DomainWarpType.OpenSimplex2Reduced);
        _noise.SetDomainWarpAmp(80.0f);
    }

    public override void Update(in GameTime gameTime)
    {
        ref var pos = ref GlobalContext.PlayerEntity.Get<Position>();

        int chunkX = (int)MathF.Floor(pos.WorldCoordinate.X / _chunkSize);
        int chunkY = (int)MathF.Floor(pos.WorldCoordinate.Y / _chunkSize);

        HashSet<(int, int)> chunksToLoad = new();
        for (int x = chunkX - _chunkDistance; x <= chunkX + _chunkDistance; x++)
        {
            for (int y = chunkY - _chunkDistance; y <= chunkY + _chunkDistance; y++)
            {
                chunksToLoad.Add((x, y));
            }
        }

        HashSet<(int, int)> chunksToUnload = new(_loadedChunks);
        chunksToUnload.ExceptWith(chunksToLoad);
        chunksToLoad.ExceptWith(_loadedChunks);

        foreach (var chunk in chunksToUnload)
        {
            UnloadChunk(chunk);
        }

        foreach (var chunk in chunksToLoad)
        {
            LoadChunk(chunk);
        }
    }

    private void UnloadChunk((int, int) chunkCoord)
    {
        var (cx, cy) = chunkCoord;
        GlobalContext.MapRegistry.RemoveColumn(cx, cy);
        _loadedChunks.Remove(chunkCoord);
    }

    private void LoadChunk((int, int) chunkCoord)
    {
        var (cx, cy) = chunkCoord;
        GlobalContext.MapRegistry.EnsureColumn(cx, cy);

        for (int x = 0; x < _chunkSize; x++)
        {
            for (int y = 0; y < _chunkSize; y++)
            {
                int bx = cx * _chunkSize + x;
                int by = cy * _chunkSize + y;

                int height = GetHeight(bx, by);
                height = Math.Min(height, GlobalContext.MaxZ - 1);

                // stone
                for (int z = 0; z < height - 5; z++)
                {
                    int entityId = 17;

                    string coordinates = $"{bx},{by},{z}";

                    EntityDefinition spawnPosition = new EntityDefinition(
                        new ComponentDefinition(
                            "Position",
                            new Dictionary<string, string> { { "WorldCoordinate", coordinates } }
                        )
                    );

                    // Spawn registers the entity in MapRegistry via EntityManager.Spawn
                    GlobalContext.EntityManager.Spawn(entityId, spawnPosition);
                }
                // dirt
                for (int z = height - 5; z < height; z++)
                {
                    int entityId = 16;

                    string coordinates = $"{bx},{by},{z}";

                    EntityDefinition spawnPosition = new EntityDefinition(
                        new ComponentDefinition(
                            "Position",
                            new Dictionary<string, string> { { "WorldCoordinate", coordinates } }
                        )
                    );

                    // Spawn registers the entity in MapRegistry via EntityManager.Spawn
                    GlobalContext.EntityManager.Spawn(entityId, spawnPosition);
                }
                // grass
                int grassChance = new Random().Next(0, 101);
                if (grassChance <= 20)
                {
                    int grassType = new Random().Next(0, 2);
                    int entityId = 10;
                    if (grassType == 0)
                    {
                        entityId = 11;
                    }

                    string coordinates = $"{bx},{by},{height}";

                    EntityDefinition spawnPosition = new EntityDefinition(
                        new ComponentDefinition(
                            "Position",
                            new Dictionary<string, string> { { "WorldCoordinate", coordinates } }
                        )
                    );

                    // Spawn registers the entity in MapRegistry via EntityManager.Spawn
                    GlobalContext.EntityManager.Spawn(entityId, spawnPosition);
                }
            }
        }
        _loadedChunks.Add(chunkCoord);
    }

    private int GetHeight(int x, int y)
    {
        // float x_height = (MathF.Sin(.1f * x) + 5) * 2;
        // float y_height = (MathF.Cos(.1f * y) + 3) * 4;
        float noise_height = _noise.GetNoise(x, y) * 5;
        // return (int)MathF.Ceiling(x_height + y_height + noise_height);
        return (int)MathF.Ceiling(noise_height + 5); // flat terrain with noise variation
        // return (int)MathF.Ceiling(x_height + y_height);
    }
}
