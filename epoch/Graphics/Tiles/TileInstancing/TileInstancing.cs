/// <summary>
/// CREDIT: Adapted from Amrik19 - Monogame-Spritesheet-Instancing
/// MIT License
// Copyright (c) 2024 Amrik Jesse Wagner

// Permission is hereby granted, free of charge, to any person obtaining a copy
// of this software and associated documentation files (the "Software"), to deal
// in the Software without restriction, including without limitation the rights
// to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
// copies of the Software, and to permit persons to whom the Software is
// furnished to do so, subject to the following conditions:

// The above copyright notice and this permission notice shall be included in all
// copies or substantial portions of the Software.
/// </summary>
using System;
using System.Runtime.CompilerServices;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace epoch.Graphics.Tiles.TileInstancing;

/// <summary>
/// GPU-instanced batch renderer for tiles. Collects per-tile data (position, colors, borders)
/// between <see cref="Begin"/> and <see cref="End"/>, then issues a single
/// <c>DrawInstancedPrimitives</c> call. Auto-grows its instance buffer as needed.
/// Sorts back-to-front by depth before submitting.
/// </summary>
public class TileInstancing
{
    private GraphicsDevice graphicsDevice;
    private Effect shader;
    private VertexBuffer vertexBuffer;
    private IndexBuffer indexBuffer;
    private DynamicVertexBuffer dynamicInstancingBuffer;

    private readonly VertexBufferBinding[] vertexBufferBindings;

    private TileVertex[] instanceDataArray;
    private TileVertex[] sortedDataArray;
    private float[] sortDepths;
    private int[] sortIndices;
    private int instanceNumber;

    // Radix sort buffers
    private uint[] radixKeys;
    private int[] radixScratchIndices;
    private int[] histogramBuffer;

    private readonly Matrix identityMatrix = Matrix.Identity; // Used if there's no transform provided.

    // For throwing error
    bool beginCalled;

    private const int InitialCapacity = 1024;

    /// <summary>Number of tile instances submitted this frame.</summary>
    internal int InstanceCount => instanceNumber;

    /// <summary>Current CPU-side buffer capacity (doubles on resize).</summary>
    internal int BufferCapacity => instanceDataArray.Length;

    public TileInstancing(GraphicsDevice graphicsDevice)
    {
        // Like Spritebatch
        if (graphicsDevice == null)
        {
            throw new ArgumentNullException(
                "graphicsDevice",
                "The GraphicsDevice must not be null when creating new resources."
            );
        }

        this.graphicsDevice = graphicsDevice;

        vertexBufferBindings = new VertexBufferBinding[2];

        CreateBaseVertexAndIndexBuffer();

        instanceDataArray = new TileVertex[InitialCapacity];
        sortedDataArray = new TileVertex[InitialCapacity];
        sortDepths = new float[InitialCapacity];
        sortIndices = new int[InitialCapacity];
        radixKeys = new uint[InitialCapacity];
        radixScratchIndices = new int[InitialCapacity];
        histogramBuffer = new int[256 * 4];
    }

    /// <summary>
    /// Creates all required buffers
    /// </summary>
    private void CreateBaseVertexAndIndexBuffer()
    {
        // Define geometry for a single quad (2 triangles)
        VertexPositionTexture[] vertices = new VertexPositionTexture[]
        {
            new VertexPositionTexture(new Vector3(0f, 1f, 0), new Vector2(0, 1)), // Down Left
            new VertexPositionTexture(new Vector3(0f, 0f, 0), new Vector2(0, 0)), // Top Left
            new VertexPositionTexture(new Vector3(1f, 1f, 0), new Vector2(1, 1)), // Down Right
            new VertexPositionTexture(new Vector3(1f, 0f, 0), new Vector2(1, 0)), // Top Right
        };

        // VertexBuffer for the Single Quad
        // Defines the shape of the mesh (corners of the square)
        vertexBuffer = new VertexBuffer(
            graphicsDevice,
            typeof(VertexPositionTexture),
            vertices.Length,
            BufferUsage.WriteOnly
        );
        vertexBuffer.SetData(vertices);

        // Creates the Index-Array for 2 triangles (one quad together)
        // Defines the lines for the two triangles in the quad
        short[] indices = new short[] { 0, 1, 2, 2, 1, 3 };

        // Indexbuffer for the 2 triangles
        indexBuffer = new IndexBuffer(
            graphicsDevice,
            IndexElementSize.SixteenBits,
            indices.Length,
            BufferUsage.WriteOnly
        );
        indexBuffer.SetData(indices);

        // Dynamicinstancing Buffer
        // Holds the TileVertex data for each instance to draw
        // Defines the custom data per instance
        dynamicInstancingBuffer = new DynamicVertexBuffer(
            graphicsDevice,
            typeof(TileVertex),
            InitialCapacity,
            BufferUsage.WriteOnly
        );

        // Setup the bindings once
        vertexBufferBindings[0] = new VertexBufferBinding(vertexBuffer, 0, 0); // Quad geometry
        vertexBufferBindings[1] = new VertexBufferBinding(dynamicInstancingBuffer, 0, 1); // Per-instance data
    }

    /// <summary>
    /// Loads a new Shader
    /// </summary>
    /// <param name="spritesheetInstancingShader"></param>
    public void LoadShader(Effect shader)
    {
        if (shader == null)
        {
            throw new ArgumentNullException(
                "spritesheetInstancingShader",
                "The Spritesheet Instancing Shader cant be null"
            );
        }
        this.shader = shader;
    }

    /// <summary>
    /// Disposes the vertex and index buffers to free up GPU resources
    /// This should be called when this instance is no longer needed.
    /// </summary>
    public void Dispose()
    {
        vertexBuffer?.Dispose();
        indexBuffer?.Dispose();
        dynamicInstancingBuffer?.Dispose();

        vertexBuffer = null;
        indexBuffer = null;
        dynamicInstancingBuffer = null;
        instanceDataArray = null;
    }

    /// <summary>
    /// Returns the Size of the internal Instancing Array.
    /// Useful for debugging or performance checks.
    /// </summary>
    /// <returns></returns>
    public int InternalArraySize()
    {
        return instanceDataArray.Length;
    }

    /// <summary>
    /// Sets the Internal Array Size to a new to a specific amount.
    /// If the Size is the same as the current size the Array will not resize.
    /// Useful for preallocating space if the number of instances is known beforehand.
    /// </summary>
    /// <param name="newSize"></param>
    /// <exception cref="InvalidOperationException"></exception>
    public void SetInternalArraySizes(int newSize)
    {
        if (newSize <= 0)
        {
            throw new InvalidOperationException("Cannot set the internal Array under 1.");
        }

        if (beginCalled)
        {
            throw new InvalidOperationException("Cannot set the internal Array in a Drawcall.");
        }

        // Only set the Array to the new size if it really is a different one
        if (instanceDataArray.Length != newSize)
        {
            Array.Resize(ref instanceDataArray, newSize);
        }
    }

    /// <summary>
    /// Starts collecting the “drawcalls” in an array before sending them to the graphics card in a (Vetex)Instancing buffer.
    /// The internal instancing array will automatically grow as needed.
    /// </summary>
    /// <param name="blendState">AlphaBlend if empty</param>
    public void Begin(
        Effect effect = null,
        BlendState blendState = null,
        SamplerState samplerState = null,
        DepthStencilState depthStencilState = null,
        RasterizerState rasterizerState = null
    )
    {
        if (effect != null)
        {
            shader = effect;
        }
        else if (shader == null)
        {
            throw new InvalidOperationException(
                "A valid shader must be provided either during construction or in the Begin() method."
            );
        }

        // Like Spritebatch
        if (beginCalled)
        {
            throw new InvalidOperationException(
                "Begin cannot be called again until End has been successfully called."
            );
        }
        // For the End Method
        beginCalled = true;

        graphicsDevice.BlendState = blendState ?? BlendState.AlphaBlend; // Standard is AlphaBlend
        graphicsDevice.DepthStencilState = depthStencilState ?? DepthStencilState.None;
        graphicsDevice.SamplerStates[0] = samplerState ?? SamplerState.LinearClamp;
        graphicsDevice.RasterizerState = rasterizerState ?? RasterizerState.CullNone;

        // Reset the Instance Number / instanceDataArray will grow dynamic
        instanceNumber = 0;
    }

    /// <summary>
    /// Resizes the Array with 2x the Capacity
    /// </summary>
    private void ResizeTheInstancesArray()
    {
        int newLength = instanceDataArray.Length * 2;
        Array.Resize(ref instanceDataArray, newLength);
        Array.Resize(ref sortedDataArray, newLength);
        Array.Resize(ref sortDepths, newLength);
        Array.Resize(ref sortIndices, newLength);
        Array.Resize(ref radixKeys, newLength);
        Array.Resize(ref radixScratchIndices, newLength);
    }

    /// <summary>
    /// Adds a sprite or spritesheet element to the draw array for rendering.
    /// <para>
    /// The sprite is centered at its top left point and transformed based on the provided parameters.
    /// </para>
    /// </summary>
    /// <param name="position">The position of the sprite.</param>
    /// <param name="rectangle">The source rectangle from the spritesheet.</param>
    /// <param name="scale">The scale of the sprite. (x, y)</param>
    /// <param name="color">The color tint applied to the sprite.</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Draw(
        Vector2 position,
        float depth,
        float sortDepth,
        float scale,
        float rotation,
        float borderMask,
        float borderWidth,
        float layerDifference,
        Rectangle rectangle,
        Color background1Color,
        Color background2Color,
        Color baseColor,
        Color accentColor,
        Color borderColor
    )
    {
        if (instanceNumber >= instanceDataArray.Length)
        {
            ResizeTheInstancesArray();
        }

        ref TileVertex instance = ref instanceDataArray[instanceNumber];
        sortDepths[instanceNumber] = sortDepth;

        instance.Position = position;
        instance.Depth = depth;
        instance.Scale = scale;
        instance.Rotation = rotation;
        instance.BorderMask = borderMask;
        instance.BorderWidth = borderWidth;
        instance.LayerDifference = layerDifference;
        instance.RectangleXY = new Vector2(rectangle.X, rectangle.Y);
        instance.RectangleWH = new Vector2(rectangle.Width, rectangle.Height);
        instance.Background1Color = background1Color;
        instance.Background2Color = background2Color;
        instance.BaseColor = baseColor;
        instance.AccentColor = accentColor;
        instance.BorderColor = borderColor;

        instanceNumber++;
    }

    /// <summary>
    /// 4-pass 8-bit radix sort on depth. Sorts indices into sortedDataArray back-to-front.
    /// Non-negative floats have uint bit patterns that sort naturally; we bitwise-NOT
    /// them so that higher depth (further back) sorts first (descending order).
    /// Skips passes where all elements land in a single bucket.
    /// </summary>
    private void RadixSortByDepth(int count)
    {
        // 1. Clear histogram (256 buckets x 4 passes)
        Array.Clear(histogramBuffer, 0, 256 * 4);

        // 2. Build keys, init indices, build all 4 histograms in one pass
        for (int i = 0; i < count; i++)
        {
            uint key = BitConverter.SingleToUInt32Bits(sortDepths[i]);
            radixKeys[i] = key;
            sortIndices[i] = i;

            histogramBuffer[(key & 0xFF)]++;
            histogramBuffer[256 + ((key >> 8) & 0xFF)]++;
            histogramBuffer[512 + ((key >> 16) & 0xFF)]++;
            histogramBuffer[768 + ((key >> 24) & 0xFF)]++;
        }

        // We ping-pong between sortIndices and radixScratchIndices
        int[] src = sortIndices;
        int[] dst = radixScratchIndices;

        // 3-4. Prefix sum + scatter for each of 4 bytes
        for (int pass = 0; pass < 4; pass++)
        {
            int histOffset = pass * 256;
            int shift = pass * 8;

            // Check if this pass can be skipped (all in one bucket)
            bool skip = false;
            for (int b = 0; b < 256; b++)
            {
                if (histogramBuffer[histOffset + b] == count)
                {
                    skip = true;
                    break;
                }
            }

            if (skip)
                continue;

            // Exclusive prefix sum
            int sum = 0;
            for (int b = 0; b < 256; b++)
            {
                int val = histogramBuffer[histOffset + b];
                histogramBuffer[histOffset + b] = sum;
                sum += val;
            }

            // Scatter
            for (int i = 0; i < count; i++)
            {
                int idx = src[i];
                int bucket = (int)((radixKeys[idx] >> shift) & 0xFF);
                dst[histogramBuffer[histOffset + bucket]++] = idx;
            }

            // Swap src and dst
            (src, dst) = (dst, src);
        }

        // 5. Final scatter into sortedDataArray
        for (int i = 0; i < count; i++)
            sortedDataArray[i] = instanceDataArray[src[i]];
    }

    /// <summary>
    /// Adds the created Array from the draws methods together in a dynamic vertexbuffer(Instancingbuffer) for the single drawcall.
    /// <para>
    /// The textures are drawn in the order in which the <c>Draw()/DrawTopLeft()</c> methods were called.
    /// </para>
    /// </summary>
    public void End()
    {
        // Like Spritebatch
        if (!beginCalled)
        {
            throw new InvalidOperationException("Begin must be called before calling End.");
        }

        beginCalled = false;

        // Are there more instances than 0?
        if (instanceNumber < 1)
        {
            return;
        }
        // Without the Array there is no Draw call
        if (instanceDataArray == null)
        {
            return;
        }

        if (instanceNumber > 1)
        {
            RadixSortByDepth(instanceNumber);
        }
        else if (instanceNumber == 1)
        {
            sortedDataArray[0] = instanceDataArray[0];
        }

        // Sets the Instancingbuffer
        // Dispose the buffer from the last Frame if the (vetex)instancingbuffer has changed
        if (dynamicInstancingBuffer.VertexCount < instanceNumber)
        {
            dynamicInstancingBuffer?.Dispose();

            int newVertexCount = Math.Max(instanceNumber, dynamicInstancingBuffer.VertexCount * 2);

            dynamicInstancingBuffer = new DynamicVertexBuffer(
                graphicsDevice,
                typeof(TileVertex),
                newVertexCount,
                BufferUsage.WriteOnly
            );

            // Update the binding
            vertexBufferBindings[1] = new VertexBufferBinding(dynamicInstancingBuffer, 0, 1);
        }

        // Fills the (vertex)instancingbuffer
        dynamicInstancingBuffer.SetData(
            sortedDataArray,
            0,
            instanceNumber,
            SetDataOptions.Discard
        );

        // Binds the vertexBuffers
        graphicsDevice.SetVertexBuffers(vertexBufferBindings);

        // Indexbuffer
        graphicsDevice.Indices = indexBuffer;

        // Activates the shader
        shader.CurrentTechnique.Passes[0].Apply();

        // Draws the 2 triangles on the screen
        graphicsDevice.DrawInstancedPrimitives(
            PrimitiveType.TriangleList,
            0, // baseVertex
            0, // minVertexIndex
            2, // primitiveCount
            instanceNumber
        );
    }
}
