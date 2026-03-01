# Shader Filtering Changes — Session Log

Documents all changes made to implement zoom-out filtering and fix visual artifacts.
References the research in `shader-filtering-research.md`.

## Files Modified

| File | Changes |
|------|---------|
| `RenderShader.fx` | Sampler Point→Linear, classifyTexel(), 3x3 multisampling, adaptive vertex snap, analytical border filter, debug border toggle, fwidth hoisting |
| `DrawSystem.cs` | SamplerState.PointClamp→LinearClamp, CameraZoom + DebugBordersOff uniforms |
| `Core.cs` | Exposed `DebugOverlay` as internal static property |
| `DebugOverlay.cs` | Added zoom/tile size/texels-per-pixel/snap readout, F4 border toggle |
| `GameController.cs` | Added `ToggleBorders()` (F4 key) |
| `CameraLogicSystem.cs` | No functional changes (was in diff due to formatting) |

---

## Change 1: Sampler — Point to Linear

**Files:** `RenderShader.fx` (sampler declaration), `DrawSystem.cs` (SamplerState)

The sampler was changed from `Point` to `Linear`, and the C# side from `SamplerState.PointClamp` to `SamplerState.LinearClamp`. This was a prerequisite from the earlier `uvPixelArt` work documented in the research doc — `Linear` is required for controlled bilinear blending at texel boundaries.

With the current multi-sample-only approach, samples are snapped to texel centers (`floor(pos) + 0.5`), so `Linear` filtering returns exact texel values anyway. The sampler mode doesn't functionally matter for the current code but is kept as `Linear` for correctness if the single-sample path is ever restored.

## Change 2: `classifyTexel()` Function

**File:** `RenderShader.fx`

New helper function that samples a single texel and classifies its color code:

```hlsl
float4 classifyTexel(float2 uv, float4 bg1, float4 bg2, float4 baseC, float4 accentC)
{
    float4 s = tex2Dlod(TextureSampler, float4(uv, 0, 0));
    // step(0.5, ...) thresholds → classify as magenta/cyan/white/yellow
    // returns the resolved display color
}
```

Uses `tex2Dlod` (explicit LOD 0) instead of `tex2D` because the 3x3 grid samples are at computed positions that may not match the GPU's automatic LOD calculation.

**Why it exists:** The sprite textures store color *codes* (magenta = bg1, cyan = bg2, etc.), not actual colors. Mipmaps average codes before classification, destroying them. This function classifies first, then the caller averages the resolved colors.

## Change 3: Post-Classification 3x3 Multisampling

**File:** `RenderShader.fx`

When zoomed out (`CameraZoom < 0.6`), each screen pixel covers multiple texels. A single sample would arbitrarily pick one texel, causing shimmer as the camera moves. Instead, a 3x3 grid of samples is taken across the pixel footprint, each classified independently, then averaged:

```hlsl
float2 gridStep = pixelFootprint / 3.0;
#define MSAMPLE(ox, oy) classifyTexel(
    (floor(texelPos + float2(ox, oy) * gridStep) + 0.5) / TextureSize, ...)

spriteLayer = (MSAMPLE(-1,-1) + ... + MSAMPLE(1,1)) / 9.0;
```

Each sample snaps to its texel center via `floor(x) + 0.5` so the Linear sampler returns exact values.

**Cost:** 9 texture reads per pixel when zoomed out. Skipped via uniform branch when zoomed in (1 read).

**Performance note:** The `fwidth(texelPos)` call that computes `pixelFootprint` was originally inside the zoom branch. GPU drivers can flatten branches containing derivative instructions (`fwidth`/`dFdx`/`dFdy`), causing both branches to execute — meaning 9+1 texture reads per pixel at ALL zoom levels. Fixed by hoisting `fwidth` outside all branches.

## Change 4: Removed Single-Sample Path / `uvPixelArt()`

**File:** `RenderShader.fx`

The `uvPixelArt()` function (texel-edge bilinear clamping from the research doc) was removed. Testing showed multi-sample-only produced better results at all zoom levels. The zoomed-in path now uses a single `classifyTexel()` call at the texel center:

```hlsl
float2 centerUV = (floor(texelPos) + 0.5) / TextureSize;
spriteLayer = classifyTexel(centerUV, ...);
```

## Change 5: Adaptive Vertex Snap Resolution

**File:** `RenderShader.fx` (vertex shader)

**Problem:** The vertex shader snaps each vertex to the nearest screen pixel to prevent tile seam shimmer. At low zoom, tiles are small on screen, and the rounding error (max 0.5px) becomes a significant fraction of the tile size — causing diamond-shaped deformations.

**Old approach:** `round(pos * pixelScale) / pixelScale` — always snaps to whole pixels.

**New approach:** Snap to finer sub-pixel grids as zoom decreases:

```hlsl
float snapMul = clamp(1.0 / CameraZoom, 1.0, 4.0);
float2 snapScale = pixelScale * snapMul;
v.V_Position.xy = round(v.V_Position.xy * snapScale) / snapScale;
```

| Zoom | Snap grid | Tile size | Max error % |
|------|-----------|-----------|-------------|
| 1.0  | 1px       | 48px      | ~2%         |
| 0.5  | 0.5px     | 24px      | ~2%         |
| 0.25 | 0.25px    | 12px      | ~2%         |

Keeps rounding error at a constant ~2% of tile size. Sub-pixel snapping still absorbs floating-point vertex discrepancies between adjacent tiles (prevents seams) but the error is too small to cause visible deformation.

**Required:** `CameraZoom` uniform uncommented and set from C# (`DrawSystem.cs` line 107).

## Change 6: Analytical Box Filter for Borders

**File:** `RenderShader.fx` (pixel shader)

**Problem:** Borders flickered/swam when zoomed out. The old `smoothstep` evaluated border visibility at a single point (pixel center), which shifts with sub-pixel camera movement.

**Old:**
```hlsl
float startFade = max(0.0, targetWidth - uvPerPixel * 3.0);
float endFade = targetWidth;
float inTop = 1.0 - smoothstep(startFade, endFade, distTop);
```

**New:**
```hlsl
float inTop = saturate((targetWidth - borderCoord.y) / uvPerPixel + 0.5);
```

This computes the fraction of the pixel geometrically covered by the border — an analytical box filter. Coverage changes smoothly and proportionally with camera movement instead of jumping.

Border minimum width changed from `uvPerPixel * 1.0` to `uvPerPixel * 1.5` (1.5 screen pixels minimum) for additional stability.

## Change 7: Debug Border Toggle

**Files:** `RenderShader.fx`, `DrawSystem.cs`, `Core.cs`, `DebugOverlay.cs`, `GameController.cs`

Added `DebugBordersOff` uniform (float, 0.0 or 1.0). When 1.0, the entire border computation block is skipped — not just the final blend, but all the rotation, mask extraction, distance math, and HSL color adjustment for the border.

Toggled via F4 key (only functional when debug overlay is open via F3).

## Change 8: Debug Overlay Enhancements

**File:** `DebugOverlay.cs`

Added diagnostic readouts:
- **Zoom:** raw camera zoom value
- **Tile size:** screen pixels per tile (24 × GlobalScale × zoom)
- **Texels/pixel:** how many texels map to one screen pixel
- **Snap:** current snap grid resolution
- **Borders [F4]:** ON/OFF toggle state

Drop shadow added for readability (white text + black offset).

## Change 9: `fwidth` Hoisting (Performance Fix)

**File:** `RenderShader.fx`

Derivative instructions (`fwidth`, `dFdx`, `dFdy`) inside conditional branches can cause GPU drivers to flatten the branch — executing both paths for every pixel. Two `fwidth` calls were moved outside their respective branches:

1. `fwidth(texelPos)` — was inside `if (CameraZoom < 0.6)`, hoisted to top of pixel shader
2. `fwidth(tilePos.x)` — was inside `if (DebugBordersOff < 0.5)`, replaced with `pixelFootprint.x / TileSize.x` derived from the hoisted value

Without this fix, the 3x3 multisampling path (9 texture reads) likely executes for every pixel regardless of zoom level.

---

## Known Issues / Future Work

- **Pre-existing hitches** far from world origin — confirmed present before these changes. CPU-side, likely related to chunk loading or entity query iteration. Not caused by shader changes.
- **3x3 at extreme zoom-out (>4:1):** Grid only covers a fraction of the pixel footprint. Could need 4x4 or 5x5 if zoom range extends further, but user's target is 0.25 (4:1 with GlobalScale 2.0 = effective 2:1 texel ratio).
- **Two `adjustLightness` calls** (rgb→hsl→rgb round-trips) run unconditionally for base and accent colors even when the tile is a single solid color. Could be optimized by checking if `layerFactor` is near zero.
