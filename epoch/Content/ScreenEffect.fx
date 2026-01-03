#if OPENGL
    #define SV_POSITION POSITION
    #define VS_SHADERMODEL vs_3_0
    #define PS_SHADERMODEL ps_3_0
#else
    #define VS_SHADERMODEL vs_4_0_level_9_1
    #define PS_SHADERMODEL ps_4_0_level_9_1
#endif

Texture2D SpriteTexture;
sampler2D SpriteTextureSampler = sampler_state
{
    Texture = <SpriteTexture>;
};

float Time; 

struct VertexShaderOutput
{
    float4 Position : SV_POSITION;
    float4 Color : COLOR0;
    float2 TextureCoordinates : TEXCOORD0;
};

float4 MainPS(VertexShaderOutput input) : COLOR
{
    // --- 0. Lens Distortion Setup ---
    
    // (-0.08) = Barrel (Standard CRT TV look)
    // (0.08)  = Pincushion (Arcade / Projection look)
    float distortionStrength = 0.06; 
    
    // 1. Center the UVs [0,1] -> [-0.5, 0.5]
    float2 centeredUV = input.TextureCoordinates - 0.5;
    
    // 2. Calculate distance from center
    float r2 = dot(centeredUV, centeredUV);
    
    // 3. Distortion formula
    float power = 1.0 + distortionStrength * r2;
    
    // 4. Distort and un-center back to [0, 1]
    float2 distortedUV = 0.5 + (centeredUV * power);

    // 5. Border Cutoff (The Bezel)
    if (distortedUV.x < 0.0 || distortedUV.x > 1.0 || distortedUV.y < 0.0 || distortedUV.y > 1.0)
    {
        return float4(0.0, 0.0, 0.0, 1.0);
    }

    // --- 1. Chromatic Aberration (Applied to Distorted UVs) ---
    float aberrationStrength = 0.002; 
    float2 aberrationOffset = (distortedUV - 0.5) * aberrationStrength;

    float r = tex2D(SpriteTextureSampler, distortedUV - aberrationOffset).r;
    float g = tex2D(SpriteTextureSampler, distortedUV).g;
    float b = tex2D(SpriteTextureSampler, distortedUV + aberrationOffset).b;
    
    float4 color = float4(r, g, b, tex2D(SpriteTextureSampler, distortedUV).a);

    // --- 1.5 VFD Glow / Bloom (New!) ---
    // We sample neighbors to create a "bleed" effect.
    // Adjust 'bloomSpread' for how far the glow reaches (0.004 is decent for 1080p).
    // Adjust 'bloomIntensity' for how "hot" the display looks.
    
    float bloomSpread = 0.0011; 
    float bloomIntensity = 1.2; 
    float bloomThreshold = 0.10;
    
    float4 glow = float4(0,0,0,0);
    float samples = 0;

// We define the 8 offsets manually for PS_3_0 compatibility
    float2 offsets[8];
    offsets[0] = float2(-1, -1); offsets[1] = float2(1, -1);
    offsets[2] = float2(-1, 1);  offsets[3] = float2(1, 1);
    offsets[4] = float2(-1, 0);  offsets[5] = float2(1, 0);
    offsets[6] = float2(0, -1);  offsets[7] = float2(0, 1);

    for(int i = 0; i < 8; i++)
    {
        // 1. Sample the neighbor
        float2 sampleCoord = distortedUV + (offsets[i] * bloomSpread);
        float4 neighbor = tex2D(SpriteTextureSampler, sampleCoord);
        
        // 2. Calculate Brightness (Luminance)
        // This formula matches how human eyes perceive brightness (Green is brightest)
        float luminance = dot(neighbor.rgb, float3(0.299, 0.587, 0.114));

        // 3. Apply Threshold
        // We subtract the threshold. If the result is negative, it becomes 0.
        // This creates a smooth ramp: 0.7 brightness = 0 glow, 1.0 brightness = 0.25 glow.
        float contribution = max(0.0, luminance - bloomThreshold);

        // 4. Accumulate
        glow += neighbor * contribution;
    }

    // Average isn't strictly necessary with the weight math, 
    // but dividing by 8 keeps the intensity controllable.
    glow /= 8.0;

    // Add the calculated glow to the final color
    color.rgb += glow.rgb * bloomIntensity;

    // --- 2. Scanline Effect ---
    float scanline = sin(distortedUV.y * 400.0) * 0.0025;
    color.rgb -= scanline;

    // --- 3. Film Grain Effect ---
    float nR = frac(sin(dot(distortedUV * Time, float2(12.9898, 78.233))) * 43758.5453);
    float nG = frac(sin(dot(distortedUV * Time + 13.0, float2(12.9898, 78.233))) * 43758.5453);
    float nB = frac(sin(dot(distortedUV * Time + 47.0, float2(12.9898, 78.233))) * 43758.5453);

    float3 noise = float3(nR, nG, nB);
    float monoNoise = (nR + nG + nB) / 3.0;

    float3 finalNoise = lerp(float3(monoNoise, monoNoise, monoNoise), noise, 1.0f);

    float grainIntensity = 0.23;
    float luminance = dot(color.rgb, float3(0.299, 0.587, 0.114));
    float luminanceMask = pow(1.0 - luminance, 5.0); 

    color.rgb += (finalNoise - 0.5) * (grainIntensity * luminanceMask);

    // --- 4. Vignette ---
    // float vignetteStrength = 1.5; 
    // float vignetteSize = 0.65;    
    // float dist = distance(distortedUV, float2(0.5, 0.5));
    // float vignette = 1.0 - smoothstep(vignetteSize, vignetteSize + 0.4, dist * vignetteStrength);
    // color.rgb *= vignette;

    return float4(saturate(color.rgb), color.a);
}

technique SpriteDrawing
{
    pass P0
    {
        PixelShader = compile PS_SHADERMODEL MainPS();
    }
};
