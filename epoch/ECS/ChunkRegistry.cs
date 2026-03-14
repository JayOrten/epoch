using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Arch.Core;
using Microsoft.Xna.Framework;

namespace epoch.ECS;

/// <summary>
/// Pre-filtered, contiguous draw data for a single visible tile.
/// Populated by TileAdjacencySystem, consumed by DrawSystem.
/// Eliminates per-entity ECS lookups during rendering.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
internal struct DrawCacheEntry
{
    public Vector2 BasePosition;
    public float RawZ;           // entity Z + tile offset (shader computes perspectiveDepth)
    public float SortDepth;      // entity Z + tile offset + position.Top
    public float BaseScale;
    public float Rotation;
    public float BorderMask;
    public float BorderWidth;
    public float EntityZ;        // entity Z (shader computes layerDifference)
    public Rectangle SourceRect;
    public Color Bg1, Bg2, Base, Accent, Border;
}

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
        public List<Entity> Packed;

        // Dense array of draw-ready tile data (only visible tiles)
        public DrawCacheEntry[] DrawCache;
        public int DrawCacheCount;

        // LocalIndex → start offset in DrawCache (-1 = not in cache)
        public short[] DrawCacheStart;
        // LocalIndex → tile count in DrawCache for this entity
        public byte[] DrawCacheTileCount;
        // DrawCache index → LocalIndex (reverse map for swap-remove)
        public short[] DrawCacheOwner;

        public Chunk(int volume)
        {
            Entities = new Entity[volume];
            Array.Fill(Entities, Entity.Null);
            Collision = new byte[volume];
            Packed = new List<Entity>();

            // Draw cache: assume ~2 visible tiles per entity on average
            int cacheCapacity = volume * 2;
            DrawCache = new DrawCacheEntry[cacheCapacity];
            DrawCacheCount = 0;
            DrawCacheStart = new short[volume];
            Array.Fill(DrawCacheStart, (short)-1);
            DrawCacheTileCount = new byte[volume];
            DrawCacheOwner = new short[cacheCapacity];
        }
    }

    /// <summary>The side length of each chunk in X and Y.</summary>
    public int ChunkSize => _chunkSize;

    /// <summary>Returns the packed entity list for a chunk, or empty if the chunk doesn't exist.</summary>
    public ReadOnlySpan<Entity> GetPackedEntities(int cx, int cy)
    {
        if (_chunks.TryGetValue((cx, cy), out var chunk))
            return CollectionsMarshal.AsSpan(chunk.Packed);
        return ReadOnlySpan<Entity>.Empty;
    }

    /// <summary>The set of currently loaded chunk coordinates.</summary>
    public IReadOnlyCollection<(int, int)> LoadedChunks => _loadedChunks;

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
        chunk.DrawCacheStart[idx] = -1;
        chunk.DrawCacheTileCount[idx] = 0;
        // Only drawable entities go in the packed list (skips air, collision-only, etc.)
        if (_world.Has<GraphicalTileList>(entity))
            chunk.Packed.Add(entity);

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
            RemoveFromDrawCache(chunk, idx);
            var entity = chunk.Entities[idx];
            chunk.Entities[idx] = Entity.Null;
            chunk.Collision[idx] = 0;
            if (entity != Entity.Null)
                chunk.Packed.Remove(entity);
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

    // --- Draw Cache API ---

    /// <summary>
    /// Returns the draw cache for a chunk as a contiguous span.
    /// </summary>
    internal ReadOnlySpan<DrawCacheEntry> GetDrawCache(int cx, int cy, out int count)
    {
        if (_chunks.TryGetValue((cx, cy), out var chunk))
        {
            count = chunk.DrawCacheCount;
            return chunk.DrawCache.AsSpan(0, chunk.DrawCacheCount);
        }
        count = 0;
        return ReadOnlySpan<DrawCacheEntry>.Empty;
    }

    /// <summary>
    /// Writes draw cache entries for an entity. Handles add, update-in-place, and remove
    /// based on the number of visible tiles.
    /// </summary>
    internal void UpdateDrawCache(Vector3 coord, ReadOnlySpan<DrawCacheEntry> entries)
    {
        int x = (int)coord.X, y = (int)coord.Y, z = (int)coord.Z;
        var key = ChunkKey(x, y);
        if (!_chunks.TryGetValue(key, out var chunk))
            return;

        int localIdx = LocalIndex(x, y, z);

        if (entries.Length == 0)
        {
            RemoveFromDrawCache(chunk, localIdx);
            return;
        }

        int oldCount = chunk.DrawCacheTileCount[localIdx];
        int oldStart = chunk.DrawCacheStart[localIdx];

        if (oldStart >= 0 && oldCount == entries.Length)
        {
            // Same size — overwrite in place
            entries.CopyTo(chunk.DrawCache.AsSpan(oldStart, oldCount));
        }
        else
        {
            // Size changed or not cached yet — remove old, append new
            if (oldStart >= 0)
                RemoveFromDrawCache(chunk, localIdx);

            AddToDrawCache(chunk, localIdx, entries);
        }
    }

    private void AddToDrawCache(Chunk chunk, int localIdx, ReadOnlySpan<DrawCacheEntry> entries)
    {
        int start = chunk.DrawCacheCount;
        int needed = start + entries.Length;

        // Grow arrays if needed
        if (needed > chunk.DrawCache.Length)
        {
            int newCap = Math.Max(chunk.DrawCache.Length * 2, needed);
            Array.Resize(ref chunk.DrawCache, newCap);
            Array.Resize(ref chunk.DrawCacheOwner, newCap);
        }

        entries.CopyTo(chunk.DrawCache.AsSpan(start, entries.Length));
        chunk.DrawCacheStart[localIdx] = (short)start;
        chunk.DrawCacheTileCount[localIdx] = (byte)entries.Length;

        for (int i = 0; i < entries.Length; i++)
            chunk.DrawCacheOwner[start + i] = (short)localIdx;

        chunk.DrawCacheCount = needed;

        Debug.Assert(chunk.DrawCacheCount <= chunk.DrawCache.Length);
    }

    private void RemoveFromDrawCache(Chunk chunk, int localIdx)
    {
        int start = chunk.DrawCacheStart[localIdx];
        if (start < 0)
            return;

        int count = chunk.DrawCacheTileCount[localIdx];
        int blockEnd = start + count;
        int totalEnd = chunk.DrawCacheCount;
        int shiftCount = totalEnd - blockEnd;

        chunk.DrawCacheStart[localIdx] = -1;
        chunk.DrawCacheTileCount[localIdx] = 0;

        if (shiftCount > 0)
        {
            // Shift everything after the removed block down to fill the gap.
            // This preserves block contiguity (swap-remove would split blocks).
            Array.Copy(chunk.DrawCache, blockEnd, chunk.DrawCache, start, shiftCount);
            Array.Copy(chunk.DrawCacheOwner, blockEnd, chunk.DrawCacheOwner, start, shiftCount);

            // Fix start pointers: each owner updated exactly once, when we hit
            // the first entry of their block (old start == old position of this entry).
            for (int i = 0; i < shiftCount; i++)
            {
                short movedOwner = chunk.DrawCacheOwner[start + i];
                if (chunk.DrawCacheStart[movedOwner] == blockEnd + i)
                    chunk.DrawCacheStart[movedOwner] = (short)(start + i);
            }
        }

        chunk.DrawCacheCount = totalEnd - count;
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
