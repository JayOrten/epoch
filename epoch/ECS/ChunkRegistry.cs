using System;
using System.Collections.Generic;
using Arch.Core;
using Microsoft.Xna.Framework;

namespace epoch.ECS;

/// <summary>
/// Callback interface for generating terrain into a chunk.
/// </summary>
public interface IChunkGenerator
{
    /// <summary>
    /// Fills a chunk starting from tile startTile, spending up to budget spawns.
    /// Returns remaining budget. Sets nextTile to resume point.
    /// </summary>
    int LoadChunkPartial(int cx, int cy, int startTile, int budget, out int nextTile);
}

/// <summary>
/// 2D chunk-based spatial hash for fast entity lookups by grid coordinate.
/// Each chunk covers all Z levels [0, maxZ) for a given (cx, cy).
/// Backs collision checks, adjacency queries, and rendering culling.
/// Owns chunk lifecycle: load/unload scheduling with budgeted processing.
/// Empty slots are represented by <see cref="Entity.Null"/>.
/// </summary>
public class ChunkRegistry
{
    private World _world;
    private int _chunkSize;
    private int _maxZ;
    private int _chunkVolume;
    private int _chunkDistance;
    private IChunkGenerator _generator;

    private Dictionary<(int, int), Chunk> _chunks = new();

    // --- Chunk lifecycle state ---
    private HashSet<(int, int)> _loadedChunks = new();
    private HashSet<(int, int)> _pendingLoadChunks = new();
    private HashSet<(int, int)> _pendingUnloadChunks = new();
    private readonly HashSet<(int, int)> _desiredChunks = new();
    private readonly HashSet<(int, int)> _chunksToLoad = new();
    private readonly HashSet<(int, int)> _chunksToUnload = new();

    private Queue<(int, int)> _loadQueue = new();
    private Queue<(int, int)> _unloadQueue = new();
    private ChunkLoadState? _activeLoad;
    private ChunkUnloadState? _activeUnload;

    private const int LoadBudget = 256;
    private const int UnloadBudget = 512;

    private struct ChunkLoadState
    {
        public int Cx, Cy;
        public int TileIndex;
    }

    private struct ChunkUnloadState
    {
        public int Cx, Cy;
        public int StartIndex;
    }

    private class Chunk
    {
        public Entity[] Entities;
        public byte[] Collision;

        public Chunk(int volume)
        {
            Entities = new Entity[volume];
            Array.Fill(Entities, Entity.Null);
            Collision = new byte[volume];
        }
    }

    /// <summary>The side length of each chunk in X and Y.</summary>
    public int ChunkSize => _chunkSize;

    /// <summary>
    /// Creates a new registry with chunk lifecycle management.
    /// </summary>
    /// <param name="world">The ECS world used to query entity components.</param>
    /// <param name="chunkSize">The side length of each chunk in X and Y (e.g. 16 for 16x16).</param>
    /// <param name="maxZ">The maximum Z level [0, maxZ).</param>
    /// <param name="chunkDistance">How many chunks around the player to keep loaded.</param>
    /// <param name="generator">Terrain generator callback for filling new chunks.</param>
    public ChunkRegistry(World world, int chunkSize, int maxZ, int chunkDistance, IChunkGenerator generator)
    {
        _world = world;
        _chunkSize = chunkSize;
        _maxZ = maxZ;
        _chunkVolume = chunkSize * chunkSize * maxZ;
        _chunkDistance = chunkDistance;
        _generator = generator;
    }

    /// <summary>
    /// Creates a registry without lifecycle management (for tests and static maps).
    /// </summary>
    public ChunkRegistry(World world, int chunkSize, int maxZ)
    {
        _world = world;
        _chunkSize = chunkSize;
        _maxZ = maxZ;
        _chunkVolume = chunkSize * chunkSize * maxZ;
    }

    private (int, int) ChunkKey(int x, int y) =>
        (
            (int)MathF.Floor((float)x / _chunkSize),
            (int)MathF.Floor((float)y / _chunkSize)
        );

    private int LocalIndex(int x, int y, int z)
    {
        int lx = ((x % _chunkSize) + _chunkSize) % _chunkSize;
        int ly = ((y % _chunkSize) + _chunkSize) % _chunkSize;
        int clampedZ = Math.Clamp(z, 0, _maxZ - 1);
        return lx + ly * _chunkSize + clampedZ * _chunkSize * _chunkSize;
    }

    /// <summary>
    /// Places an entity at the given grid coordinate, updating both the entity map
    /// and the collision map (derived from the entity's <see cref="Position.Passable"/> flag).
    /// Auto-creates the chunk if it doesn't exist yet.
    /// </summary>
    public void Register(Vector3 coord, Entity entity)
    {
        int x = (int)coord.X, y = (int)coord.Y, z = (int)coord.Z;
        var key = ChunkKey(x, y);

        if (!_chunks.TryGetValue(key, out var chunk))
        {
            chunk = new Chunk(_chunkVolume);
            _chunks[key] = chunk;
        }

        int idx = LocalIndex(x, y, z);
        chunk.Entities[idx] = entity;

        ref var position = ref _world.Get<Position>(entity);
        chunk.Collision[idx] = (byte)(position.Passable ? 0 : 1);
    }

    /// <summary>
    /// Removes the entity mapping at <paramref name="coord"/>. Does not destroy the entity.
    /// </summary>
    public void Unregister(Vector3 coord)
    {
        int x = (int)coord.X, y = (int)coord.Y, z = (int)coord.Z;
        var key = ChunkKey(x, y);

        if (_chunks.TryGetValue(key, out var chunk))
        {
            int idx = LocalIndex(x, y, z);
            chunk.Entities[idx] = Entity.Null;
            chunk.Collision[idx] = 0;
        }
    }

    /// <summary>
    /// Returns the entity at <paramref name="coord"/>, or <see cref="Entity.Null"/> if absent.
    /// </summary>
    public Entity GetEntityAt(Vector3 coord)
    {
        int x = (int)coord.X, y = (int)coord.Y, z = (int)coord.Z;
        var key = ChunkKey(x, y);

        if (_chunks.TryGetValue(key, out var chunk))
            return chunk.Entities[LocalIndex(x, y, z)];

        return Entity.Null;
    }

    /// <summary>
    /// Returns <c>true</c> if the tile at <paramref name="coord"/> is passable.
    /// If the chunk doesn't exist or Z is out of range, returns <c>true</c> —
    /// empty/unloaded space is passable.
    /// </summary>
    public bool IsPassableAt(Vector3 coord)
    {
        int x = (int)coord.X, y = (int)coord.Y, z = (int)coord.Z;

        if (z < 0 || z >= _maxZ)
            return true;

        var key = ChunkKey(x, y);

        if (_chunks.TryGetValue(key, out var chunk))
            return chunk.Collision[LocalIndex(x, y, z)] == 0;

        return true;
    }

    /// <summary>
    /// Ensures a chunk exists for the given chunk coordinates, even if empty.
    /// </summary>
    public void EnsureChunk(int cx, int cy)
    {
        var key = (cx, cy);
        if (!_chunks.ContainsKey(key))
            _chunks[key] = new Chunk(_chunkVolume);
    }

    /// <summary>
    /// Removes an entire chunk and destroys all entities in it.
    /// </summary>
    public void RemoveChunk(int cx, int cy)
    {
        var key = (cx, cy);

        if (_chunks.TryGetValue(key, out var chunk))
        {
            for (int i = 0; i < _chunkVolume; i++)
            {
                if (chunk.Entities[i] != Entity.Null)
                    _world.Destroy(chunk.Entities[i]);
            }
            _chunks.Remove(key);
        }
    }

    /// <summary>
    /// Destroys up to <paramref name="budget"/> entities in the chunk starting from
    /// <paramref name="startIndex"/>. Returns the number of entities destroyed.
    /// When <paramref name="startIndex"/> reaches the chunk volume, the chunk
    /// is fully drained and can be removed.
    /// </summary>
    public int RemoveChunkPartial(int cx, int cy, ref int startIndex, int budget)
    {
        var key = (cx, cy);

        if (!_chunks.TryGetValue(key, out var chunk))
            return 0;

        int destroyed = 0;
        while (startIndex < _chunkVolume && destroyed < budget)
        {
            if (chunk.Entities[startIndex] != Entity.Null)
            {
                _world.Destroy(chunk.Entities[startIndex]);
                chunk.Entities[startIndex] = Entity.Null;
                chunk.Collision[startIndex] = 0;
                destroyed++;
            }
            startIndex++;
        }

        // Chunk fully drained — remove it
        if (startIndex >= _chunkVolume)
            _chunks.Remove(key);

        return destroyed;
    }

    // --- Chunk lifecycle ---

    /// <summary>
    /// Drives chunk load/unload scheduling based on the player's world position.
    /// Call once per frame from GenerationSystem.
    /// </summary>
    public void Update(float playerWorldX, float playerWorldY)
    {
        int chunkX = (int)MathF.Floor(playerWorldX / _chunkSize);
        int chunkY = (int)MathF.Floor(playerWorldY / _chunkSize);

        // Compute desired set of chunks to load in
        _desiredChunks.Clear();
        for (int x = chunkX - _chunkDistance; x <= chunkX + _chunkDistance; x++)
        {
            for (int y = chunkY - _chunkDistance; y <= chunkY + _chunkDistance; y++)
            {
                _desiredChunks.Add((x, y));
            }
        }

        // Determine what to enqueue for loading
        _chunksToLoad.Clear();
        foreach (var chunk in _desiredChunks)
        {
            if (!_loadedChunks.Contains(chunk) && !_pendingLoadChunks.Contains(chunk))
                _chunksToLoad.Add(chunk);
        }

        // Determine what to enqueue for unloading
        _chunksToUnload.Clear();
        foreach (var chunk in _loadedChunks)
        {
            if (!_desiredChunks.Contains(chunk) && !_pendingUnloadChunks.Contains(chunk))
                _chunksToUnload.Add(chunk);
        }

        foreach (var chunk in _chunksToUnload)
        {
            _unloadQueue.Enqueue(chunk);
            _pendingUnloadChunks.Add(chunk);
        }

        foreach (var chunk in _chunksToLoad)
        {
            _loadQueue.Enqueue(chunk);
            _pendingLoadChunks.Add(chunk);
        }

        PruneLoadQueue();
        ProcessUnloads();
        ProcessLoads();
    }

    private void PruneLoadQueue()
    {
        int count = _loadQueue.Count;
        for (int i = 0; i < count; i++)
        {
            var chunk = _loadQueue.Dequeue();
            if (_desiredChunks.Contains(chunk))
            {
                _loadQueue.Enqueue(chunk);
            }
            else
            {
                _pendingLoadChunks.Remove(chunk);
            }
        }
    }

    private void ProcessUnloads()
    {
        int budget = UnloadBudget;

        if (_activeUnload.HasValue)
        {
            var state = _activeUnload.Value;
            int startIdx = state.StartIndex;
            int destroyed = RemoveChunkPartial(
                state.Cx,
                state.Cy,
                ref startIdx,
                budget
            );
            budget -= destroyed;

            if (startIdx >= _chunkVolume)
            {
                _loadedChunks.Remove((state.Cx, state.Cy));
                _pendingUnloadChunks.Remove((state.Cx, state.Cy));
                _activeUnload = null;
            }
            else
            {
                _activeUnload = new ChunkUnloadState
                {
                    Cx = state.Cx,
                    Cy = state.Cy,
                    StartIndex = startIdx,
                };
            }
        }

        while (budget > 0 && !_activeUnload.HasValue && _unloadQueue.Count > 0)
        {
            var (cx, cy) = _unloadQueue.Dequeue();

            if (_desiredChunks.Contains((cx, cy)))
            {
                _pendingUnloadChunks.Remove((cx, cy));
                continue;
            }

            int startIdx = 0;
            int destroyed = RemoveChunkPartial(
                cx,
                cy,
                ref startIdx,
                budget
            );
            budget -= destroyed;

            if (startIdx >= _chunkVolume)
            {
                _loadedChunks.Remove((cx, cy));
                _pendingUnloadChunks.Remove((cx, cy));
            }
            else
            {
                _activeUnload = new ChunkUnloadState
                {
                    Cx = cx,
                    Cy = cy,
                    StartIndex = startIdx,
                };
            }
        }
    }

    private void ProcessLoads()
    {
        int budget = LoadBudget;

        if (_activeLoad.HasValue)
        {
            var state = _activeLoad.Value;
            budget = _generator.LoadChunkPartial(
                state.Cx,
                state.Cy,
                state.TileIndex,
                budget,
                out int nextTile
            );

            if (nextTile >= _chunkSize * _chunkSize)
            {
                _activeLoad = null;
            }
            else
            {
                _activeLoad = new ChunkLoadState
                {
                    Cx = state.Cx,
                    Cy = state.Cy,
                    TileIndex = nextTile,
                };
            }
        }

        while (budget > 0 && !_activeLoad.HasValue && _loadQueue.Count > 0)
        {
            var (cx, cy) = _loadQueue.Dequeue();

            if (_loadedChunks.Contains((cx, cy)))
            {
                _pendingLoadChunks.Remove((cx, cy));
                continue;
            }

            EnsureChunk(cx, cy);
            _loadedChunks.Add((cx, cy));
            _pendingLoadChunks.Remove((cx, cy));

            budget = _generator.LoadChunkPartial(cx, cy, 0, budget, out int nextTile);

            if (nextTile < _chunkSize * _chunkSize)
            {
                _activeLoad = new ChunkLoadState
                {
                    Cx = cx,
                    Cy = cy,
                    TileIndex = nextTile,
                };
            }
        }
    }
}
