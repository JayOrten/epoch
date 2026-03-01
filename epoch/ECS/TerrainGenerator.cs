using System;
using Arch.Core;
using Arch.Core.Extensions;
using epoch.Utilities;
using Microsoft.Xna.Framework;

namespace epoch.ECS;

/// <summary>
/// Generates terrain columns (stone, dirt, grass) using noise-based height maps.
/// Knows what terrain looks like, nothing about scheduling or chunk lifecycle.
/// </summary>
public sealed class TerrainGenerator : IChunkGenerator
{
    private int _chunkSize;
    private FastNoiseLite _noise = new FastNoiseLite();

    public TerrainGenerator(int chunkSize)
    {
        _chunkSize = chunkSize;

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

    /// <inheritdoc />
    public int LoadChunkPartial(int cx, int cy, int startTile, int budget, out int nextTile)
    {
        int tilesPerChunk = _chunkSize * _chunkSize;

        for (nextTile = startTile; nextTile < tilesPerChunk && budget > 0; nextTile++)
        {
            int x = nextTile % _chunkSize;
            int y = nextTile / _chunkSize;
            int bx = cx * _chunkSize + x;
            int by = cy * _chunkSize + y;

            int height = GetHeight(bx, by);
            height = Math.Min(height, GlobalContext.MaxZ - 1);

            // Once we start a tile column, finish it — budget is checked
            // between tiles only. Overshoot bounded by max height (~9).
            int dirtStart = Math.Max(0, height - 5);
            // stone
            for (int z = 0; z < dirtStart; z++)
            {
                GlobalContext.EntityManager.SpawnTerrain(17, new Vector3(bx, by, z));
                DirtyNeighbors(new Vector3(bx, by, z));
                budget--;
            }
            // dirt
            for (int z = dirtStart; z < height; z++)
            {
                GlobalContext.EntityManager.SpawnTerrain(16, new Vector3(bx, by, z));
                DirtyNeighbors(new Vector3(bx, by, z));
                budget--;
            }
            // grass
            // TODO: remove this hashcoord random stuff
            int grassChance = HashCoord(bx, by, 0) % 101;
            if (grassChance <= 20)
            {
                int grassType = HashCoord(bx, by, 1) % 2;
                int entityId = grassType == 0 ? 11 : 10;
                GlobalContext.EntityManager.SpawnTerrain(entityId, new Vector3(bx, by, height));
                DirtyNeighbors(new Vector3(bx, by, height));
                budget--;
            }
        }

        return budget;
    }

    /// <summary>
    /// Marks existing neighbors in 6 cardinal directions as dirty so they recompute adjacency.
    /// Only matters at the loading frontier — interior tiles' neighbors are in the same batch.
    /// </summary>
    private void DirtyNeighbors(Vector3 pos)
    {
        ReadOnlySpan<Vector3> offsets =
        [
            new Vector3(1, 0, 0),
            new Vector3(-1, 0, 0),
            new Vector3(0, 1, 0),
            new Vector3(0, -1, 0),
            new Vector3(0, 0, 1),
            new Vector3(0, 0, -1),
        ];

        var registry = GlobalContext.ChunkRegistry;
        foreach (var offset in offsets)
        {
            var neighbor = registry.GetEntityAt(pos + offset);
            if (neighbor != Entity.Null && neighbor.IsAlive() && !neighbor.Has<DirtyTag>())
                neighbor.Add<DirtyTag>();
        }
    }

    private static int HashCoord(int x, int y, int salt)
    {
        int hash = unchecked(x * 73856093 ^ y * 19349669 ^ salt);
        return hash & 0x7FFFFFFF;
    }

    private int GetHeight(int x, int y)
    {
        float noise_height = _noise.GetNoise(x, y) * 5;
        return (int)MathF.Ceiling(noise_height + 5);
    }
}
