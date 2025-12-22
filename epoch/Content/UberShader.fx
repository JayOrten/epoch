#if OPENGL // Building for Mac/Linux/Android
	#define SV_POSITION POSITION // Older OpenGL uses POSITION instead of SV_POSITION, this replaces it
	#define VS_SHADERMODEL vs_3_0
	#define PS_SHADERMODEL ps_3_0
#else // Building for Windows (DirectX)
	#define VS_SHADERMODEL vs_4_0_level_9_1
	#define PS_SHADERMODEL ps_4_0_level_9_1
#endif

// --- GRID PARAMETERS ---
matrix WorldViewProjection;
float2 TextureSize;   // Total size of your spritesheet (e.g. 512, 512)
float2 TileSize;      // Size of one tile (e.g. 32, 32)
float2 ViewportSize;  // Size of the viewport in pixels
// float CameraZoom;
// float GameTime;
// float GrainIntensity;

Texture2D SpriteTexture;
sampler2D SpriteTextureSampler = sampler_state
{
	Texture = <SpriteTexture>;
};

struct VertexShaderInput
{
	float4 Position : POSITION0;
	float2 TextureCoordinates : TEXCOORD0;
	float4 SpriteColor : COLOR0;
    float4 BackgroundColor : TEXCOORD1;
    float4 BorderColor : TEXCOORD2;
    float BorderMask : TEXCOORD3;
    float BorderWidth : TEXCOORD4;
    float LayerDifference : TEXCOORD5;
};

struct VertexShaderOutput
{
    // The coordinate of the speific pixel on the actual monitor: scaled from the previous step.
	float4 Position : SV_POSITION; // Signals to the rasterizer that this is the final position
    // Normalized sprite sheet texture coordinates (0.0 to 1.0). UNSCALED
	float2 TextureCoordinates : TEXCOORD0;
	float4 SpriteColor : COLOR0;
    float4 BackgroundColor : TEXCOORD1;
    float4 BorderColor : TEXCOORD2; // YOU HAVE TO NORMALIZE (/255) BY YOURSELF
    float BorderMask : TEXCOORD3;
    float BorderWidth : TEXCOORD4;
    float LayerDifference: TEXCOORD5;
};

// --- VERTEX SHADER ---
VertexShaderOutput MainVS(in VertexShaderInput input)
{
    VertexShaderOutput output = (VertexShaderOutput)0; // Zero out the output struct

    // Input position is position of vertex in MODEL space
    // Position relative to the sprite's center.
    // WorldViewProjection transforms it to CLIP space for rendering.
    // It does so by applying World, View, and Projection matrices.
    float4 projectedPos = mul(input.Position, WorldViewProjection);

    // Camera snap to pixel grid:
    // We convert from Clip Space (-1.0 to 1.0) to Screen
    // Space (0 to ViewportSize), round to nearest pixel, then convert back to Clip Space.
    // Not sure how much of an effect this really has..
    float2 pixelScale = ViewportSize / 2.0;
    projectedPos.xy = round(projectedPos.xy * pixelScale) / pixelScale;

    output.Position = projectedPos;
    output.TextureCoordinates = input.TextureCoordinates;
    output.SpriteColor = input.SpriteColor;
    output.BackgroundColor = input.BackgroundColor;
    output.BorderColor = input.BorderColor;
    output.BorderMask = input.BorderMask;
    output.BorderWidth = input.BorderWidth;
    output.LayerDifference = input.LayerDifference;

    return output;
}

float4 MainPS(VertexShaderOutput input) : COLOR0
{
    // The shader iterates through every scaled pixel on the screen that the sprite covers, according to the transformation applied.

    // --- 1. CALCULATE LOCAL COORDINATES (0.0 to 1.0) ---
    // Convert UVs to pixel coordinates, then find where we are inside a single tile.
    // 'frac' returns just the decimal part (e.g., 5.05 becomes 0.05).
    // TextureCoordinates are in the space of the entire texture, not just the region we are interested in
    // TextureCoordinates go from 0.0 to 1.0 across the entire texture (UV space)
    float2 pixelPos = input.TextureCoordinates * TextureSize;
    // EXAMPLE: if textureCoordinates = (0.003, 0.003) and TextureSize = (112, 112)
    // pixelPos = (0.35, 0.35)
    float2 tilePos = pixelPos / TileSize;
    // EXAMPLE: If TileSize = (7, 7)
    // tilePos = (0.05, 0.05)
    float2 localCoord = frac(tilePos); // This gives us local percentage coordinates in the tile
    // EXAMPLE: localCoord = (0.05, 0.05)

    // // --- 2. BORDER LOGIC ---
    // // This converts the float BorderMask into an integer, effectively, by rounding it.
    float mask = floor(input.BorderMask + 0.5);
    
    // // -- Step A: Extract Bits --
    // // fmod : floating point modulus (remainder after division)
    // // step(a, b) : returns 0.0 if b < a, else 1.0
    // // We extract each bit from the mask (top, right, bottom, left)
    float bitTop    = step(1.0, fmod(mask, 2.0)); mask = floor(mask / 2.0);
    float bitRight  = step(1.0, fmod(mask, 2.0)); mask = floor(mask / 2.0);
    float bitBottom = step(1.0, fmod(mask, 2.0)); mask = floor(mask / 2.0);
    float bitLeft   = step(1.0, fmod(mask, 2.0));

    // // -- Step B: Check Position --
    // // Determine thickness based on camera zoom, to eliminate flickering
    // // This way, if you zoom out farther than the BorderWidth, it will start scaling up
    // // Ensure at least 1 screen pixel thick
    float uvPerPixel = fwidth(tilePos.x); // Approximate size of one texture pixel in UV space
    // float uvPerPixel = 1.0 / (CameraZoom * TileSize.x * 4); // Alternative calculation based on CameraZoom
    // EXAMPLE: If CameraZoom = 2.0 and TileSize.x = 7, uvPerPixel = 1.0 / (2.0 * 7 * 4) = 0.017

    // float fadeWidth  = uvPerPixel * 5.0; // This is more of a normal AA way to do it (scales properly?)
    // float fadeWidth = 0.01;
    // EXAMPLE: fadeWidth = 0.017 * 200.0 = 3.4

    float targetWidth = max(input.BorderWidth, uvPerPixel); // Ensure that border is at least 1 screen pixel thick
    // EXAMPLE: If BorderWidth = 0.1, targetWidth = max(0.1, 0.017) = 0.1

    // float startFade = max(0.1, targetWidth - fadeWidth);
    float startFade = uvPerPixel * 2;
    // EXAMPLE: startFade = max(0.0, 0.1 - 3.4) = max(0.0, -3.3) = 0.0
    float endFade = targetWidth;

    // EXAMPLE: distTop = 0.05
    // // Smooth step: if the distance from the border is less than startfade, it's 0.0,
    // // which make inTop 1.0 (fully visible).
    // // if the distance is greater than endFade, it's 1.0, making inTop 0.0 (not visible).
    // // if the distance is in between, it smoothly interpolates.
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

    // -- Step D: Apply Blend --
    // 1. Determine layer brightness for only sprite and border colors
    // Interpolate based on difference between player layer and sprite layer
    // If layerDifference is positive, interpolate to white, otherwise to black
    // If layerDifference > 0, targetColor = (1, 1, 1)
    // If layerDifference < 0, targetColor = (0, 0, 0)
    // We clamp so it doesn't become fully black
    float3 targetBrightness = clamp(step(0.0, input.LayerDifference), 0.1, 1.0).xxx;
    
    // Saturate clamps the value between 0.0 and 1.0
    float layerFactor = saturate(abs(input.LayerDifference) / 5.0); // Assuming max difference of 10 layers
    
    // 2. Blend sprite onto background
    float4 finalColor = input.BackgroundColor / 255.0;

    // Sample the Texture (using original UVs) and apply brightness modifier
    float4 sprite = tex2D(SpriteTextureSampler, input.TextureCoordinates) * input.SpriteColor;
    sprite.rgb = lerp(sprite.rgb, targetBrightness, layerFactor);
    
    // Blend Sprite onto Background
    // If this is a sprite pixel, it will overwrite the background based on its alpha
    finalColor.rgb = sprite.rgb * sprite.a + finalColor.rgb * (1.0 - sprite.a);
    finalColor.a = max(finalColor.a, sprite.a);

    // 3. Blend Border onto the result
    // Mix the border color with the background color based on the alpha of the border color
    // This way, you can have semi-transparent borders
    // Otherwise, because we are not actually drawing two things, you wouldn't see the background color
    float appliedAlpha = borderStrength * saturate(input.BorderColor.a);

    float borderColor = lerp(input.BorderColor.rgb / 255.0, targetBrightness, layerFactor);

    // Blend onto background
    finalColor.rgb = lerp(finalColor.rgb, borderColor, appliedAlpha);

    finalColor.a = max(finalColor.a, appliedAlpha);

    return finalColor;
    // return float4(frac(localCoord.x * 100), 0, 0, 1);
    // return float4(input.Depth, 0, 0, 1);
}

technique SpriteDrawing
{
	pass P0
	{
        VertexShader = compile VS_SHADERMODEL MainVS();
		PixelShader = compile PS_SHADERMODEL MainPS();
	}
};
