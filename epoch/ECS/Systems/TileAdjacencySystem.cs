using System;
using System.Runtime.CompilerServices;
using Arch.Core;
using Arch.Core.Extensions;
using epoch.Graphics.Tiles;
using Microsoft.Xna.Framework;

namespace epoch.ECS;

/// <summary>
/// Recalculates spatial metadata for entities tagged with <see cref="DirtyTag"/>.
/// Computes the 26-bit <see cref="Position.SpaceMask"/> (3D neighborhood openness)
/// and derives per-tile border masks and autotile indices from it.
/// Removes the <see cref="DirtyTag"/> after processing.
/// </summary>
public sealed class TileAdjacencySystem : SystemBase<GameTime>
{
    private readonly QueryDescription _entitiesToUpdate2 = new QueryDescription().WithAll<
        Position,
        GraphicalTileList,
        DirtyTag
    >();

    // 26 directions for full 3D neighborhood (3x3x3 cube minus center)
    // Coordinate system: X = East(+)/West(-), Y = South(+)/North(-), Z = Above(+)/Below(-)
    // Bit indices correspond to array index for SpaceMask
    private static readonly Vector3[] _directions = new Vector3[]
    {
        // Faces (bits 0-5)
        new Vector3(0, -1, 0), // 0:  North
        new Vector3(1, 0, 0), // 1:  East
        new Vector3(0, 1, 0), // 2:  South
        new Vector3(-1, 0, 0), // 3:  West
        new Vector3(0, 0, 1), // 4:  Above
        new Vector3(0, 0, -1), // 5:  Below
        // Edges - horizontal (bits 6-9)
        new Vector3(1, -1, 0), // 6:  North-East
        new Vector3(1, 1, 0), // 7:  South-East
        new Vector3(-1, 1, 0), // 8:  South-West
        new Vector3(-1, -1, 0), // 9:  North-West
        // Edges - vertical north/south (bits 10-13)
        new Vector3(0, -1, 1), // 10: North-Above
        new Vector3(0, -1, -1), // 11: North-Below
        new Vector3(0, 1, 1), // 12: South-Above
        new Vector3(0, 1, -1), // 13: South-Below
        // Edges - vertical east/west (bits 14-17)
        new Vector3(1, 0, 1), // 14: East-Above
        new Vector3(1, 0, -1), // 15: East-Below
        new Vector3(-1, 0, 1), // 16: West-Above
        new Vector3(-1, 0, -1), // 17: West-Below
        // Corners - above (bits 18-21)
        new Vector3(1, -1, 1), // 18: North-East-Above
        new Vector3(1, 1, 1), // 19: South-East-Above
        new Vector3(-1, 1, 1), // 20: South-West-Above
        new Vector3(-1, -1, 1), // 21: North-West-Above
        // Corners - below (bits 22-25)
        new Vector3(1, -1, -1), // 22: North-East-Below
        new Vector3(1, 1, -1), // 23: South-East-Below
        new Vector3(-1, 1, -1), // 24: South-West-Below
        new Vector3(-1, -1, -1), // 25: North-West-Below
    };

    // Maps cardinal direction index (0-3: N, E, S, W) to the
    // above-adjacent bit index in the space mask
    private static readonly int[] _aboveAdjacentBits = { 10, 14, 12, 16 };

    public TileAdjacencySystem(World world)
        : base(world) { }

    /// <summary>
    /// Builds the 26-bit space mask for a position by probing each neighbor in the
    /// <see cref="ChunkRegistry"/>. A set bit means that direction is open (air or passable).
    /// </summary>
    public static int CalculateSpaceMask(Vector3 worldCoordinate, ChunkRegistry registry)
    {
        int mask = 0;
        for (int i = 0; i < _directions.Length; i++)
        {
            Vector3 adjacentCoord = worldCoordinate + _directions[i];
            var entityAtAdjacent = registry.GetEntityAt(adjacentCoord);
            if (
                entityAtAdjacent == Entity.Null
                || entityAtAdjacent.Get<Position>().Passable
            )
            {
                mask |= (1 << i);
            }
        }
        return mask;
    }

    /// <summary>
    /// Derives border masks from a space mask. Returns (middle, top, bottom) where each
    /// is a 4-bit cardinal mask (N=0, E=1, S=2, W=3).
    /// </summary>
    public static (int middle, int top, int bottom) CalculateBorderMasks(int spaceMask)
    {
        int middleMask = spaceMask & 0xF;
        bool aboveIsOpen = (spaceMask & (1 << 4)) != 0;
        bool belowIsOpen = (spaceMask & (1 << 5)) != 0;

        int bottomMask = belowIsOpen ? middleMask : 0;

        int topMask = 0;
        if (aboveIsOpen)
        {
            for (int i = 0; i < 4; i++)
            {
                bool adjacentOpen = (middleMask & (1 << i)) != 0;
                bool aboveAdjacentOpen = (spaceMask & (1 << _aboveAdjacentBits[i])) != 0;

                if (adjacentOpen || !aboveAdjacentOpen)
                    topMask |= (1 << i);
            }
        }

        return (middleMask, topMask, bottomMask);
    }

    public override void Update(in GameTime gameTime)
    {
        var commandBuffer = new Arch.Buffer.CommandBuffer();
        var query = World.Query(in _entitiesToUpdate2);

        var tileManager = GlobalContext.TileManager;
        var tileset = tileManager.Tileset;
        float tileWidth = tileset.TileWidth;
        float tileHeight = tileset.TileHeight;
        float globalScale = GlobalContext.GlobalScale;

        foreach (ref var chunk in query.GetChunkIterator())
        {
            var references = chunk.GetFirst<Position, GraphicalTileList, DirtyTag>();

            foreach (var entity in chunk)
            {
                ref var graphicalTileList = ref Unsafe.Add(ref references.t1, entity);
                ref var position = ref Unsafe.Add(ref references.t0, entity);

                position.SpaceMask = CalculateSpaceMask(
                    position.WorldCoordinate,
                    GlobalContext.ChunkRegistry
                );

                if (position.IsBlock)
                {
                    var (middleMask, topMask, bottomMask) = CalculateBorderMasks(
                        position.SpaceMask
                    );

                    int tileMask = graphicalTileList.ActiveTileMask;
                    while (tileMask != 0)
                    {
                        int i = System.Numerics.BitOperations.TrailingZeroCount(tileMask);
                        tileMask &= tileMask - 1;
                        ref var tile = ref graphicalTileList.Tiles[i];

                        if (tile.BorderType != BorderType.None)
                        {
                            tile.BorderMask = tile.BorderType switch
                            {
                                BorderType.Top => topMask,
                                BorderType.Bottom => bottomMask,
                                _ => 0,
                            };
                        }

                        if (tile.AutoTile)
                        {
                            if (middleMask == 0 && (position.SpaceMask & 0x3C0) != 0)
                                tile.AutoTileMask = 15; // diagonal-only: treat as fully open
                            else
                                tile.AutoTileMask = middleMask;
                        }
                    }
                }

                // Populate cached draw data for all active tiles
                PopulateTileCache(ref position, ref graphicalTileList, tileManager, tileset, tileWidth, tileHeight, globalScale);

                // Update the chunk draw cache with visible tiles.
                // Skip composite parts — they move every frame and use the uncached draw path.
                var archEntity = chunk.Entity(entity);
                if (!archEntity.Has<CompositePartComponent>())
                    PopulateDrawCache(ref position, ref graphicalTileList, GlobalContext.ChunkRegistry);

                commandBuffer.Remove<DirtyTag>(archEntity);
            }
        }

        commandBuffer.Playback(World, true);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void PopulateTileCache(
        ref Position position,
        ref GraphicalTileList graphicalTileList,
        TileManager tileManager,
        Tileset tileset,
        float tileWidth,
        float tileHeight,
        float globalScale)
    {
        int mask = graphicalTileList.ActiveTileMask;
        while (mask != 0)
        {
            int i = System.Numerics.BitOperations.TrailingZeroCount(mask);
            mask &= mask - 1;

            ref var tile = ref graphicalTileList.Tiles[i];

            Tile tileInfo = tileManager.GetTile(tile.TileId);
            if (tileInfo == null)
                continue;

            (Rectangle sourceRect, float rotation) = tileset.GetTileRect(
                tileInfo.TileIndex,
                tile.AutoTileMask
            );

            tile.CachedSourceRect = sourceRect;
            tile.CachedRotation = rotation;

            tile.CachedBasePosition = new Vector2(
                position.WorldCoordinate.X * tileWidth,
                position.WorldCoordinate.Y * tileHeight
            ) * tile.Scale * globalScale;

            tile.CachedBaseScale = tile.Scale * globalScale;

            tile.CachedSortDepth = position.WorldCoordinate.Z + tile.Offset + position.Top;

            // Resolve colors: override if set, otherwise tile definition default
            int colorMask = tile.ColorOverrideMask;
            if (colorMask == 0)
            {
                tile.CachedBg1 = tileInfo.Background1Color;
                tile.CachedBg2 = tileInfo.Background2Color;
                tile.CachedBase = tileInfo.BaseColor;
                tile.CachedAccent = tileInfo.AccentColor;
                tile.CachedBorder = tileInfo.BorderColor;
            }
            else
            {
                tile.CachedBg1 = (colorMask & (1 << 0)) != 0
                    ? tile.Background1Color : tileInfo.Background1Color;
                tile.CachedBg2 = (colorMask & (1 << 1)) != 0
                    ? tile.Background2Color : tileInfo.Background2Color;
                tile.CachedBase = (colorMask & (1 << 2)) != 0
                    ? tile.BaseColor : tileInfo.BaseColor;
                tile.CachedAccent = (colorMask & (1 << 3)) != 0
                    ? tile.AccentColor : tileInfo.AccentColor;
                tile.CachedBorder = (colorMask & (1 << 4)) != 0
                    ? tile.BorderColor : tileInfo.BorderColor;
            }
        }
    }

    /// <summary>
    /// Builds draw cache entries for visible tiles and writes them to the ChunkRegistry.
    /// Uses the same visibility rules as DrawSystem (space mask culling).
    /// </summary>
    private static void PopulateDrawCache(
        ref Position position,
        ref GraphicalTileList graphicalTileList,
        ChunkRegistry registry)
    {
        const int MiddleMaskCheck =
            (1 << 0) | (1 << 1) | (1 << 2) | (1 << 3)
            | (1 << 6) | (1 << 7) | (1 << 8) | (1 << 9);

        bool hasMiddleExposure = (position.SpaceMask & MiddleMaskCheck) != 0;
        bool aboveIsOpen = (position.SpaceMask & (1 << 4)) != 0;

        // Fully buried — no visible tiles
        if (!hasMiddleExposure && !aboveIsOpen)
        {
            registry.UpdateDrawCache(position.WorldCoordinate, ReadOnlySpan<DrawCacheEntry>.Empty);
            return;
        }

        int lastIndex =
            graphicalTileList.ActiveTileMask == 0
                ? -1
                : 31 - System.Numerics.BitOperations.LeadingZeroCount(
                    (uint)graphicalTileList.ActiveTileMask);

        // Stack-allocate entries buffer (max 9 tiles)
        Span<DrawCacheEntry> entries = stackalloc DrawCacheEntry[GraphicalTileList.MaxTiles];
        int entryCount = 0;

        int mask = graphicalTileList.ActiveTileMask;
        while (mask != 0)
        {
            int i = System.Numerics.BitOperations.TrailingZeroCount(mask);
            mask &= mask - 1;

            bool isTop = (i == lastIndex);
            if (isTop && !aboveIsOpen) continue;
            if (!isTop && !hasMiddleExposure) continue;

            ref var tile = ref graphicalTileList.Tiles[i];
            ref var entry = ref entries[entryCount++];

            entry.BasePosition = tile.CachedBasePosition;
            entry.RawZ = position.WorldCoordinate.Z + tile.Offset;
            entry.SortDepth = tile.CachedSortDepth;
            entry.BaseScale = tile.CachedBaseScale;
            entry.Rotation = tile.CachedRotation;
            entry.BorderMask = tile.BorderMask;
            entry.BorderWidth = tile.BorderWidth;
            entry.EntityZ = position.WorldCoordinate.Z;
            entry.SourceRect = tile.CachedSourceRect;
            entry.Bg1 = tile.CachedBg1;
            entry.Bg2 = tile.CachedBg2;
            entry.Base = tile.CachedBase;
            entry.Accent = tile.CachedAccent;
            entry.Border = tile.CachedBorder;
        }

        registry.UpdateDrawCache(position.WorldCoordinate, entries.Slice(0, entryCount));
    }
}
