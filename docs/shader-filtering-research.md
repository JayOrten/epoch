# Shader Filtering Research: Pixel Swimming & Zoom-Out Artifacts

Research and implementation notes from investigating sub-pixel rendering artifacts. All code changes described here have been reverted — this document exists as a reference for future work.

## Problems Investigated

### 1. Pixel Swimming (Sub-Pixel Camera Movement)

When the camera moves at sub-pixel positions, screen pixels flip between adjacent texels frame-to-frame. Thin lines appear/disappear and edges visibly crawl.

The existing vertex snapping in the vertex shader (`round(clipPos * pixelScale) / pixelScale`) prevents tile *seam* shimmering but doesn't fix swimming *within* tiles.

### 2. Sprite Degradation When Zoomed Out

When zooming out, sprites lose detail, shimmer, and eventually disappear entirely. Multiple texels map to one screen pixel with no proper downsampling strategy.

### 3. Tile Settling After Camera Stops

After the camera stops moving, tiles visibly slide into their final positions over several frames. Caused by the draw position lerp in `DrawSystem.cs` — it smooths *all* position changes, not just z-level transitions.

### 4. Diamond Shimmering When Zoomed Out

Tiles deform into diamond shapes and flicker at certain zoom levels. Caused by vertex snapping rounding each vertex independently when tiles are small on screen.

---

## Solutions Attempted

### Fix: Texel-Edge Bilinear Clamping (Pixel Swimming)

**Status: Working. Reverted for bundled re-implementation.**

**Files:** `RenderShader.fx`, `DrawSystem.cs`

Changed sampler from `Point` to `Linear` (both in shader declaration and C# `SamplerState`), then added a UV snapping function that controls where interpolation happens:

```hlsl
float2 uvPixelArt(float2 uv, float2 textureSize)
{
    float2 pixel = uv * textureSize;       // UV to texel space
    float2 seam = floor(pixel + 0.5);      // nearest texel boundary
    float2 dudv = fwidth(pixel);           // texels per screen pixel
    pixel = seam + clamp((pixel - seam) / dudv, -0.5, 0.5);
    return pixel / textureSize;
}
```

**How it works:**
- Converts UV to texel-space coordinates
- Finds nearest texel boundary (seam)
- `fwidth(pixel)` measures how many texels one screen pixel spans
- Offsets from the seam are clamped to half a screen pixel
- **Inside a texel:** UV snaps to texel center (crisp, no swimming)
- **At a texel boundary:** allows bilinear blend over exactly 1 screen pixel (stable transition)

The C# side must also change `SamplerState.PointClamp` to `SamplerState.LinearClamp` because MonoGame's `SamplerStates[0]` overrides the shader-declared sampler for `register(s0)`.

---

### Fix: Classify-Then-Blend Multisampling (Zoom-Out)

**Status: Working. Reverted for bundled re-implementation.**

**Files:** `RenderShader.fx`

#### Failed approach: Mipmaps

Enabled `GenerateMipmaps=True` in `Content.mgcb` and `MipFilter = Linear` in the shader. Sprites disappeared at distance.

**Why it failed:** The color mask system stores codes (magenta, cyan, white, yellow) not actual colors. Mipmaps average texels *before* the shader classifies them. Example: two magenta `(1,0,1)` + two black `(0,0,0)` average to `(0.5, 0, 0.5)`. With `step(0.5)` that's barely magenta. One more dark pixel and it drops below threshold — no mask matches, pixel vanishes. Mipmaps fundamentally break classify-after-sample pipelines.

#### Working approach: Post-classification multisampling

Instead of averaging raw texels then classifying, classify each texel individually then average the resolved display colors.

```hlsl
float4 classifyTexel(float2 uv, float4 bg1, float4 bg2, float4 baseC, float4 accentC)
{
    float4 s = tex2Dlod(TextureSampler, float4(uv, 0, 0));
    float r = step(0.5, s.r);
    float g = step(0.5, s.g);
    float b = step(0.5, s.b);

    float4 result = 0;
    result += r * (1.0 - g) * b * bg1;           // Magenta (1,0,1)
    result += (1.0 - r) * g * b * bg2;           // Cyan (0,1,1)
    result += r * g * b * baseC;                  // White (1,1,1)
    result += r * g * (1.0 - b) * accentC;       // Yellow (1,1,0)
    return result;
}
```

**Dual-path sampling** based on `fwidth`:

- **Zoomed in** (footprint <= 1 texel/pixel): Single sample with `uvPixelArt` snapping
- **Zoomed out** (footprint > 1 texel/pixel): 2x2 grid of texel centers across the pixel footprint, each classified independently, then averaged
- **Transition**: `lerp` between paths at footprint 1.0-2.0

The 2x2 grid positions snap to texel centers via `floor(x) + 0.5` so the Linear sampler returns exact texel values (no interpolation at centers).

**Cost:** 5 texture samples per pixel total (1 single + 4 multi). Both paths always execute (no dynamic branching in ps_3_0 due to `fwidth` requiring uniform control flow).

**Known limitation:** At extreme zoom-out (>4:1), the 2x2 grid only covers a fraction of the pixel's texel footprint. Could expand to 3x3 (9 samples) if needed.

---

### Fix: Distance-Based Interpolation Threshold (Tile Settling)

**Status: Working. Reverted for bundled re-implementation.**

**Files:** `DrawSystem.cs`

The original code lerps every tile's draw position toward its target every frame:
```csharp
float lerpFactor = 1 - (float)Math.Pow(0.0001, deltaTime); // ~0.9999 at 60fps
Vector2.Lerp(currentPos, targetPos, lerpFactor);
```

This asymptotically approaches the target, causing visible settling. The lerp exists for smooth z-level transitions but fires on every camera movement.

**First attempt (failed):** Track `PreviousDepth` per tile, only lerp when depth changes. Too binary — catches one frame of depth change, then snaps the next frame. Z-transitions looked jarring.

**Working approach:** Check distance between current and target position. If far (mid z-transition), lerp. If close (normal camera movement), snap:
```csharp
float distance = Vector2.DistanceSquared(currentPos, targetPos);
if (distance > 2.0f) // ~1.4px threshold
    lerp toward target;
else
    snap to target;
```

This naturally handles the full duration of z-transitions while eliminating settling from camera movement.

---

### Fix: Zoom-Based Vertex Snap Fade (Diamond Shimmering)

**Status: Partially working — reduced but not eliminated. Reverted for further investigation.**

**Files:** `RenderShader.fx`

The vertex shader rounds each vertex to the nearest screen pixel:
```hlsl
v.V_Position.xy = round(v.V_Position.xy * pixelScale) / pixelScale;
```

At 1:1 zoom with 24px tiles, a 0.5px rounding error is invisible. At 4:1 zoom-out with 6px tiles, that same error is an 8% size fluctuation — enough to create diamond deformations and size flickering.

**Approach:** Fade out snapping based on `CameraZoom` (uniform for all tiles):
```hlsl
float snapStrength = saturate((CameraZoom - 0.3) / 0.7);
float2 snappedPos = round(v.V_Position.xy * pixelScale) / pixelScale;
v.V_Position.xy = lerp(v.V_Position.xy, snappedPos, snapStrength);
```

Using `CameraZoom` instead of per-tile screen size is important — perspective scaling causes adjacent tiles to have slightly different sizes, which means different snap strengths, which means inconsistent seams (the original diamond artifact source).

**Still investigating:** Shimmering was reduced but not eliminated. Possible remaining causes:
- The snap fade transition zone may still have partial rounding inconsistencies
- Could try snapping the tile *origin* uniformly and offsetting vertices from that
- Could try disabling snapping entirely below a zoom threshold (hard cutoff instead of fade)
- The shimmering may have an additional cause beyond vertex snapping

---

## Other Additions

### `adjustSaturation` Function

Added parallel to `adjustLightness` — shifts HSL saturation while preserving hue and lightness:
```hlsl
float3 adjustSaturation(float3 rgb, float factor, float saturate_)
{
    float minS = 0.1;
    float3 hsl = rgb2hsl(rgb);
    float targetS = lerp(minS, 1.0, saturate_);
    hsl.y = lerp(hsl.y, targetS, factor);
    return hsl2rgb(hsl);
}
```

---

## Summary of Required Changes for Re-Implementation

| File | Change |
|------|--------|
| `RenderShader.fx` | Sampler `Point` → `Linear`; add `uvPixelArt()`, `classifyTexel()`, `adjustSaturation()`; replace sampling steps 4-6 with dual-path classify-then-blend; vertex snap fade based on `CameraZoom`; uncomment `CameraZoom` parameter |
| `DrawSystem.cs` | `SamplerState.PointClamp` → `SamplerState.LinearClamp`; distance-based interpolation threshold |
| `Content.mgcb` | No change needed (mipmaps do NOT work with color mask system) |
