using epoch.Engine.Graphics.Tiles.TileInstancing;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace epoch.Engine.Graphics;

/// <summary>
/// Represents a rectangular region within a texture.
/// </summary>
public class TextureRegion
{
    /// <summary>
    /// Gets or Sets the source texture this texture region is part of.
    /// </summary>
    public Texture2D Texture { get; set; }

    /// <summary>
    /// Gets or Sets the source rectangle boundary of this texture region within the source texture.
    /// </summary>
    public Rectangle SourceRectangle { get; set; }

    /// <summary>
    /// Gets the width, in pixels, of this texture region.
    /// </summary>
    public int Width => SourceRectangle.Width;

    /// <summary>
    /// Gets the height, in pixels, of this texture region.
    /// </summary>
    public int Height => SourceRectangle.Height;

    /// <summary>
    /// Gets the top normalized texture coordinate of this region.
    /// </summary>
    public float TopTextureCoordinate => SourceRectangle.Top / (float)Texture.Height;

    /// <summary>
    /// Gets the bottom normalized texture coordinate of this region.
    /// </summary>
    public float BottomTextureCoordinate => SourceRectangle.Bottom / (float)Texture.Height;

    /// <summary>
    ///  Gets the left normalized texture coordinate of this region.
    /// </summary>
    public float LeftTextureCoordinate => SourceRectangle.Left / (float)Texture.Width;

    /// <summary>
    /// Gets the right normalized texture coordinate of this region.
    /// </summary>
    public float RightTextureCoordinate => SourceRectangle.Right / (float)Texture.Width;

    /// <summary>
    /// Creates a new texture region. Default constructor.
    /// </summary>
    public TextureRegion() { }

    /// <summary>
    /// Creates a new texture region using the specified source texture.
    /// </summary>
    /// <param name="texture">The texture to use as the source texture for this texture region.</param>
    /// <param name="x">The x-coordinate position of the upper-left corner of this texture region relative to the upper-left corner of the source texture.</param>
    /// <param name="y">The y-coordinate position of the upper-left corner of this texture region relative to the upper-left corner of the source texture.</param>
    /// <param name="width">The width, in pixels, of this texture region.</param>
    /// <param name="height">The height, in pixels, of this texture region.</param>
    public TextureRegion(Texture2D texture, int x, int y, int width, int height)
    {
        Texture = texture;
        SourceRectangle = new Rectangle(x, y, width, height);
    }
}
