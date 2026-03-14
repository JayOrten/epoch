# Draw Cache Implementation

## Problem

DrawSystem iterated ~30k entities via ChunkRegistry packed lists, but ~72% were immediately space-culled (buried underground). Each entity required random ECS lookups into ~1KB `GraphicalTileList` components stored in archetype order (not spatial order), causing severe cache thrashing. Total cost: ~6.5ms/frame (3ms iterate + 3.5ms tile work + 1.15ms sort) to draw only ~16k tiles.

## Solution

Replace per-entity ECS random access with contiguous, pre-filtered chunk-local arrays. Five phases, each independently testable.

## Changes by Phase

### Phase 1: Move Depth to Shader

Moved `perspectiveDepth` and `layerDifference` computation from CPU to GPU.

**RenderShader.fx:**
- Added three uniforms: `DisplayPlayerZ`, `PlayerZLevel`, `ZScale`
- Vertex shader now computes derived values from raw Z:
  ```hlsl
  float I_Depth = (i.I_TransformData.z - DisplayPlayerZ) * ZScale;
  float I_LayerDifference = i.I_PropData.w - PlayerZLevel;
  ```

**DrawSystem.cs:**
- Sets the three new uniforms per frame before the draw loop
- Both `DrawEntityTilesCached` and `DrawEntityTilesUncached` now pass raw Z values (`entity Z + tile offset` and `entity Z`) instead of pre-computed depth/layerDifference
- Removed `displayPlayerZ`, `playerZLevel`, `zScale` params from draw method signatures

**Why:** The draw cache doesn't need to store per-frame-varying values (`displayPlayerZ`, `zScale`). Without this, every cache entry would need updating every frame during Z-level transitions.

### Phase 2: Draw Cache Data Structure

**ChunkRegistry.cs — new struct:**
```csharp
[StructLayout(LayoutKind.Sequential)]
internal struct DrawCacheEntry  // ~72 bytes
{
    public Vector2 BasePosition;
    public float RawZ;            // entity Z + tile offset
    public float SortDepth;       // entity Z + tile offset + position.Top
    public float BaseScale;
    public float Rotation;
    public float BorderMask;
    public float BorderWidth;
    public float EntityZ;         // entity Z
    public Rectangle SourceRect;
    public Color Bg1, Bg2, Base, Accent, Border;
}
```

**Chunk class additions:**
- `DrawCache[]` — dense array of draw-ready tile data (only visible tiles)
- `DrawCacheCount` — number of active entries
- `DrawCacheStart[localIndex]` — start offset in DrawCache (-1 = not cached)
- `DrawCacheTileCount[localIndex]` — tile count per entity
- `DrawCacheOwner[cacheIndex]` — reverse map for removal fixup

**API:**
- `GetDrawCache(cx, cy, out count)` — returns `ReadOnlySpan<DrawCacheEntry>`
- `UpdateDrawCache(coord, entries)` — handles add, update-in-place, and remove
- `RemoveFromDrawCache(chunk, localIdx)` — shift-down removal (preserves block contiguity)

**Memory:** ~25KB per chunk, ~1.2MB for 49 loaded chunks.

### Phase 3: Populate Cache from TileAdjacencySystem

**TileAdjacencySystem.cs:**
- Added `PopulateDrawCache()` called after `PopulateTileCache()` for each dirty entity
- Applies same visibility rules as the old DrawSystem (space mask culling):
  - Fully buried (no middle exposure and above not open) → empty cache
  - Top tile hidden if above is not open
  - Middle tiles hidden if no middle exposure
- Uses `stackalloc DrawCacheEntry[MaxTiles]` to avoid heap allocation
- Skips `CompositePartComponent` entities (player body parts use uncached path)

### Phase 4: Rewrite DrawSystem

**Before:**
```
for chunk (cx, cy):
  for entity in packed:
    entity.Get<Position>()           // random ECS lookup
    entity.Get<GraphicalTileList>()  // random ECS lookup (~1KB)
    space mask cull (72% culled)
    viewport cull
    DrawEntityTilesCached(...)
```

**After:**
```
for chunk (cx, cy):
  var cache = GetDrawCache(cx, cy)   // contiguous span
  for entry in cache:
    viewport cull on BasePosition
    tileInstancing.Draw(entry fields...)
```

- No ECS lookups. No inner tile loop. No space-mask cull (pre-filtered).
- Composite parts path (player body) unchanged — still uses Arch query + uncached draw.
- Removed `DrawEntityTilesCached()` method entirely.
- Added viewport culling on `entry.BasePosition` in world-pixel space.

### Phase 5: Sort Optimization

**TileInstancing.cs:**
- Added `sortedDataArray` buffer (same size as `instanceDataArray`)
- Replaced cycle-following permutation with gather-copy:
  ```csharp
  // Old: random reads AND random writes (cache-hostile)
  instanceDataArray[j] = instanceDataArray[src[j]]; // cycle-following

  // New: random reads, sequential writes (write-combine friendly)
  for (int i = 0; i < count; i++)
      sortedDataArray[i] = instanceDataArray[perm[i]];
  ```
- Upload from `sortedDataArray` instead of `instanceDataArray`
- `RadixSortByDepth` now returns sorted indices via `out` parameter instead of permuting in-place

## Bug Fixes

### Missing tiles (swap-remove corruption)
The original swap-remove in `RemoveFromDrawCache` copied the last N entries into the gap, but this could split a multi-entry block that straddled the tail boundary. Fixed by using shift-down instead: `Array.Copy(blockEnd → start, shiftCount)`. O(n) per chunk but maintains block contiguity.

The owner fixup loop also had a double-decrement bug: after updating an owner's start, the new value could still pass the `>= blockEnd` check, causing a second decrement. Fixed by using `== blockEnd + i` (exact match on old position) instead of `>= blockEnd`.

### Ghost player tiles (double rendering)
Composite part entities (player head/torso/legs) have `GraphicalTileList` and get `DirtyTag`, so `PopulateDrawCache` was writing them into the chunk draw cache. DrawSystem then rendered them twice: once from the draw cache (frozen at spawn position) and once from the composite parts query (updated each frame). Fixed by skipping draw cache population for `CompositePartComponent` entities.

## Files Modified

| File | Changes |
|------|---------|
| `epoch/Content/RenderShader.fx` | Added 3 uniforms, shader computes depth from raw Z |
| `epoch/ECS/ChunkRegistry.cs` | DrawCacheEntry struct, cache storage, mutation API |
| `epoch/ECS/Systems/TileAdjacencySystem.cs` | PopulateDrawCache after tile cache computation |
| `epoch/ECS/Systems/DrawSystem.cs` | Flat cache iteration, viewport culling, simplified profiling |
| `epoch/Graphics/Tiles/TileInstancing/TileInstancing.cs` | Gather-copy sort, sortedDataArray buffer |

## Profiling

The log output format changed:
```
Draw (avg 60f): cache: N (M view-culled)  tiles: T  |  iterate=Xms  tileWork=Yms  sort=Sms  upload=Ums  total=Tms
```

- `cache` — total draw cache entries iterated
- `view-culled` — entries skipped by viewport culling
- `tiles` — entries that reached tileInstancing.Draw()
- `iterate` — time in the draw cache loop (reading entries + calling Draw)
- `tileWork` — time in composite parts uncached draw only
- `sort` — radix sort + gather-copy
- `upload` — GPU buffer upload + draw calls

## Design Decisions

**Shift-down vs swap-remove:** Swap-remove is O(1) but breaks block contiguity when the tail region spans multiple entity blocks. Shift-down is O(entries-per-chunk) but correct. Since removal only happens for dirty entities (~0.1%/frame in steady state), the cost is negligible.

**Gather-copy vs cycle-following:** Both do N random reads on ~64-byte TileVertex structs. Gather-copy adds sequential writes (write-combine friendly) while cycle-following does random writes. The practical difference is modest since random reads dominate.

**Viewport culling in draw loop:** The draw cache contains all visible tiles for all loaded chunks, including tiles in edge chunks that are off-screen. Viewport culling on `BasePosition` (world-pixel coords) reduces the number of tiles reaching the sort.
