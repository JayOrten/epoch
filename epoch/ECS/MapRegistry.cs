using System;
using Arch.Core;
using Microsoft.Xna.Framework;

namespace epoch.ECS;

/// <summary>
/// 3D spatial hash for fast entity lookups by grid coordinate.
/// Backs collision checks, adjacency queries, and rendering culling.
/// Flat array indexed as <c>x + z*xSize + y*xSize*zSize</c>.
/// </summary>
/// <remarks>
/// TODO: chunk loading/unloading for larger maps.
/// </remarks>
public class MapRegistry
{
    private World _world;

    private int _xSize;
    private int _ySize;
    private int _zSize;

    private Entity[] _entityMap;
    private byte[] _collisionMap; // 0 = passable, 1 = blocked

    /// <summary>Returns true if the coordinate is within grid bounds.</summary>
    public bool IsInBounds(Vector3 coord) =>
        coord.X >= 0
        && coord.Y >= 0
        && coord.Z >= 0
        && coord.X < _xSize
        && coord.Y < _ySize
        && coord.Z < _zSize;

    private int GetIndex(int x, int y, int z) => x + (z * _xSize) + (y * _xSize * _zSize);

    private int GetIndex(Vector3 coord) =>
        (int)(coord.X + (coord.Z * _xSize) + (coord.Y * _xSize * _zSize));

    /// <summary>
    /// Creates a new registry pre-filled with air entities.
    /// </summary>
    /// <param name="world">The ECS world used to create placeholder air entities.</param>
    /// <param name="xSize">Grid width.</param>
    /// <param name="ySize">Grid depth (north/south).</param>
    /// <param name="zSize">Number of vertical layers.</param>
    public MapRegistry(World world, int xSize, int ySize, int zSize)
    {
        _world = world;
        _xSize = xSize;
        _ySize = ySize;
        _zSize = zSize;

        int size = xSize * ySize * zSize;

        _entityMap = new Entity[size];
        // Create an entity with a AirTag component to represent empty space.
        Entity airEntity = world.Create(new AirTag());
        // Fill the entity map with this entity initially.
        Array.Fill(_entityMap, airEntity);

        _collisionMap = new byte[size];
    }

    /// <summary>
    /// Places an entity at the given grid coordinate, updating both the entity map
    /// and the collision map (derived from the entity's <see cref="Position.Passable"/> flag).
    /// </summary>
    public void Register(Vector3 coord, Entity entity)
    {
        if (!IsInBounds(coord))
            return;

        int idx = GetIndex(coord);
        _entityMap[idx] = entity;

        // Get passability from entity's Position component
        ref var position = ref _world.Get<Position>(entity);
        _collisionMap[idx] = (byte)(position.Passable ? 0 : 1);
    }

    /// <summary>
    /// Returns the entity at <paramref name="coord"/>, or <see cref="Entity.Null"/> if out of bounds.
    /// </summary>
    public Entity GetEntityAt(Vector3 coord)
    {
        if (!IsInBounds(coord))
            return Entity.Null;

        return _entityMap[GetIndex(coord)];
    }

    /// <summary>
    /// Returns <c>true</c> if the tile at <paramref name="coord"/> is passable (or out of bounds → false).
    /// </summary>
    public bool IsPassableAt(Vector3 coord)
    {
        if (!IsInBounds(coord))
            return false;

        return _collisionMap[GetIndex(coord)] == 0;
    }

    /// <summary>Returns the total number of vertical layers in the grid.</summary>
    public int GetNumZLevels()
    {
        return _zSize;
    }
}
