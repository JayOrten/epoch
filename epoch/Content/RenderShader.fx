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
// float CameraZoom;
// float GameTime;

Texture2D SpriteTexture;
sampler TextureSampler : register(s0)
{
    Texture = <SpriteTexture>;
    MinFilter = Point;
    MagFilter = Point;
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
    float4 P_Background1Color : TEXCOORD4;
    float4 P_Background2Color : TEXCOORD5;
    float4 P_BaseColor : TEXCOORD6;
    float4 P_AccentColor : TEXCOORD7;
    float4 P_BorderColor : TEXCOORD8;
};

// Rotation around the origin
// theta = Roatation in radians 2Pi (Pi = half rotation, 2Pi = full rotation)
float2x2 getRotationMatrix(float theta)
{    
    float s = sin(theta);
    float c = cos(theta);
    return float2x2(c, -s, s, c);
    // Rotate a Vector
    //
    // [ a | b ]   [ e ]   [ ae + bf ]
    // [ c | d ] * [ f ] = [ ce + df ]
    //
}

PixelInput MainVS(VertexInput v, InstanceInput i)
{
    PixelInput output;

    // 1. UNPACK INSTANCE DATA
    float2 I_Position = i.I_TransformData.xy;
    float I_Depth = i.I_TransformData.z;
    float I_Scale = i.I_TransformData.w;

    float I_Rotation = i.I_PropData.x;
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

    // Camera snap to pixel grid:
    // We convert from Clip Space (-1.0 to 1.0) to Screen
    // Space (0 to ViewportSize), round to nearest pixel, then convert back to Clip Space.
    // This eliminates border shimmering.
    float2 pixelScale = ViewportSize / 2.0;
    v.V_Position.xy = round(v.V_Position.xy * pixelScale) / pixelScale;

    output.P_Position = v.V_Position;
    output.P_uv = pixelUV;
    output.P_BorderMask = I_BorderMask;
    output.P_BorderWidth = I_BorderWidth;
    output.P_LayerDifference = I_LayerDifference;
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
    // Convert UVs to pixel coordinates, then find where we are inside a single tile.
    // P_uv go from 0.0 to 1.0 across the entire texture
    float2 pixelPos = input.P_uv * TextureSize;

    float2 tilePos = pixelPos / TileSize;

    // 'frac' returns just the decimal part (e.g., 5.05 becomes 0.05).
    float2 localCoord = frac(tilePos); // This gives us local percentage coordinates in the tile

    // --- 2. BORDER LOGIC ---
    // This converts the float BorderMask into an integer, effectively, by rounding it.
    float mask = floor(input.P_BorderMask + 0.5);
    
    // -- Step A: Extract Bits --
    // fmod : floating point modulus (remainder after division)
    // step(a, b) : returns 0.0 if b < a, else 1.0
    // We extract each bit from the mask (top, right, bottom, left)
    float bitTop    = step(1.0, fmod(mask, 2.0)); mask = floor(mask / 2.0);
    float bitRight  = step(1.0, fmod(mask, 2.0)); mask = floor(mask / 2.0);
    float bitBottom = step(1.0, fmod(mask, 2.0)); mask = floor(mask / 2.0);
    float bitLeft   = step(1.0, fmod(mask, 2.0));

    // -- Step B: Check Position --
    // Determine thickness based on camera zoom, to eliminate flickering
    // This way, if you zoom out farther than the BorderWidth, it will start scaling up
    float uvPerPixel = fwidth(tilePos.x); // Approximate size of one texture pixel in UV space

    float targetWidth = max(input.P_BorderWidth, uvPerPixel); // Ensure that border is at least 1 screen pixel thick

    float startFade = max(0.0, targetWidth - uvPerPixel * 3.0);
    float endFade = targetWidth;

    // Smooth step: if the distance from the border is less than startfade, it's 0.0,
    // which make inTop 1.0 (fully visible).
    // if the distance is greater than endFade, it's 1.0, making inTop 0.0 (not visible).
    // if the distance is in between, it smoothly interpolates.
    float distTop = localCoord.y;
    float inTop = 1.0 - smoothstep(startFade, endFade, distTop);

    float distRight = 1.0 - localCoord.x;
    float inRight = 1.0 - smoothstep(startFade, endFade, distRight);

    float distBottom = 1.0 - localCoord.y;
    float inBottom = 1.0 - smoothstep(startFade, endFade, distBottom);

    float distLeft = localCoord.x;
    float inLeft = 1.0 - smoothstep(startFade, endFade, distLeft);

    // -- Step C: Combine --
    // Calculate the strength of each side individually.
    // bitTop is 0.0 or 1.0 (Is the border turned on?)
    // inTop is 0.0 to 1.0 (How visible is the border here?)
    float topStrength    = bitTop * inTop;       
    float rightStrength  = bitRight * inRight;
    float bottomStrength = bitBottom * inBottom;
    float leftStrength   = bitLeft * inLeft;

    // Find the maximum strength at this pixel.
    // If we are in a corner where Top=0.8 and Left=0.8, max keeps it at 0.8 (it doesn't add up to 1.6).
    float borderStrength = max(topStrength, max(rightStrength, max(bottomStrength, leftStrength)));

    // -- Step D: Apply Colors --
    // 1. Determine layer brightness for only sprite and border colors
    // Interpolate based on difference between player layer and sprite layer
    // If layerDifference is positive, interpolate to white, otherwise to black
    // If layerDifference > 0, targetColor = (1, 1, 1)
    // If layerDifference < 0, targetColor = (0, 0, 0)
    // We clamp so it doesn't become fully black
    float layerFactor = saturate(abs(input.P_LayerDifference) / 5.0);
    float isBrightening = step(0.0, input.P_LayerDifference); // 1.0 if Near, 0.0 if Far
    
    // 2. Prepare input colors
    float4 bg1Col = input.P_Background1Color / 255.0;
    bg1Col.rgb *= bg1Col.a;

    float4 bg2Col = input.P_Background2Color / 255.0;
    bg2Col.rgb *= bg2Col.a;

    float4 baseCol = input.P_BaseColor / 255.0;
    baseCol.rgb *= baseCol.a;

    float4 accentCol = input.P_AccentColor / 255.0;
    accentCol.rgb *= accentCol.a;

    float4 borderCol = input.P_BorderColor / 255.0;
    float appliedAlpha = borderStrength * saturate(borderCol.a);

    float3 fogColor = float3(0.1, 0.1, 0.1);

    // 3. Prepare Brightness Versions 
    // -- Base Color --
    float3 baseFar  = lerp(baseCol.rgb, fogColor * baseCol.a, layerFactor);
    float3 baseNear = baseCol.rgb * (1.0 + layerFactor); // Scale up to 2x brightness
    float3 baseResult = lerp(baseFar, baseNear, isBrightening);

    // -- Accent Color --
    float3 accentFar  = lerp(accentCol.rgb, fogColor * accentCol.a, layerFactor);
    float3 accentNear = accentCol.rgb * (1.0 + layerFactor);
    float3 accentResult = lerp(accentFar, accentNear, isBrightening);

    // -- Border Color --
    float3 borderFar  = lerp(borderCol.rgb, fogColor, layerFactor);
    float3 borderNear = borderCol.rgb * (1.0 + layerFactor);
    float3 borderResult = lerp(borderFar, borderNear, isBrightening);

    // Apply alpha to border result
    borderResult *= appliedAlpha;

    // 4. Sample the Texture (using original UVs, already premultiplied)
    float4 spritePixel = tex2D(TextureSampler, input.P_uv);

    // 5. Create Masks
    // We use step(0.5, value) which returns 1.0 if value >= 0.5, else 0.0.
    // This snaps the texture colors to pure 0 or 1 to avoid fuzziness.
    float r = step(0.5, spritePixel.r);
    float g = step(0.5, spritePixel.g);
    float b = step(0.5, spritePixel.b);

    // Calculate which color this pixel "is" (Results will be 1.0 or 0.0)
    float isBg1 = r * (1.0 - g) * b;      // Magenta (1,0,1)
    float isBg2 = (1.0 - r) * g * b;      // Cyan (0,1,1)
    float isBase = r * g * b;             // White (1,1,1)
    float isAccent = r * g * (1.0 - b);   // Yellow (1,1,0)

    // 6. Combine Colors
    // Multiply each color by its mask and sum them up.
    // Since only one mask will be 1.0 at a time, the others add 0.0.
    float4 spriteLayer = 0;
    spriteLayer += isBg1 * bg1Col;
    spriteLayer += isBg2 * bg2Col;
    spriteLayer += isBase * float4(baseResult, baseCol.a);
    spriteLayer += isAccent * float4(accentResult, accentCol.a);

    // 7. Blend Border onto the result

    // D. Blend Border Over Sprite (Standard Premultiplied Blend)
    // Formula: Result = Source + Dest * (1 - SourceAlpha)
    float4 finalColor;
    
    // RGB Blend
    finalColor.rgb = borderResult + spriteLayer.rgb * (1.0 - appliedAlpha);
    
    // Alpha Blend
    // This effectively replaces your old 'max' logic with mathematically correct blending
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
