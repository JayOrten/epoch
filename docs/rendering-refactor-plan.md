# Rendering Refactor Plan

Work-through plan for fixing the rendering pipeline. Each section is a self-contained change that can be tested visually before moving on.

Ordered by: least risky first, dependencies respected, biggest architectural change deferred until groundwork is laid.

---

## 1. Consolidate Smoothing Systems

**Problem:** Four independent smoothing systems (camera follow, predictive lead, VP smoothing, tile interpolation) are layered on top of each other. The VP is smoothing a value that's already smooth. The tile interpolation is smoothing a value that's already been smoothed twice. This makes visual bugs nearly impossible to attribute to a specific system, and adds latency between input and visual response.

**Current state:**
- Camera follow: SmoothDamp, 0.45s → tracks player position
- Predictive lead: lerp, ramp 2.0/1.0 → offset within camera follow
- VP smoothing: SmoothDamp, 0.1s → tracks camera.Center + lookDirection
- Tile interpolation: lerp, `1 - pow(0.0001, dt)` → smooths position + scale per tile

**Proposed solution:**

Remove the VP smoothing (layer 3). The vanishing point should just *be* `Camera.Center + LookDirection` with no additional SmoothDamp. The camera position is already smooth (0.45s damping). The look direction is already accumulated incrementally from input (no discontinuities). Adding a 0.1s spring on top of already-smooth inputs just adds lag and desynchronizes the VP from the camera, which is a likely source of visual artifacts -- the perspective warp shifts relative to the camera frame because the VP hasn't caught up yet.

If removing VP smoothing reveals jitter in the look direction, the fix belongs on the look direction input, not on the VP.

For tile interpolation (layer 4), keep it but only for Z-level transitions. This is genuinely needed when the player changes floors and every tile's depth changes simultaneously. But the `_drawTime = 0.0001` constant should be reviewed -- at 60fps the lerp factor is ~0.86, which is fast but creates a visible 2-3 frame lag on every tile every frame, even when nothing is changing Z. Consider making this a one-shot transition (interpolate for N frames after a Z-change, then snap) rather than a perpetual per-frame lerp.

**Why first:** This is the simplest change (remove code, don't add it), and it will make every subsequent change easier to evaluate visually because there's less smoothing hiding problems.

**What to test:**
- Move the camera around. Does the perspective warp feel more responsive?
- Look around with the right stick / mouse. Does the parallax shift feel tighter?
- If there's new jitter, it reveals a real problem that was previously masked.

---

## 2. Clean Up Interpolation State on Components

**Problem:** `CurrentDrawPosition`, `CurrentDrawScale`, `DrawInitialized`, and `InterpolateMovement` live on `GraphicalTile` -- a component struct. These are transient rendering state, not entity data. They couple the ECS data model to the draw system's smoothing implementation and bloat every tile instance in memory.

**Proposed solution:**

Move interpolation state into the DrawSystem itself. Options:

**Option A -- Dictionary keyed by entity + tile index.** Simple, but dictionary lookups in the hot loop aren't great.

**Option B -- Parallel array managed by DrawSystem.** Allocate a flat array of `TileDrawState` structs (position, scale, initialized flag) sized to the max tile count. Index by a stable tile ID or by entity archetype chunk index. This is cache-friendly and keeps the hot loop fast.

  * [ ] **Option C -- Do this later, after moving perspective to the GPU (step 4).** If the GPU handles perspective, the CPU no longer computes per-tile positions, and most of this state goes away entirely. The only remaining interpolation would be Z-transition smoothing, which is much simpler.

**Recommendation:** Option C. Don't invest effort cleaning up state that's about to be deleted. But if step 4 turns out to be too large or gets deferred, Option B is the fallback.

**Why second (conceptually):** Understanding that this state is temporary helps frame the later GPU work. We won't actually change code here unless step 4 doesn't happen.

**What to test:** N/A if deferring to step 4.

---

## 3. Fix Scale/Position Interpolation Asymmetry

**Problem:** Scale interpolation is always on (unconditional lerp every frame). Position interpolation is conditional on `InterpolateMovement`. This means every tile pays the cost of scale smoothing even when depth isn't changing, and the behavior is inconsistent -- a tile could have its scale lagging behind while its position snaps instantly.

**Proposed solution:**

Make both conditional on the same flag. If `InterpolateMovement` is true, interpolate both. If false, snap both. There's no case where you'd want smooth scale but instant position -- they're driven by the same depth value.

Additionally, consider making interpolation event-driven rather than perpetual. Track the player's Z-level. When it changes, mark all tiles as "in transition" and interpolate for a fixed duration (e.g. 200ms), then snap to final values. This eliminates the per-frame lerp cost during steady state (which is most frames).

**Why third:** Small, surgical change. Easy to verify visually. Reduces per-frame CPU work and fixes a logical inconsistency.

**What to test:**
- Change Z-levels (go up/down stairs or whatever triggers it). Transition should look the same.
- Stand still. Tiles should be perfectly static -- no per-frame drift from perpetual lerping.

---

## 4. Move Perspective Transform to the GPU

**Problem:** The perspective warp (`VP * (1 - perspScale) + basePos * perspScale`) is computed per-tile on the CPU every frame. This is the most expensive part of the draw loop and it's redundant -- the GPU is about to apply another coordinate transform (WVP) on top. The CPU computes a world-space position, uploads it, and the GPU transforms it again.

**Current flow:**
```
CPU: grid → world → perspective-warped world → upload as I_Position
GPU: I_Position → expand quad → WVP → clip space → pixel snap
```

**Proposed flow:**
```
CPU: grid → world → upload as I_Position (with depth as I_Depth)
GPU: I_Position → expand quad → perspective warp → WVP → clip space → pixel snap
```

**Proposed solution:**

Pass the vanishing point and depth strength to the shader as uniforms (they're the same for all tiles in a frame). Pass each tile's depth as per-instance data (already available as `I_Depth`, though currently used for sorting only -- may need a separate field for perspective depth vs. sort depth).

In the vertex shader, after quad expansion and rotation, apply the perspective transform:

```hlsl
float perspectiveScale = 1.0 + (I_PerspectiveDepth * DepthStrength);
float2 vpOffset = VanishingPoint * (1.0 - perspectiveScale);
position.xy = vpOffset + (position.xy * perspectiveScale);
```

Then proceed with WVP and pixel snapping as before.

**The Z-level bucketing concern:** You raised that Z isn't always integer. This approach sidesteps that entirely -- there's no bucketing needed. Each tile carries its own depth float as instance data, and the GPU applies the perspective individually. The cost is one multiply-add per vertex, which is trivial for the GPU. The "one transform per Z-layer" optimization would only matter if we were doing separate draw calls per layer, which we're not -- it's all one instanced call anyway.

**What changes:**
- `DrawSystem`: compute `basePosition` (grid × tileSize × scale) without perspective. Pass depth as instance data.
- `TileVertex`: add a `PerspectiveDepth` field (or repurpose existing `Depth`).
- `RenderShader.fx`: add `VanishingPoint` (float2) and `DepthStrength` (float) uniforms. Apply perspective before WVP.
- `DrawSystem`: set the new shader uniforms per-frame.

**What this eliminates:**
- CPU-side vanishing point math per tile
- CPU-side `perspectiveScale` computation per tile
- `CurrentDrawPosition` stored on components (GPU computes final position)
- Possibly `CurrentDrawScale` too, though Z-transition smoothing may still need it (see step 3)

**Why fourth:** This is the biggest change. Steps 1-3 reduce noise and simplify the system first, making this easier to implement and verify. Also, steps 1-3 deliver immediate visual improvements while this one is primarily architectural.

**What to test:**
- Everything. This touches the core rendering path.
- Side-by-side comparison: the visual output should be pixel-identical to before (minus any artifacts the old system had).
- Performance: should be measurably faster on the CPU side.

---

## 5. Unify Pixel Snapping

**Problem:** Two independent pixel snaps (camera CPU-side, vertex shader GPU-side) exist because the CPU-side perspective warp produces non-integer positions that need cleanup. If perspective moves to the GPU (step 4), the camera snap's job changes.

**Current state:**
- Camera snap: rounds camera position to `1 / (TileWidth * GlobalScale)` world units. Purpose: keep tiles at depth=0 pixel-aligned.
- Vertex snap: rounds clip-space positions to screen pixels. Purpose: catch everything the camera snap missed (perspective-warped tiles, floating-point drift).

**Proposed solution (after step 4):**

Keep the vertex shader snap -- it's the definitive final authority on pixel alignment and costs essentially nothing on the GPU.

Re-evaluate the camera snap. With perspective on the GPU, the tile positions sent from the CPU are just `grid * tileSize * scale` -- these are already exact multiples of `TileWidth * GlobalScale` (i.e., multiples of 32). The camera snap's job is to ensure that `tilePosition - cameraPosition` lands on an integer, which means snapping the camera to integer world units. The current formula snaps to `1/32` world units, which is already integer pixels. This might be correct as-is, or it might be worth simplifying to just `Round(position)` if 1 world unit = 1 pixel.

The key question: does the camera snap still prevent visible artifacts after step 4, or is the vertex snap alone sufficient? Test by disabling the camera snap and checking for seam flicker at depth=0.

If the vertex snap alone is sufficient, remove the camera snap entirely for simplicity. If seams appear, keep both but document clearly why each exists.

**Why fifth:** Depends on step 4. Can't properly evaluate until perspective is on the GPU.

**What to test:**
- Disable camera snap, keep vertex snap only. Pan around at depth=0. Any seam flicker?
- If yes, re-enable camera snap and document.
- If no, delete camera snap code.

---

## 6. Revisit the Linear Perspective Model

**Problem:** `perspectiveScale = 1.0 + (depth * 0.06)` is linear. Real perspective is `1 / distance`. The linear model goes to zero at depth = -16.67 (tiles collapse) and grows without bound above. It works at small depth ranges but doesn't degrade gracefully.

**Proposed solution:**

Replace with a bounded function that asymptotically approaches limits:

```
perspectiveScale = 1.0 / (1.0 - depth * strength)     // hyperbolic, more realistic
perspectiveScale = pow(base, depth)                      // exponential, tunable
perspectiveScale = 1.0 + strength * tanh(depth * k)     // bounded, smooth
```

The `tanh` variant is probably best for a game: it behaves linearly near depth=0 (so the current look is preserved for nearby tiles) but saturates at extreme depths (so nothing explodes or collapses).

**Why last:** Purely aesthetic, no correctness issue. Only worth doing after the architecture is clean. Also easiest to tune when the perspective is in the shader (step 4), because you can tweak the curve without recompiling C#.

**What to test:**
- Look at deep Z ranges. Do tiles at extreme depth look reasonable?
- Compare to current linear model at depth +-2. Should be nearly identical (tanh is approximately linear for small inputs).

---

## Summary / Execution Order

| Step | Change | Risk | Dependencies |
|------|--------|------|-------------|
| 1 | Remove VP smoothing | Low | None |
| 2 | Plan component cleanup (defer to step 4) | None | None |
| 3 | Fix interpolation asymmetry | Low | Step 1 (cleaner to evaluate) |
| 4 | Move perspective to GPU | Medium | Steps 1, 3 (simpler system to port) |
| 5 | Unify pixel snapping | Low | Step 4 |
| 6 | Bounded perspective curve | Low | Step 4 (easier to tune in shader) |

Steps 1 and 3 are quick wins. Step 4 is the big one. Steps 5 and 6 are cleanup that falls out naturally after step 4.

---

## Context for Future Sessions

This section captures everything needed to execute the steps above without re-exploring the codebase.

### File Map

| File | Role |
|------|------|
| `epoch/ECS/Systems/DrawSystem.cs` | Main rendering logic. Perspective transform, interpolation, shader param setup, two-pass rendering. |
| `epoch/ECS/Systems/CameraLogicSystem.cs` | Camera targeting: SmoothDamp follow, predictive lead, look direction, zoom accumulation. |
| `epoch/ECS/Systems/CameraApplySystem.cs` | Camera pixel-snap and zoom application to OrthographicCamera. |
| `epoch/ECS/Components.cs` | All ECS component structs. `GraphicalTile` (line ~110), `GraphicalTileList` (line ~48), `Position` (line ~190), `CameraState` (line ~316). |
| `epoch/Content/RenderShader.fx` | HLSL vertex + pixel shader. Instanced tile rendering, pixel snapping, border rendering, color masking. |
| `epoch/Content/EffectShader.fx` | Post-processing shader (currently all effects disabled). |
| `epoch/Graphics/Tiles/TileInstancing/TileInstancing.cs` | Instancing pipeline: `Begin()` → `Draw()` (accumulate) → `End()` (radix sort, GPU upload, draw call). |
| `epoch/Graphics/Tiles/TileInstancing/TileVertex.cs` | Per-instance GPU data struct (68 bytes). Must match HLSL `InstanceInput` layout exactly. |
| `epoch/Scenes/WorldScene.cs` | Scene setup. Loads shaders, sets one-time shader uniforms (TextureSize, TileSize, ViewportSize, SpriteTexture). Creates all systems. |
| `epoch/Utilities/Utilities.cs` | `CameraUtils.SmoothDamp()` -- critically-damped spring (Unity algorithm). |
| `epoch/ECS/Context.cs` | `GlobalContext` static class. `GlobalScale` (2.0f), camera, player entity, map registry. |

### Key Constants

| Constant | Value | Location |
|----------|-------|----------|
| `GlobalScale` | `2.0f` | `Context.cs:19` |
| `_smoothTime` (VP) | `0.1f` | `DrawSystem.cs:34` |
| `_depthStrength` | `0.06f` | `DrawSystem.cs:35` |
| `_drawTime` (lerp) | `0.0001f` | `DrawSystem.cs:41` |
| `smoothTime` (camera) | `0.45f` | `CameraLogicSystem.cs:16` |
| `_leadRampUp` | `2.0f` | `CameraLogicSystem.cs:23` |
| `_leadRampDown` | `1.0f` | `CameraLogicSystem.cs:24` |
| `clampLength` (look) | `500.0f` | `CameraLogicSystem.cs:19` |
| `TileWidth` / `TileHeight` | `16` | Loaded from tileset JSON at runtime |

### TileVertex Struct (GPU Instance Data)

68 bytes per instance. Layout must match `InstanceInput` in RenderShader.fx exactly.

```
Offset  Size  Field            HLSL Semantic     Packed As
─────────────────────────────────────────────────────────────
 0      16    TransformData    TEXCOORD1         Vector4: Position.xy, Depth, Scale
16      16    PropData         TEXCOORD2         Vector4: Rotation, BorderMask, BorderWidth, LayerDifference
32       8    RectXY           TEXCOORD3         Vector2: source rect X, Y
40       8    RectWH           TEXCOORD4         Vector2: source rect W, H
48       4    Background1Color TEXCOORD5         Color (RGBA bytes)
52       4    Background2Color TEXCOORD6         Color
56       4    BaseColor        TEXCOORD7         Color
60       4    AccentColor      TEXCOORD8         Color
64       4    BorderColor      TEXCOORD9         Color
```

For **step 4** (GPU perspective): the `Depth` field already exists in slot 1 (TEXCOORD1.z) but currently carries the sort depth. You'll need to either repurpose it or add a `PerspectiveDepth` float. The sort depth (`entityZ + offset + top`) differs from perspective depth (`entityZ - playerZ + offset`), so they can't share a field without changes. Options:
- Pack perspective depth into `LayerDifference`'s slot (TEXCOORD2.w) since `LayerDifference = entityZ - playerZ` and perspective depth = `LayerDifference + offset`. The shader can reconstruct: `perspDepth = LayerDifference + Offset`. But `Offset` isn't currently sent to the GPU.
- Overwrite `Depth` with perspective depth and compute sort order on CPU before upload (which already happens -- radix sort runs before GPU upload).
- Add a new field, growing the struct to 72 bytes and adding a TEXCOORD10 slot.

### Shader Uniform Setup

**One-time** (WorldScene.cs, during scene load):
```csharp
renderShader.Parameters["TextureSize"].SetValue(textureSize);  // spritesheet dimensions
renderShader.Parameters["TileSize"].SetValue(tileWidth, tileWidth);  // single tile size × globalScale
renderShader.Parameters["ViewportSize"].SetValue(viewportWidth, viewportHeight);
renderShader.Parameters["SpriteTexture"].SetValue(texture);
```

**Per-frame** (DrawSystem.cs, in Update):
```csharp
renderShader.Parameters["WorldViewProjection"].SetValue(viewMatrix * projectionMatrix);
renderShader.Parameters["CameraZoom"].SetValue(camera.Zoom);  // currently unused in shader
```

For **step 4**, add per-frame:
```csharp
renderShader.Parameters["VanishingPoint"].SetValue(_currentVanishingPoint);  // float2
renderShader.Parameters["DepthStrength"].SetValue(_depthStrength);           // float
```

### GraphicalTile Fields Relevant to Refactoring

```csharp
// Core data (keep)
int TileId
float Scale = 1.0f              // per-tile scale multiplier
float Offset = 0.0f             // sub-Z depth offset
bool InterpolateMovement = true
int AutoTileMask, BorderMask, BorderWidth, ColorOverrideMask
// ... color properties ...

// Transient render state (step 2/4 targets for removal)
bool DrawInitialized = false
Vector2 CurrentDrawPosition      // interpolated screen position
Vector2 DrawPositionVelocity     // unused? (SmoothDamp velocity, but lerp is used instead)
float CurrentDrawScale           // interpolated screen scale
```

Note: `DrawPositionVelocity` is declared but the current interpolation uses `Vector2.Lerp`, not `SmoothDamp`. This field may be dead code.

### DrawSystem Data Flow (Current)

```
Per frame:
  1. Get playerZ
  2. Compute vanishingPoint = SmoothDamp(current, Camera.Center + LookDirection)
  3. Compute lerpFactor = 1 - pow(0.0001, dt)
  4. Compute viewMatrix * projectionMatrix → set as WVP uniform
  5. For each entity with (Position, GraphicalTileList):
     a. Check SpaceMask visibility
     b. Check viewport culling
     c. For each active GraphicalTile:
        - basePosition = gridPos * tileSize * tileScale * globalScale     [world space]
        - depth = (entityZ - playerZ) + tile.Offset
        - perspectiveScale = 1.0 + (depth * 0.06)
        - finalPosition = VP * (1 - perspScale) + basePos * perspScale   [perspective world space]
        - finalScale = tileScale * globalScale * perspScale
        - Interpolate position (if enabled) and scale (always)
        - Resolve colors
        - TileInstancing.Draw(position, sortDepth, scale, rotation, ...)
  6. TileInstancing.End() → radix sort → GPU upload → DrawInstancedPrimitives
```

### System Execution Order

Set up in WorldScene.cs. Systems run in this exact order:
```
InputSystem → TileAdjacencySystem → MovementSystem → CameraLogicSystem → CameraApplySystem → DrawSystem
```

### Build & Test

```bash
dotnet build epoch.sln
dotnet run --project epoch/epoch.csproj
dotnet test epoch.sln
```

Relevant test file: `epoch.Tests/ECS/Systems/DrawTests.cs` -- tests perspective scale calculation. Note: tests use `depthStrength = 0.03`, code uses `0.06`. These will need updating if the perspective formula changes.
