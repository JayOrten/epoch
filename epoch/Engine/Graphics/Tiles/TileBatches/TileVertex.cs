using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace epoch.Engine.Graphics.Tiles.TileBatches
{
    public struct TileVertex : IVertexType
    {
        public Vector3 Position;
        public Vector2 TextureCoordinate;
        public Color SpriteColor;
        public Color BackgroundColor;
        public Color BorderColor;
        public float BorderMask;
        public float BorderWidth;
        public float LayerDifference;

        public TileVertex(
            Vector3 position,
            Vector2 texCoord,
            Color spriteCol,
            Color bgCol,
            Color borderCol,
            float mask,
            float borderWidth,
            float layerDifference
        )
        {
            Position = position;
            TextureCoordinate = texCoord;
            SpriteColor = spriteCol;
            BackgroundColor = bgCol;
            BorderColor = borderCol;
            BorderMask = mask;
            BorderWidth = borderWidth;
            LayerDifference = layerDifference;
        }

        public static readonly VertexDeclaration VertexDeclaration = new VertexDeclaration(
            new VertexElement(0, VertexElementFormat.Vector3, VertexElementUsage.Position, 0),
            new VertexElement(
                12,
                VertexElementFormat.Vector2,
                VertexElementUsage.TextureCoordinate,
                0
            ),
            new VertexElement(20, VertexElementFormat.Color, VertexElementUsage.Color, 0),
            new VertexElement(
                24,
                VertexElementFormat.Color,
                VertexElementUsage.TextureCoordinate,
                1
            ), // BgColor
            new VertexElement(
                28,
                VertexElementFormat.Color,
                VertexElementUsage.TextureCoordinate,
                2
            ), // BorderColor
            new VertexElement(
                32,
                VertexElementFormat.Single,
                VertexElementUsage.TextureCoordinate,
                3
            ), // Mask packed in TexCoord1
            new VertexElement(
                36,
                VertexElementFormat.Single,
                VertexElementUsage.TextureCoordinate,
                4
            ), // BorderWidth packed in TexCoord2
            new VertexElement(
                40,
                VertexElementFormat.Single,
                VertexElementUsage.TextureCoordinate,
                5
            ) // LayerDifference packed in TexCoord3
        );

        VertexDeclaration IVertexType.VertexDeclaration => VertexDeclaration;
    }
}
