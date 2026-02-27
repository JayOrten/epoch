#if OPENGL // Building for Mac/Linux/Android
	#define SV_POSITION POSITION // Older OpenGL uses POSITION instead of SV_POSITION, this replaces it
    #define SV_TARGET COLOR0 // Older OpenGL uses COLOR instead of SV_TARGET, this replaces it
	#define VS_SHADERMODEL vs_3_0
	#define PS_SHADERMODEL ps_3_0
#else // Building for Windows (DirectX)
	#define VS_SHADERMODEL vs_4_0_level_9_1
	#define PS_SHADERMODEL ps_4_0_level_9_1
#endif

// --- GRID PARAMETERS ---
float4x4 WorldViewProjection;
float2 TextureSize;   // Total size of your spritesheet (e.g. 512, 512)
float2 TileSize;      // Size of one tile (e.g. 32, 32)
float2 ViewportSize;  // Size of the viewport in pixels
float2 VanishingPoint; // World-pixel coords of vanishing point, set per-frame
float DepthStrength;   // Perspective warp strength (e.g. 0.06), set per-frame
float CameraZoom;
float DebugBordersOff; // 1.0 = skip border computation
// float GameTime;

Texture2D SpriteTexture;
sampler TextureSampler : register(s0)
{
    Texture = <SpriteTexture>;
    MinFilter = Linear;
    MagFilter = Linear;
    MipFilter = None;
    AddressU = Clamp;
    AddressV = Clamp;
};

struct VertexInput
{
    float4 V_Position : POSITION0;
    float2 V_uv : TEXCOORD0;
};

struct InstanceInput
{
    // UsageIndex 1: Vector4 (Position.x, Position.y, Depth, Scale)
    float4 I_TransformData : TEXCOORD1;

    // UsageIndex 2: Vector4 (Rotation, BorderMask, BorderWidth, LayerDiff)
    float4 I_PropData      : TEXCOORD2;

    // UsageIndex 3: Vector2 (RectangleXY)
    float2 I_RectXY        : TEXCOORD3;

    // UsageIndex 4: Vector2 (RectangleWH)
    float2 I_RectWH        : TEXCOORD4;

    // UsageIndices 5-9: Colors
    float4 I_Bg1Color      : TEXCOORD5;
    float4 I_Bg2Color      : TEXCOORD6;
    float4 I_BaseColor     : TEXCOORD7;
    float4 I_AccentColor   : TEXCOORD8;
    float4 I_BorderColor   : TEXCOORD9;
};

struct PixelInput
{
    // The coordinate of the speific pixel on the actual monitor: scaled from the previous step.
    float4 P_Position : SV_POSITION;
    // Normalized sprite sheet texture coordinates (0.0 to 1.0). UNSCALED
    float2 P_uv : TEXCOORD0;
    float P_BorderMask : TEXCOORD1;
    float P_BorderWidth : TEXCOORD2;
    float P_LayerDifference: TEXCOORD3;
    float P_Rotation : TEXCOORD9;
    float4 P_Background1Color : TEXCOORD4;
    float4 P_Background2Color : TEXCOORD5;
    float4 P_BaseColor : TEXCOORD6;
    float4 P_AccentColor : TEXCOORD7;
    float4 P_BorderColor : TEXCOORD8;
};

// --- HSL UTILITIES ---

float3 rgb2hsl(float3 c)
{
    float maxC = max(c.r, max(c.g, c.b));
    float minC = min(c.r, min(c.g, c.b));
    float l = (maxC + minC) * 0.5;
    float d = maxC - minC;

    // If max == min, achromatic (h = 0, s = 0)
    float s = 0.0;
    float h = 0.0;

    if (d > 0.0001)
    {
        s = (l > 0.5) ? d / (2.0 - maxC - minC) : d / (maxC + minC);

        if (maxC == c.r)
            h = (c.g - c.b) / d + (c.g < c.b ? 6.0 : 0.0);
        else if (maxC == c.g)
            h = (c.b - c.r) / d + 2.0;
        else
            h = (c.r - c.g) / d + 4.0;

        h /= 6.0;
    }

    return float3(h, s, l);
}

float hue2rgb(float p, float q, float t)
{
    if (t < 0.0) t += 1.0;
    if (t > 1.0) t -= 1.0;
    if (t < 1.0 / 6.0) return p + (q - p) * 6.0 * t;
    if (t < 1.0 / 2.0) return q;
    if (t < 2.0 / 3.0) return p + (q - p) * (2.0 / 3.0 - t) * 6.0;
    return p;
}

float3 hsl2rgb(float3 hsl)
{
    float h = hsl.x;
    float s = hsl.y;
    float l = hsl.z;

    if (s < 0.0001)
        return float3(l, l, l);

    float q = (l < 0.5) ? l * (1.0 + s) : l + s - l * s;
    float p = 2.0 * l - q;

    float r = hue2rgb(p, q, h + 1.0 / 3.0);
    float g = hue2rgb(p, q, h);
    float b = hue2rgb(p, q, h - 1.0 / 3.0);

    return float3(r, g, b);
}

// Adjust lightness of an RGB color by shifting its HSL lightness.
// factor: 0.0 = no change, 1.0 = full effect
// brighten: 1.0 = push lightness toward 1.0, 0.0 = push lightness toward minL
float3 adjustLightness(float3 rgb, float factor, float brighten)
{
    float minL = 0.2; // Floor for darkening — never fully black
    float3 hsl = rgb2hsl(rgb);
    float targetL = lerp(minL, 1.0, brighten);
    hsl.z = lerp(hsl.z, targetL, factor);
    return hsl2rgb(hsl);
}

// Rotation around the origin
// theta = Roatation in radians 2Pi (Pi = half rotation, 2Pi = full rotation)
float2x2 getRotationMatrix(float theta)
{
    float s = sin(theta);
    float c = cos(theta);
    return float2x2(c, s, -s, c);
    // Rotate a Vector
    //
    // [ a | b ]   [ e ]   [ ae + bf ]
    // [ c | d ] * [ f ] = [ ce + df ]
    //
}

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

PixelInput MainVS(VertexInput v, InstanceInput i)
{
    PixelInput output;

    // 1. UNPACK INSTANCE DATA
    float2 I_Position = i.I_TransformData.xy;
    float I_Depth = i.I_TransformData.z;
    float I_Scale = i.I_TransformData.w;

    float I_Rotation = radians(i.I_PropData.x);
    float I_BorderMask = i.I_PropData.y;
    float I_BorderWidth = i.I_PropData.z;
    float I_LayerDifference = i.I_PropData.w;

    float I_RectangleX = i.I_RectXY.x;
    float I_RectangleY = i.I_RectXY.y;
    float I_RectangleW = i.I_RectWH.x;
    float I_RectangleH = i.I_RectWH.y;

    // 2. POSITION TRANSFORMATION
    // A. Vertex Expansion - Scale the quad to sprite size
    float2 size = float2(I_RectangleW, I_RectangleH) * I_Scale;
    v.V_Position.xy *= size;

    // B. Rotation (around center)
    // Shift origin temporarily to center for rotation
    v.V_Position.xy -= size * 0.5;
    float2x2 matr = getRotationMatrix(I_Rotation);
    v.V_Position.xy = mul(v.V_Position.xy, matr);
    // Shift origin back to top-left
    v.V_Position.xy += size * 0.5;

    // C. Translation to world position
    v.V_Position.xy += I_Position;

    // C2. Perspective warp: scale position relative to vanishing point.
    // Uses tanh (via exp) to bound extreme depths — behaves like the linear model
    // near depth=0 but saturates rather than exploding or collapsing.
    // tanh(x) = (e^2x - 1) / (e^2x + 1), approximates x for small x.
    float depthInput = I_Depth * DepthStrength;
    float e2x = exp(2.0 * depthInput);
    float perspectiveScale = 1.0 + (e2x - 1.0) / (e2x + 1.0);
    v.V_Position.xy = VanishingPoint + (v.V_Position.xy - VanishingPoint) * perspectiveScale;

    // E. Depth Adjustment
    // v.V_Position.z = 0.0;
    // v.V_Position.z = I_Depth * 0.0001; // Scale down depth to avoid z-fighting

    // D. Projection to Clip Space
    // Input position is position of vertex in MODEL space
    // Position relative to the sprite's center.
    // WorldViewProjection transforms it to CLIP space for rendering.
    // It does so by applying World, View, and Projection matrices.
    v.V_Position = mul(v.V_Position, WorldViewProjection);

    // 3. UV Calculation

    // 1. Scale the 0..1 UV to the size of the sprite (in pixels)
    float2 pixelUV = v.V_uv * float2(I_RectangleW, I_RectangleH);

    // 2. Move that sized box to the correct X,Y position on the sheet
    pixelUV += float2(I_RectangleX, I_RectangleY);
    pixelUV /= TextureSize; // Normalize back to 0..1

    // Snap vertex positions to pixel grid to eliminate tile seam shimmering.
    // Adaptive resolution: as zoom decreases, snap to finer sub-pixel grids
    // so rounding error stays ~2% of tile size (prevents diamonds at low zoom).
    // At zoom 1.0: snap to whole pixels. At 0.5: half-pixels. At 0.25: quarter-pixels.
    float2 pixelScale = ViewportSize / 2.0;
    float snapMul = clamp(1.0 / CameraZoom, 1.0, 4.0);
    float2 snapScale = pixelScale * snapMul;
    v.V_Position.xy = round(v.V_Position.xy * snapScale) / snapScale;

    output.P_Position = v.V_Position;
    output.P_uv = pixelUV;
    output.P_BorderMask = I_BorderMask;
    output.P_BorderWidth = I_BorderWidth;
    output.P_LayerDifference = I_LayerDifference;
    output.P_Rotation = I_Rotation;
    output.P_Background1Color = i.I_Bg1Color;
    output.P_Background2Color = i.I_Bg2Color;
    output.P_BaseColor = i.I_BaseColor;
    output.P_AccentColor = i.I_AccentColor;
    output.P_BorderColor = i.I_BorderColor;

    return output;
}

float4 MainPS(PixelInput input) : SV_TARGET
{
    // --- 1. CALCULATE LOCAL COORDINATES (0.0 to 1.0) ---
    // Convert UVs to texel coordinates, then find where we are inside a single tile.
    float2 texelPos = input.P_uv * TextureSize;

    float2 tilePos = texelPos / TileSize;

    // Compute pixel footprint unconditionally — derivative instructions inside
    // branches can cause GPU drivers to flatten the branch (executing both paths).
    float2 pixelFootprint = fwidth(texelPos);

    // 'frac' returns just the decimal part (e.g., 5.05 becomes 0.05).
    float2 localCoord = frac(tilePos); // This gives us local percentage coordinates in the tile

    // --- 2. BORDER LOGIC ---
    float borderStrength = 0;
    float3 borderResult = 0;
    float appliedAlpha = 0;

    if (DebugBordersOff < 0.5)
    {
        // The border mask is in world space (N/E/S/W), but localCoord is in texture space
        // which rotates with the tile's autotile rotation. To make them match, we un-rotate
        // localCoord back to world space so border edges line up with the correct screen sides.
        float2 borderCoord = localCoord - 0.5;
        float2x2 invRotation = getRotationMatrix(input.P_Rotation);
        borderCoord = mul(borderCoord, invRotation) + 0.5;

        float mask = floor(input.P_BorderMask + 0.5);

        // Extract bits (top, right, bottom, left)
        float bitTop    = step(1.0, fmod(mask, 2.0)); mask = floor(mask / 2.0);
        float bitRight  = step(1.0, fmod(mask, 2.0)); mask = floor(mask / 2.0);
        float bitBottom = step(1.0, fmod(mask, 2.0)); mask = floor(mask / 2.0);
        float bitLeft   = step(1.0, fmod(mask, 2.0));

        // Determine thickness — minimum 1.5 screen pixels for stability
        // uvPerPixel derived from pixelFootprint (computed outside all branches)
        float uvPerPixel = pixelFootprint.x / TileSize.x;
        float targetWidth = max(input.P_BorderWidth, uvPerPixel * 1.5);

        // Analytical box filter for stable border coverage
        float inTop    = saturate((targetWidth - borderCoord.y)         / uvPerPixel + 0.5);
        float inRight  = saturate((targetWidth - (1.0 - borderCoord.x)) / uvPerPixel + 0.5);
        float inBottom = saturate((targetWidth - (1.0 - borderCoord.y)) / uvPerPixel + 0.5);
        float inLeft   = saturate((targetWidth - borderCoord.x)         / uvPerPixel + 0.5);

        borderStrength = max(bitTop * inTop, max(bitRight * inRight, max(bitBottom * inBottom, bitLeft * inLeft)));

        float4 borderCol = input.P_BorderColor / 255.0;
        appliedAlpha = borderStrength * saturate(borderCol.a);

        float layerFactor = saturate(abs(input.P_LayerDifference) / 10.0);
        float isBrightening = step(0.0, input.P_LayerDifference);
        borderResult = adjustLightness(borderCol.rgb, layerFactor, isBrightening) * appliedAlpha;
    }

    // -- Tile Colors --
    float layerFactor = saturate(abs(input.P_LayerDifference) / 10.0);
    float isBrightening = step(0.0, input.P_LayerDifference);

    float4 bg1Col = input.P_Background1Color / 255.0;
    bg1Col.rgb *= bg1Col.a;

    float4 bg2Col = input.P_Background2Color / 255.0;
    bg2Col.rgb *= bg2Col.a;

    float4 baseCol = input.P_BaseColor / 255.0;
    baseCol.rgb *= baseCol.a;

    float4 accentCol = input.P_AccentColor / 255.0;
    accentCol.rgb *= accentCol.a;

    float3 baseResult   = adjustLightness(baseCol.rgb,   layerFactor, isBrightening);
    float3 accentResult = adjustLightness(accentCol.rgb, layerFactor, isBrightening);

    float4 resolvedBg1 = bg1Col;
    float4 resolvedBg2 = bg2Col;
    float4 resolvedBase = float4(baseResult, baseCol.a);
    float4 resolvedAccent = float4(accentResult, accentCol.a);

    // Post-classification multisampling: classify each texel individually
    // (color code → display color), then average the resolved colors.
    // Branch on CameraZoom (uniform) so the GPU skips the 3x3 path when
    // zoomed in — all 9 samples would hit the same texel anyway.
    // NOTE: pixelFootprint (fwidth) is computed above, outside this branch,
    // to prevent GPU drivers from flattening it due to derivative instructions.
    float4 spriteLayer;

    if (CameraZoom < 0.6)
    {
        // Zoomed out: 3x3 grid across the pixel footprint
        float2 gridStep = pixelFootprint / 3.0;

        #define MSAMPLE(ox, oy) classifyTexel((floor(texelPos + float2(ox, oy) * gridStep) + 0.5) / TextureSize, resolvedBg1, resolvedBg2, resolvedBase, resolvedAccent)

        spriteLayer = (
            MSAMPLE(-1, -1) + MSAMPLE(0, -1) + MSAMPLE(1, -1) +
            MSAMPLE(-1,  0) + MSAMPLE(0,  0) + MSAMPLE(1,  0) +
            MSAMPLE(-1,  1) + MSAMPLE(0,  1) + MSAMPLE(1,  1)
        ) / 9.0;

        #undef MSAMPLE
    }
    else
    {
        // Zoomed in: single sample at texel center
        float2 centerUV = (floor(texelPos) + 0.5) / TextureSize;
        spriteLayer = classifyTexel(centerUV, resolvedBg1, resolvedBg2, resolvedBase, resolvedAccent);
    }

    // 7. Blend Border onto the result (appliedAlpha is 0 when borders are off)
    float4 finalColor;
    finalColor.rgb = borderResult + spriteLayer.rgb * (1.0 - appliedAlpha);
    finalColor.a = appliedAlpha + spriteLayer.a * (1.0 - appliedAlpha);

    return finalColor;
}

technique SpriteDrawing
{
	pass P0
	{
        VertexShader = compile VS_SHADERMODEL MainVS();
		PixelShader = compile PS_SHADERMODEL MainPS();
	}
};
