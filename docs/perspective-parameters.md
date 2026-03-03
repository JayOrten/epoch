# Perspective & Camera Parameter Reference

All tuning levers for the depth stacking, viewport squash, and camera movement systems.

## Perspective / Depth Stacking

**File:** `epoch/ECS/Systems/DrawSystem.cs`

| Field | Default | Effect |
|---|---|---|
| `_depthStrength` | `0.04f` | Shader `DepthStrength` — scales the tanh input. Higher = tiles converge toward VP faster per Z-level. At 0 the VP warp does nothing. |
| `_vpBlendFactor` | `0.4f` | Shader `VpBlend` — lerp between ortho stack (0) and VP warp (1). 0.4 = 40% VP convergence, 60% flat displacement. |
| `_maxStackHeight` | `24f` | Max pixels of ortho offset per depth-unit at full tilt. Higher = more vertical separation between Z-levels in ortho mode. Scaled by `vpRatio` and `globalScale` at runtime. |
| `_maxZScale` | `1.5f` | Maximum vertical stretch factor applied to the projection matrix. At full elevation (`vpRatio=1`), the ortho projection expands vertically by this factor, squashing the apparent tile height. |

## Viewport Squash (Computed at Runtime)

**File:** `epoch/ECS/Systems/DrawSystem.cs`

| Value | Formula | Effect |
|---|---|---|
| `vpRatio` | `clamp(VpDistance / 1200, 0, 1)` | Normalized elevation. Drives `zScale`, `stackHeight`, and VP position distance. |
| `zScale` | `lerp(1.0, _maxZScale, vpRatio)` | Actual stretch factor this frame. Fed into the projection matrix (`halfExtra`) and per-tile `perspectiveDepth`. |
| `halfExtra` | `viewport.Height * (zScale - 1) / 2` | Extra vertical range added symmetrically to the ortho projection. Makes tiles appear foreshortened. |
| `perspectiveDepth` | `(tileZ - _displayPlayerZ + offset) * zScale` | Per-tile depth sent to shader. Scaled by `zScale` so Z-layer gaps increase with elevation, then projection squash compresses them back. |

## Camera Movement

**File:** `epoch/ECS/Systems/CameraLogicSystem.cs`

| Field | Default | Effect |
|---|---|---|
| `smoothTime` | `0.45f` | SmoothDamp time constant for camera follow. Lower = snappier, higher = floatier. |
| `zoomSpeed` | `0.01f` | Multiplier on scroll input for zoom delta per frame. |
| `rotationSpeed` | `3.0f` | Radians per second of camera rotation from input. |
| `elevationSpeed` | `300.0f` | Pixels per second change to `VpDistance` from elevation input. Controls how fast you tilt. |
| `minVpDistance` | `0f` | Floor for VP distance — directly overhead, no perspective. |
| `maxVpDistance` | `1200f` | Ceiling for VP distance — maximum tilt. Also the denominator in `vpRatio`. |
| `_leadRampUp` | `1.0f` | How fast the predictive look-ahead engages when moving. |
| `_leadRampDown` | `3.0f` | How fast look-ahead decays when you stop. Lower = longer coast. |

## Camera State

**File:** `epoch/ECS/Components.cs` (`CameraState` struct)

| Property | Default | Effect |
|---|---|---|
| `VpDistance` | `600f` | Starting VP distance (mid-tilt). Runtime value. |
| `Rotation` | `0f` | Camera rotation in radians. Drives VP orbit, stack direction, and input remapping. |
| `ZoomAmount` | `0f` | Accumulated zoom. Applied in CameraApplySystem. |
| `Position` | `Vector2.Zero` | Camera world position. |

## Global

| Value | Default | Location | Effect |
|---|---|---|---|
| `GlobalScale` | `2.0f` | `epoch/ECS/Context.cs` | Uniform scale on all tile rendering. Multiplies tile positions and ortho stack height. |

## Z-Transition Smoothing

**File:** `epoch/ECS/Systems/DrawSystem.cs`

| Field | Default | Effect |
|---|---|---|
| `_displayPlayerZ` | (runtime) | Smoothed player Z used as the depth origin. Tiles at this Z have depth=0. |
| `_zLerpRate` | `0.0001f` | Exponential decay rate for Z-level transitions. Smaller = slower, smoother transitions. |

## Shader Uniforms

**File:** `epoch/Content/RenderShader.fx`

| Uniform | Set By | Effect |
|---|---|---|
| `VanishingPoint` | DrawSystem | World-pixel position of VP. Orbits with camera rotation. |
| `DepthStrength` | DrawSystem (`_depthStrength`) | tanh scale factor for VP convergence. |
| `StackDirection` | DrawSystem | Unit vector for ortho displacement direction. Rotates with camera. |
| `StackHeight` | DrawSystem | Pixels of ortho offset per depth-unit (= `vpRatio * _maxStackHeight * globalScale`). |
| `VpBlend` | DrawSystem (`_vpBlendFactor`) | Lerp factor: 0 = pure ortho, 1 = pure VP. |
| `CameraZoom` | DrawSystem | Current camera zoom. Used for vertex snap and multisampling threshold. |
