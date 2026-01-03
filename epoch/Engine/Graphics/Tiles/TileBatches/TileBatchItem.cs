using System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace epoch.Engine.Graphics.Tiles.TileBatches
{
    public class TileBatchItem : IComparable<TileBatchItem>
    {
        public Texture2D Texture;
        public float SortKey;

        public TileVertex vertexTL;
        public TileVertex vertexTR;
        public TileVertex vertexBL;
        public TileVertex vertexBR;

        public TileBatchItem()
        {
            vertexTL = new TileVertex();
            vertexTR = new TileVertex();
            vertexBL = new TileVertex();
            vertexBR = new TileVertex();
        }

        public void Set(
            float x,
            float y,
            float dx,
            float dy,
            float w,
            float h,
            float sin,
            float cos,
            Color spriteColor,
            Vector2 texCoordTL,
            Vector2 texCoordBR,
            float depth,
            Color bgColor,
            Color borderColor,
            float borderMask,
            float borderWidth,
            float layerDifference
        )
        {
            // --- 1. HALF-PIXEL UV OFFSET ---
            // Fixes texture bleeding from neighboring sprites in the atlas
            float halfPixelX = 0.5f / Texture.Width;
            float halfPixelY = 0.5f / Texture.Height;

            texCoordTL.X += halfPixelX;
            texCoordTL.Y += halfPixelY;
            texCoordBR.X -= halfPixelX;
            texCoordBR.Y -= halfPixelY;

            // --- 2. FAT VERTEX FIX ---
            // We add a tiny amount to the width/height of the geometry (but not UVs).
            // This causes the tiles to overlap slightly, sealing the floating-point cracks.
            float epsilon = 0.01f;

            // TODO, Should we be just assigning the Depth Value to Z?
            // According to http://blogs.msdn.com/b/shawnhar/archive/2011/01/12/spritebatch-billboards-in-a-3d-world.aspx
            // We do.

            vertexTL.Position.X = x + dx * cos - dy * sin;
            vertexTL.Position.Y = y + dx * sin + dy * cos;
            vertexTL.Position.Z = depth;
            vertexTL.SpriteColor = spriteColor;
            vertexTL.TextureCoordinate.X = texCoordTL.X;
            vertexTL.TextureCoordinate.Y = texCoordTL.Y;
            vertexTL.BackgroundColor = bgColor;
            vertexTL.BorderColor = borderColor;
            vertexTL.BorderMask = borderMask;
            vertexTL.BorderWidth = borderWidth;
            vertexTL.LayerDifference = layerDifference;

            // Expand Width by epsilon
            vertexTR.Position.X = x + (dx + w + epsilon) * cos - dy * sin;
            vertexTR.Position.Y = y + (dx + w + epsilon) * sin + dy * cos;
            vertexTR.Position.Z = depth;
            vertexTR.SpriteColor = spriteColor;
            vertexTR.TextureCoordinate.X = texCoordBR.X;
            vertexTR.TextureCoordinate.Y = texCoordTL.Y;
            vertexTR.BackgroundColor = bgColor;
            vertexTR.BorderColor = borderColor;
            vertexTR.BorderMask = borderMask;
            vertexTR.BorderWidth = borderWidth;
            vertexTR.LayerDifference = layerDifference;

            // Expand Height by epsilon
            vertexBL.Position.X = x + dx * cos - (dy + h + epsilon) * sin;
            vertexBL.Position.Y = y + dx * sin + (dy + h + epsilon) * cos;
            vertexBL.Position.Z = depth;
            vertexBL.SpriteColor = spriteColor;
            vertexBL.TextureCoordinate.X = texCoordTL.X;
            vertexBL.TextureCoordinate.Y = texCoordBR.Y;
            vertexBL.BackgroundColor = bgColor;
            vertexBL.BorderColor = borderColor;
            vertexBL.BorderMask = borderMask;
            vertexBL.BorderWidth = borderWidth;
            vertexBL.LayerDifference = layerDifference;

            // Expand Width and Height by epsilon
            vertexBR.Position.X = x + (dx + w + epsilon) * cos - (dy + h + epsilon) * sin;
            vertexBR.Position.Y = y + (dx + w + epsilon) * sin + (dy + h + epsilon) * cos;
            vertexBR.Position.Z = depth;
            vertexBR.SpriteColor = spriteColor;
            vertexBR.TextureCoordinate.X = texCoordBR.X;
            vertexBR.TextureCoordinate.Y = texCoordBR.Y;
            vertexBR.BackgroundColor = bgColor;
            vertexBR.BorderColor = borderColor;
            vertexBR.BorderMask = borderMask;
            vertexBR.BorderWidth = borderWidth;
            vertexBR.LayerDifference = layerDifference;
        }

        public void Set(
            float x,
            float y,
            float w,
            float h,
            Color spriteColor,
            Vector2 texCoordTL,
            Vector2 texCoordBR,
            float depth,
            Color bgColor,
            Color borderColor,
            float borderMask,
            float borderWidth,
            float layerDifference
        )
        {
            // --- 1. HALF-PIXEL UV OFFSET ---
            // float halfPixelX = 0.5f / Texture.Width;
            // float halfPixelY = 0.5f / Texture.Height;

            // texCoordTL.X += halfPixelX;
            // texCoordTL.Y += halfPixelY;
            // texCoordBR.X -= halfPixelX;
            // texCoordBR.Y -= halfPixelY;

            // --- 2. FAT VERTEX FIX ---
            float epsilon = 0.01f;

            vertexTL.Position.X = x;
            vertexTL.Position.Y = y;
            vertexTL.Position.Z = depth;
            vertexTL.SpriteColor = spriteColor;
            vertexTL.TextureCoordinate.X = texCoordTL.X;
            vertexTL.TextureCoordinate.Y = texCoordTL.Y;
            vertexTL.BackgroundColor = bgColor;
            vertexTL.BorderColor = borderColor;
            vertexTL.BorderMask = borderMask;
            vertexTL.BorderWidth = borderWidth;
            vertexTL.LayerDifference = layerDifference;

            vertexTR.Position.X = x + w + epsilon; // Expand Width
            vertexTR.Position.Y = y;
            vertexTR.Position.Z = depth;
            vertexTR.SpriteColor = spriteColor;
            vertexTR.TextureCoordinate.X = texCoordBR.X;
            vertexTR.TextureCoordinate.Y = texCoordTL.Y;
            vertexTR.BackgroundColor = bgColor;
            vertexTR.BorderColor = borderColor;
            vertexTR.BorderMask = borderMask;
            vertexTR.BorderWidth = borderWidth;
            vertexTR.LayerDifference = layerDifference;

            vertexBL.Position.X = x;
            vertexBL.Position.Y = y + h + epsilon; // Expand Height
            vertexBL.Position.Z = depth;
            vertexBL.SpriteColor = spriteColor;
            vertexBL.TextureCoordinate.X = texCoordTL.X;
            vertexBL.TextureCoordinate.Y = texCoordBR.Y;
            vertexBL.BackgroundColor = bgColor;
            vertexBL.BorderColor = borderColor;
            vertexBL.BorderMask = borderMask;
            vertexBL.BorderWidth = borderWidth;
            vertexBL.LayerDifference = layerDifference;

            vertexBR.Position.X = x + w + epsilon; // Expand Width
            vertexBR.Position.Y = y + h + epsilon; // Expand Height
            vertexBR.Position.Z = depth;
            vertexBR.SpriteColor = spriteColor;
            vertexBR.TextureCoordinate.X = texCoordBR.X;
            vertexBR.TextureCoordinate.Y = texCoordBR.Y;
            vertexBR.BackgroundColor = bgColor;
            vertexBR.BorderColor = borderColor;
            vertexBR.BorderMask = borderMask;
            vertexBR.BorderWidth = borderWidth;
            vertexBR.LayerDifference = layerDifference;
        }

        #region Implement IComparable
        public int CompareTo(TileBatchItem other)
        {
            return SortKey.CompareTo(other.SortKey);
        }
        #endregion
    }
}
