using System;
using System.Collections.Generic;
using Arch.Core;
using Microsoft.Xna.Framework;

namespace epoch.ECS;

/// <summary>
/// 2D column-based spatial hash for fast entity lookups by grid coordinate.
/// Each column covers all Z levels [0, maxZ) for a given (cx, cy).
/// Backs collision checks, adjacency queries, and rendering culling.
/// Empty slots are represented by <see cref="Entity.Null"/>.
/// </summary>
public class MapRegistry
{
    private World _world;
    private int _chunkSize;
    private int _maxZ;
    private int _columnVolume;

    private Dictionary<(int, int), Column> _columns = new();

    private class Column
    {
        public Entity[] Entities;
        public byte[] Collision;

        public Column(int volume)
        {
            Entities = new Entity[volume];
            Array.Fill(Entities, Entity.Null);
            Collision = new byte[volume];
        }
    }

    /// <summary>
    /// Creates a new registry.
    /// </summary>
    /// <param name="world">The ECS world used to query entity components.</param>
    /// <param name="chunkSize">The side length of each column in X and Y (e.g. 16 for 16x16).</param>
    /// <param name="maxZ">The maximum Z level [0, maxZ).</param>
    public MapRegistry(World world, int chunkSize, int maxZ)
    {
        _world = world;
        _chunkSize = chunkSize;
        _maxZ = maxZ;
        _columnVolume = chunkSize * chunkSize * maxZ;
    }

    private (int, int) ColumnKey(int x, int y) =>
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
    /// Auto-creates the column if it doesn't exist yet.
    /// </summary>
    public void Register(Vector3 coord, Entity entity)
    {
        int x = (int)coord.X, y = (int)coord.Y, z = (int)coord.Z;
        var key = ColumnKey(x, y);

        if (!_columns.TryGetValue(key, out var column))
        {
            column = new Column(_columnVolume);
            _columns[key] = column;
        }

        int idx = LocalIndex(x, y, z);
        column.Entities[idx] = entity;

        ref var position = ref _world.Get<Position>(entity);
        column.Collision[idx] = (byte)(position.Passable ? 0 : 1);
    }

    /// <summary>
    /// Removes the entity mapping at <paramref name="coord"/>. Does not destroy the entity.
    /// </summary>
    public void Unregister(Vector3 coord)
    {
        int x = (int)coord.X, y = (int)coord.Y, z = (int)coord.Z;
        var key = ColumnKey(x, y);

        if (_columns.TryGetValue(key, out var column))
        {
            int idx = LocalIndex(x, y, z);
            column.Entities[idx] = Entity.Null;
            column.Collision[idx] = 0;
        }
    }

    /// <summary>
    /// Returns the entity at <paramref name="coord"/>, or <see cref="Entity.Null"/> if absent.
    /// </summary>
    public Entity GetEntityAt(Vector3 coord)
    {
        int x = (int)coord.X, y = (int)coord.Y, z = (int)coord.Z;
        var key = ColumnKey(x, y);

        if (_columns.TryGetValue(key, out var column))
            return column.Entities[LocalIndex(x, y, z)];

        return Entity.Null;
    }

    /// <summary>
    /// Returns <c>true</c> if the tile at <paramref name="coord"/> is passable.
    /// If the column doesn't exist or Z is out of range, returns <c>true</c> —
    /// empty/unloaded space is passable.
    /// </summary>
    public bool IsPassableAt(Vector3 coord)
    {
        int x = (int)coord.X, y = (int)coord.Y, z = (int)coord.Z;

        if (z < 0 || z >= _maxZ)
            return true;

        var key = ColumnKey(x, y);

        if (_columns.TryGetValue(key, out var column))
            return column.Collision[LocalIndex(x, y, z)] == 0;

        return true;
    }

    /// <summary>
    /// Ensures a column exists for the given column coordinates, even if empty.
    /// </summary>
    public void EnsureColumn(int cx, int cy)
    {
        var key = (cx, cy);
        if (!_columns.ContainsKey(key))
            _columns[key] = new Column(_columnVolume);
    }

    /// <summary>
    /// Removes an entire column and destroys all entities in it.
    /// </summary>
    public void RemoveColumn(int cx, int cy)
    {
        var key = (cx, cy);

        if (_columns.TryGetValue(key, out var column))
        {
            for (int i = 0; i < _columnVolume; i++)
            {
                if (column.Entities[i] != Entity.Null)
                    _world.Destroy(column.Entities[i]);
            }
            _columns.Remove(key);
        }
    }
}
