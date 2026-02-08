using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace epoch.Graphics.Tiles.TileInstancing
{
    /// <summary>
    /// Per-instance vertex data sent to the GPU for tile instancing (68 bytes).
    /// Packs transform, border props, source rect, and 5 colors into texture coordinate slots.
    /// Layout must match the HLSL shader's input semantics exactly.
    /// </summary>
    public struct TileVertex : IVertexType
    {
        // --- Group 1: Transform (Slot 1) ---
        public Vector2 Position; // 8 bytes
        public float Depth; // 4 bytes
        public float Scale; // 4 bytes

        // --- Group 2: Props (Slot 2) ---
        public float Rotation; // 4 bytes
        public float BorderMask; // 4 bytes
        public float BorderWidth; // 4 bytes
        public float LayerDifference; // 4 bytes

        // --- Group 3: Rects (Slot 3) ---
        public Vector2 RectangleXY; // 4 bytes
        public Vector2 RectangleWH; // 4 bytes

        // --- Group 4: Colors (Slots 4-8) ---
        public Color Background1Color;
        public Color Background2Color;
        public Color BaseColor;
        public Color AccentColor;
        public Color BorderColor;

        public TileVertex(
            Vector2 position,
            float depth,
            float scale,
            float rotation,
            float borderMask,
            float borderWidth,
            float layerDifference,
            Vector2 rectangleXY,
            Vector2 rectangleWH,
            Color background1Color,
            Color background2Color,
            Color baseColor,
            Color accentColor,
            Color borderColor
        )
        {
            Position = position;
            Depth = depth;
            Scale = scale;
            Rotation = rotation;
            BorderMask = borderMask;
            BorderWidth = borderWidth;
            LayerDifference = layerDifference;
            RectangleXY = rectangleXY;
            RectangleWH = rectangleWH;
            Background1Color = background1Color;
            Background2Color = background2Color;
            BaseColor = baseColor;
            AccentColor = accentColor;
            BorderColor = borderColor;
        }

        public static readonly VertexDeclaration VertexDeclaration = new VertexDeclaration(
            // 1. Position(2) + Depth(1) + Scale(1) = 16 bytes (Vector4)
            new VertexElement(
                0,
                VertexElementFormat.Vector4,
                VertexElementUsage.TextureCoordinate,
                1
            ),
            // 2. Rotation(1) + BorderMask(1) + BorderWidth(1) + LayerDifference(1) = 16 bytes (Vector4)
            // (Offset: 16)
            new VertexElement(
                16,
                VertexElementFormat.Vector4,
                VertexElementUsage.TextureCoordinate,
                2
            ),
            // 3. RectangleXY = 8 bytes (Vector2)
            // (Offset: 32)
            new VertexElement(
                32,
                VertexElementFormat.Vector2,
                VertexElementUsage.TextureCoordinate,
                3
            ),
            // 4. RectangleWH = 8 bytes (Vector2)
            // (Offset: 32 + 8 = 40)
            new VertexElement(
                40,
                VertexElementFormat.Vector2,
                VertexElementUsage.TextureCoordinate,
                4
            ),
            // 5. The 5 Colors (4 bytes each)

            // Background1 (Offset: 48)
            new VertexElement(
                48,
                VertexElementFormat.Color,
                VertexElementUsage.TextureCoordinate,
                5
            ),
            // Background2 (Offset: 52)
            new VertexElement(
                52,
                VertexElementFormat.Color,
                VertexElementUsage.TextureCoordinate,
                6
            ),
            // BaseColor (Offset: 56)
            new VertexElement(
                56,
                VertexElementFormat.Color,
                VertexElementUsage.TextureCoordinate,
                7
            ),
            // AccentColor (Offset: 60)
            new VertexElement(
                60,
                VertexElementFormat.Color,
                VertexElementUsage.TextureCoordinate,
                8
            ),
            // BorderColor (Offset: 64)
            new VertexElement(
                64,
                VertexElementFormat.Color,
                VertexElementUsage.TextureCoordinate,
                9
            )
        );

        VertexDeclaration IVertexType.VertexDeclaration => VertexDeclaration;
    }
}
