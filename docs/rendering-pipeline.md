# Rendering Pipeline

This document maps every factor that affects tile position and scale on screen. Written to support debugging visual artifacts.

## System Execution Order (per frame)

1. `InputSystem` - gather player input
2. `TileAdjacencySystem` - update spatial masks (SpaceMask)
3. `MovementSystem` - apply movement, update grid position
4. `CameraLogicSystem` - compute smooth camera target
5. `CameraApplySystem` - apply camera position and zoom
6. `DrawSystem` - compute base tile positions, set shader uniforms, submit to GPU
7. `TileInstancing.End()` - radix sort + GPU upload + draw call
8. Vertex shader - expand quads, perspective warp, WVP projection, pixel snap

## Stage 1: Camera Target (CameraLogicSystem)

**File:** `epoch/ECS/Systems/CameraLogicSystem.cs`

### Predictive Lead

When the player holds a movement direction, the camera leads by up to one tile ahead:

```
targetLead = movementInput.Direction  (if tile ahead is passable)
_leadOffset = lerp(_leadOffset, targetLead, rampSpeed * deltaTime)
```

- Ramp-up speed: `2.0` (engages quickly)
- Ramp-down speed: `1.0` (disengages slowly, prevents snap-back)

### Position Calculation

```
playerPosition = (playerGridPos + _leadOffset) * GlobalScale * TileHeight
targetPosition = playerPosition - (screenSize / 2)
```

The camera target is centered on the player's world-space position, offset by the lead.

### SmoothDamp

```
cameraState.Position = SmoothDamp(current, target, ref velocity, smoothTime=0.45, maxSpeed=inf, dt)
```

Critically-damped spring (Unity-style). This is the primary camera smoothing -- 0.45s to reach the target.

### Look Direction

Accumulated from input each frame, clamped to a 500px circular radius:

```
newLook = currentLookDirection + (lookChange * 15.0)
if (length > 500) clamp to circle
```

Stored on `CameraState.LookDirection`. No additional smoothing -- the direction is used as-is when computing the vanishing point.

### Zoom

Accumulated per-frame deltas, applied in CameraApplySystem.

## Stage 2: Camera Apply (CameraApplySystem)

**File:** `epoch/ECS/Systems/CameraApplySystem.cs`

Applies `CameraState.Position` directly to the `OrthographicCamera`. No CPU-side pixel snapping — pixel alignment is handled definitively by the vertex shader's clip-space snap.

### Zoom

`ZoomIn`/`ZoomOut` on `OrthographicCamera` (MonoGame.Extended), then resets `ZoomAmount` to 0.

## Stage 3: DrawSystem

**File:** `epoch/ECS/Systems/DrawSystem.cs`

Computes base tile positions (no perspective — the GPU handles that), sets per-frame shader uniforms, and feeds the instancing pipeline.

### Constants (field defaults)

| Name | Value | Purpose |
|------|-------|---------|
| `_depthStrength` | `0.06f` | Perspective warp strength, passed to shader |
| `_zLerpRate` | `0.0001f` | Z-transition smoothing rate for `_displayPlayerZ` |
| `GlobalScale` | `2.0f` | Engine-wide pixel multiplier |

### 3a. Z-Transition Smoothing

One lerp per frame (not per tile):

```csharp
float lerpFactor = 1 - pow(_zLerpRate, deltaTime);
_displayPlayerZ = lerp(_displayPlayerZ, playerZ, lerpFactor);
```

`_displayPlayerZ` is the smoothed player Z used to compute per-tile perspective depth. When the player changes Z-levels, every tile's depth shifts simultaneously through this single value, so all tiles animate in lockstep.

At 60fps, `deltaTime ≈ 0.0167`, so `lerpFactor ≈ 0.86` — fast convergence, short visible transition.

### 3b. Vanishing Point & Shader Uniforms

Computed once per frame:

```csharp
vanishingPoint = Camera.Center + CameraState.LookDirection;

_renderShader.Parameters["VanishingPoint"].SetValue(vanishingPoint);
_renderShader.Parameters["DepthStrength"].SetValue(_depthStrength);
```

No additional smoothing on the vanishing point — the camera is already smooth (0.45s damping) and the look direction has no discontinuities.

### 3c. Viewport Culling

Camera bounds are converted to grid space. A margin of `15.0` grid units is added in all directions to account for perspective displacement at extreme Z-levels.

### 3d. Visibility Rules (SpaceMask)

Before any position math, tiles are culled by exposure:

- **MiddleMask** (bits 0,1,2,3,6,7,8,9): cardinal + diagonal neighbor openings
- **Bit 4**: space above is open

Rules:
- If no middle exposure AND above is not open → skip entirely (buried)
- Top tile in a stack only draws when above is open
- Non-top tiles only draw when there's middle exposure

### 3e. Per-Tile Position Calculation

For each visible `GraphicalTile` in each entity's `GraphicalTileList`:

#### Base Position (world space, no perspective)

```csharp
basePosition = Vector2(gridX * tileWidth, gridY * tileHeight) * tileScale * globalScale
```

- `gridX`, `gridY`: integer tile coordinates from `Position.WorldCoordinate`
- `tileWidth`, `tileHeight`: pixel size of one tile in the tileset (e.g. 16)
- `tileScale`: per-tile `GraphicalTile.Scale` (default 1.0)
- `globalScale`: 2.0

No perspective applied here. The GPU handles the warp.

#### Base Scale

```csharp
baseScale = tileScale * globalScale
```

Perspective scale is not multiplied in on the CPU. The GPU applies it to both position and size.

#### Perspective Depth (for GPU)

```csharp
perspectiveDepth = (entityZ - _displayPlayerZ) + graphicalTile.Offset
```

Uses the smoothed `_displayPlayerZ`, not raw `playerZ`, so depth values animate during Z-transitions. This is what drives the perspective warp in the vertex shader.

#### Sort Depth (for radix sort)

```csharp
sortDepth = entityZ + graphicalTile.Offset + position.Top
```

- `position.Top`: fractional [0, 1) sub-layer priority
- Stored in the parallel `sortDepths[]` array in TileInstancing, separate from `TileVertex.Depth`
- Used only for back-to-front ordering, not sent to the GPU as depth

### 3f. Layer Brightness

```csharp
layerDifference = entityZ - playerZ
```

Passed to shader for HSL lightness adjustment. Tiles far from the player's Z-level are dimmed.

## Stage 4: TileInstancing -- Sort & GPU Upload

**File:** `epoch/Graphics/Tiles/TileInstancing/TileInstancing.cs`

`Draw()` signature: `Draw(position, depth, sortDepth, scale, rotation, ...)`

- `depth` → stored in `TileVertex.Depth`, sent to GPU as perspective depth (`I_Depth`)
- `sortDepth` → stored in parallel `sortDepths[]` float array (not in the vertex struct)

1. **Collect:** `Draw()` calls accumulate `TileVertex` structs into `instanceDataArray` and sort keys into `sortDepths[]`
2. **Radix Sort:** 4-pass 8-bit radix sort on `sortDepths[]` (bitwise for descending = back-to-front). The final scatter copies `TileVertex` structs (carrying perspective depth in `.Depth`) into `sortedDataArray`.
3. **Upload:** Sorted data written to GPU vertex buffer
4. **Draw:** Single `DrawInstancedPrimitives` call (2 triangles per tile, instanced)

## Stage 5: Vertex Shader

**File:** `epoch/Content/RenderShader.fx`

### Per-frame uniforms

| Uniform | Type | Value |
|---------|------|-------|
| `WorldViewProjection` | float4x4 | viewMatrix * projectionMatrix |
| `VanishingPoint` | float2 | Camera.Center + LookDirection (world pixels) |
| `DepthStrength` | float | 0.06 |
| `ViewportSize` | float2 | screen resolution |

### Per-instance input

- `I_Position`: base world-space position from DrawSystem (no perspective)
- `I_Depth`: perspective depth (`entityZ - _displayPlayerZ + offset`)
- `I_Scale`: base scale (tileScale × globalScale, no perspective multiplier)
- `I_Rotation`: rotation in degrees (from tileset autotile lookup)

### Transform sequence

```hlsl
// 1. Expand unit quad to sprite pixel size
size = float2(sourceRectW, sourceRectH) * I_Scale;
position.xy *= size;

// 2. Rotate around tile center
position.xy -= size * 0.5;
position.xy = mul(position.xy, rotationMatrix);
position.xy += size * 0.5;

// 3. Translate to world position
position.xy += I_Position;

// 4. Perspective warp (GPU-side)
//    tanh implemented via exp to avoid SM 3.0 compatibility issues
float depthInput = I_Depth * DepthStrength;
float e2x = exp(2.0 * depthInput);
float perspectiveScale = 1.0 + (e2x - 1.0) / (e2x + 1.0);   // 1 + tanh(depthInput)
position.xy = VanishingPoint + (position.xy - VanishingPoint) * perspectiveScale;

// 5. Transform to clip space (camera view + orthographic projection)
position = mul(position, WorldViewProjection);

// 6. Pixel snapping
pixelScale = ViewportSize / 2.0;
position.xy = round(position.xy * pixelScale) / pixelScale;
```

### Perspective Formula

`perspectiveScale = 1 + tanh(depth * DepthStrength)`

- At depth=0: scale=1.0 (no distortion)
- At depth=+1: scale ≈ 1.058 (≈6% larger, nearly identical to old linear at small depths)
- At depth=-1: scale ≈ 0.942 (≈6% smaller)
- At extreme negative depth: scale saturates toward 0.0 (tiles shrink but never collapse or invert)
- At extreme positive depth: scale saturates toward 2.0 (tiles grow but never explode)

The tanh model is numerically identical to the old linear model (`1 + depth * 0.06`) for `|depth| < 3`. It differs only at large Z-level separations.

### Pixel Snapping

After projection to clip space (-1 to +1), vertex positions are snapped to the nearest screen pixel. This is the single authority on pixel alignment — it handles sub-pixel camera drift, perspective-warped offsets, and floating-point accumulation.

## Stage 6: Pixel Shader

**File:** `epoch/Content/RenderShader.fx`

Not directly relevant to position/scale artifacts, but for completeness:

- **Color masking:** sprite texture channels select between 5 color slots (Background1, Background2, Base, Accent, Border)
- **Border rendering:** distance-field edges using `BorderMask` bits, thickness adapts to zoom via `fwidth()`
- **Layer brightness:** HSL lightness shift based on `LayerDifference`

## Stage 7: Post-Processing

**File:** `epoch/Content/EffectShader.fx`

DrawSystem renders to a `RenderTarget2D`, then blits to screen through EffectShader. Currently all effects are disabled (lens distortion=0, chromatic aberration=0, grain=0, etc.). No geometry transform -- pure pixel shader.

---

## Position Lifecycle: Grid to Screen Pixel

This section traces a single tile's position through every coordinate space, with concrete numbers assuming TileWidth=16, TileHeight=16, GlobalScale=2.0, screen resolution 1280x720, no zoom.

### Coordinate Spaces

| Space | Units | Range |
|-------|-------|-------|
| **Grid** | integer tile coords | (0,0) to (79,79) |
| **World** | pixels at engine scale | grid × tileSize × scale (e.g. tile (3,2) → (96, 64)) |
| **View** | world relative to camera | world - cameraPosition |
| **Clip** | normalized device coords | (-1, -1) to (+1, +1) |
| **Screen** | physical pixels | (0, 0) to (1280, 720) |

### Step-by-step trace

#### 1. Grid → World (DrawSystem)

```
basePosition = (gridX * tileWidth, gridY * tileHeight) * tileScale * globalScale
```

Example: tile at grid (3, 2), tileScale=1.0:
```
basePosition = (3 * 16, 2 * 16) * 1.0 * 2.0 = (96, 64)
```

One tile = 32 world units = 32 screen pixels at 1:1 zoom. This value is uploaded to the GPU as-is in `I_Position`.

#### 2. Camera position (CameraLogicSystem → CameraApplySystem)

The camera isn't applied to tile positions directly. It's baked into the **view matrix**. DrawSystem computes `viewMatrix * projectionMatrix` and passes it to the shader as `WorldViewProjection`.

`I_Position` is still raw world space when it reaches the GPU.

#### 3. Vertex shader: quad expansion + rotation + translation (RenderShader.fx)

```hlsl
size = float2(sourceRectW, sourceRectH) * I_Scale;   // (16, 16) * 2.0 = (32, 32)
position.xy *= size;                                   // unit quad → 32×32 pixel quad
// ... rotation around center ...
position.xy += I_Position;                             // translate to (96, 64) in world space
```

After this step, vertices are at world-space positions. For a 32×32 tile at (96, 64), corners are at (96, 64), (128, 64), (96, 96), (128, 96).

#### 4. Vertex shader: perspective warp

```hlsl
float perspectiveScale = 1.0 + tanh(I_Depth * DepthStrength);
position.xy = VanishingPoint + (position.xy - VanishingPoint) * perspectiveScale;
```

At depth=0 (tile on same Z as player), perspectiveScale=1.0, no change. At depth=+1, the tile's distance from the vanishing point grows by ~6%. Note that this scales both **position** and **size** (each vertex moves, so the quad itself scales).

Still in world space after this step.

#### 5. Vertex shader: WorldViewProjection → clip space

```hlsl
position = mul(position, WorldViewProjection);
```

This applies the combined view + orthographic projection:
- **View:** subtract camera position, apply zoom
- **Projection:** `CreateOrthographicOffCenter(0, viewportWidth, viewportHeight, 0, 0, 1)` maps screen pixels to clip space

The projection maps X from `[0, 1280]` to `[-1, +1]` and Y from `[0, 720]` to `[+1, -1]` (Y-flipped for screen convention).

Example: if camera is at (-544, -296), the view matrix adds (544, 296). Tile corner at (96, 64) becomes (640, 360) in view space — dead center. Projection maps that to clip (0, 0).

#### 6. Vertex shader: pixel snapping

```hlsl
float2 pixelScale = ViewportSize / 2.0;               // (1280, 720) / 2 = (640, 360)
position.xy = round(position.xy * pixelScale) / pixelScale;
```

Snaps clip-space coordinates to the nearest value that maps to an exact screen pixel.

**The math:** Clip space `[-1, +1]` spans `ViewportSize` pixels. One pixel = `2.0 / ViewportSize` in clip space. Multiplying by `ViewportSize / 2` converts to pixel units, `round()` snaps to integers, divides back to clip space.

Example: vertex at clip x = 0.00078 (≈ 0.5 pixels off center):
```
0.00078 * 640 = 0.4992 → round → 0 → 0 / 640 = 0.0
```
Snapped to dead center.

---

## Summary: What Affects Tile Position

Listed in order of application:

| Factor | Where | Effect |
|--------|-------|--------|
| Grid position (X, Y) | `Position.WorldCoordinate` | Base tile location |
| Tile dimensions | `Tileset.TileWidth/Height` (16) | Grid-to-pixel conversion |
| Per-tile scale | `GraphicalTile.Scale` (default 1.0) | Per-sprite size adjustment |
| Global scale | `GlobalContext.GlobalScale` (2.0) | Engine-wide pixel multiplier |
| Camera position | SmoothDamp toward player (0.45s) | Viewport centering (baked into view matrix) |
| Camera look direction | Up to 500px offset, sets VanishingPoint | Shifts VP and perspective origin |
| Predictive lead | Up to 1 tile ahead of player | Anticipatory camera shift |
| Camera zoom | OrthographicCamera zoom | Magnification (view matrix) |
| Perspective warp | GPU: `1 + tanh(depth * 0.06)`, centered on VanishingPoint | Pseudo-3D depth shift |
| Vertex shader pixel snap | Round in clip space | Definitive pixel alignment |

## Summary: What Affects Tile Scale

| Factor | Where | Effect |
|--------|-------|--------|
| Per-tile scale | `GraphicalTile.Scale` | Per-sprite multiplier |
| Global scale | `GlobalContext.GlobalScale` (2.0) | Engine-wide multiplier |
| Perspective scale | GPU: `1 + tanh(depth * 0.06)` | Z-depth scaling |
| Camera zoom | View matrix | Magnification (post-DrawSystem) |

## Smoothing Systems

There are **two independent smoothing systems** running simultaneously:

1. **Camera follow** -- SmoothDamp, 0.45s -- camera tracks player position
2. **Predictive lead** -- lerp, ramp 2.0/1.0 -- camera leads in movement direction

Plus one one-shot transition smoother:

3. **`_displayPlayerZ`** -- lerp, `1 - pow(0.0001, dt)` -- single float that lerps toward actual playerZ; drives perspective depth for all tiles during Z-level transitions. All tiles animate in lockstep. Converges in ~3 frames at 60fps.
