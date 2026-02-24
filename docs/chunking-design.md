# Chunk System Design

## Context

The draw system iterates all ~30k entities every frame, even though only ~5k are visible after viewport culling. The ECS query overhead of touching every entity is the dominant remaining cost. Chunking solves this by only loading entities near the camera.

## Performance Work Leading Here

Before arriving at chunking, the following optimizations were applied to the draw loop:

1. **Replaced `Color?` with `Color` + bitmask** in `GraphicalTile` to reduce struct size (~192 bytes to ~120 bytes). Setters auto-manage `ColorOverrideMask`.
2. **Removed per-tile `Stopwatch.GetTimestamp()` calls** — was 60k timestamp reads/frame. Replaced with frame-averaged profiling (2 timestamps/frame, logged every 60 frames).
3. **Hoisted `GlobalContext` lookups** (`TileManager`, `Tileset`, `GlobalScale`, etc.) into locals before the loop.
4. **Inlined `ComputeTileTransform`** math into the loop body (was a 9-parameter static method called 30k times).
5. **Eliminated `DrawTile` method** — everything is in the loop body now.
6. **Viewport culling** — converts camera `BoundingRectangle` to grid space, skips entities outside visible area + margin.
7. **`ref var position`** instead of copying the Position struct per entity.
8. **Color mask short-circuit** — `colorMask == 0` (common case) skips 5 individual bit checks.
9. **`[AggressiveInlining]` on `TileInstancing.Draw`** — hints JIT to inline the 14-field write.
10. **Removed `Stopwatch` from `TileInstancing.End()`** radix sort.

After all of this, the per-tile cost is about as low as it gets. The remaining bottleneck is ECS iteration overhead: traversing all entities in the world just to cull most of them.

## Architecture

### Core Idea

Divide the world into fixed-size chunks (e.g., 16x16 grid cells). Only chunks near the camera are "loaded" (entities created). Chunks far from the camera are "unloaded" (entities destroyed). The draw system only iterates loaded entities.

### Components

- **MapRegistry** — remains a static spatial index of terrain tiles only. Always in sync with what exists. Used for adjacency lookups (borders, autotiling).
- **Chunks** — groups of tile entities. Each chunk corresponds to a region of the grid. Responsible for creating/destroying tile entities and populating/clearing MapRegistry.
- **Dynamic entities** (player, NPCs, projectiles) — queried separately via ECS, never put in MapRegistry, never culled. There are so few of these that iterating all of them every frame is fine.

### Draw System

Two iteration sources:
1. Visible chunk entities (tagged or queried from loaded chunks)
2. All dynamic entities (separate ECS query, always drawn)

### Entity Tagging

Loaded chunk entities receive a tag component (e.g., `LoadedTag`). The draw query filters on `WithAll<Position, GraphicalTileList, LoadedTag>`. Arch places tagged entities in a separate archetype, so the query only touches loaded entities. Tag is added at chunk creation time.

### Chunk Lifecycle

**Load:**
1. Determine which chunks overlap the camera viewport (with margin for look-ahead)
2. For each newly visible chunk: create tile entities from tilemap data, add `LoadedTag`, populate MapRegistry
3. Recalculate adjacency for edge tiles of neighboring already-loaded chunks (they now have new neighbors)

**Unload:**
1. Determine which loaded chunks are no longer near the camera
2. For each chunk leaving range: destroy tile entities, remove from MapRegistry
3. Recalculate adjacency for edge tiles of remaining neighbor chunks (they lost neighbors)

**Edge adjacency cost:** proportional to chunk perimeter, not area. For a 16x16 chunk that's ~64 edge tiles per Z-level — trivial, and only happens on chunk load/unload.

### Player Tile Modification (Break/Place)

Tiles can be destroyed or placed by the player (Minecraft-style).

**Break tile at (X, Y, Z):**
1. Destroy the entity
2. Remove from MapRegistry
3. Recalculate adjacency for the 26 neighbors (lookup via MapRegistry is instant)
4. Mark neighbors dirty so TileAdjacencySystem recomputes borders/autotiling

**Place tile at (X, Y, Z):**
1. Create entity with appropriate components + `LoadedTag`
2. Insert into MapRegistry
3. Recalculate adjacency for 26 neighbors

This is triggered by player actions, not per-frame. The cost is negligible.

### Dynamic Entities

Player, NPCs, projectiles, etc. are a separate category:

- **Not in MapRegistry.** They move, so tracking them spatially would require per-frame updates. Not worth it for a small number of entities.
- **Query MapRegistry** to check surroundings (collision, interaction), but don't live in it.
- **Queried separately** by the draw system via a standard ECS query (e.g., `WithAll<Position, GraphicalTileList>`  without requiring `LoadedTag`, or with a `DynamicTag`).
- **Never culled.** Always drawn, always updated. There will be dozens, not thousands.

### MapRegistry Contract

MapRegistry is always in sync with loaded terrain:
- Chunk load adds entries
- Chunk unload removes entries
- Player modification (break/place) updates entries
- Adjacency is recomputed locally whenever the registry changes
- Dynamic entities are not tracked in MapRegistry

## Open Questions

- Chunk size: 16x16? 32x32? Tradeoff between load/unload granularity and overhead.
- How far ahead to load chunks (camera velocity prediction? fixed radius?).
- Whether to load/unload asynchronously to avoid frame spikes.
- Z-level handling: are chunks 2D columns (16x16 x all Z) or 3D cubes (16x16x16)?
