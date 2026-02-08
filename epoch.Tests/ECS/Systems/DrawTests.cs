using epoch.ECS;
using Microsoft.Xna.Framework;

namespace epoch.Tests.ECS.Systems;

public class DrawTests
{
    private const float TileWidth = 16f;
    private const float TileHeight = 16f;
    private const float GlobalScale = 1f;
    private const float DepthStrength = 0.03f;
    private static readonly Vector2 VanishingPoint = new(400, 300);

    [Fact]
    public void ComputeTileTransform_ZeroDepth()
    {
        // Tile at same Z as player → perspectiveScale = 1.0
        var (position, scale) = DrawSystem.ComputeTileTransform(
            worldCoordinate: new Vector3(5, 5, 3),
            tileScale: 1f,
            depthOffset: 0f,
            playerZLevel: 3f,
            tileWidth: TileWidth,
            tileHeight: TileHeight,
            globalScale: GlobalScale,
            depthStrength: DepthStrength,
            vanishingPoint: VanishingPoint
        );

        // depth = 0 → perspectiveScale = 1.0 → no VP offset
        Assert.Equal(1.0f, scale);
        Assert.Equal(new Vector2(5 * TileWidth, 5 * TileHeight), position);
    }

    [Fact]
    public void ComputeTileTransform_PositiveDepth_ScalesUp()
    {
        // Tile above player → depth > 0 → scale > 1
        var (_, scale) = DrawSystem.ComputeTileTransform(
            worldCoordinate: new Vector3(5, 5, 5),
            tileScale: 1f,
            depthOffset: 0f,
            playerZLevel: 3f,
            tileWidth: TileWidth,
            tileHeight: TileHeight,
            globalScale: GlobalScale,
            depthStrength: DepthStrength,
            vanishingPoint: VanishingPoint
        );

        // depth = 2 → perspectiveScale = 1 + 2*0.03 = 1.06
        Assert.True(scale > 1.0f);
        Assert.Equal(1.06f, scale, 0.001f);
    }

    [Fact]
    public void ComputeTileTransform_NegativeDepth_ScalesDown()
    {
        // Tile below player → depth < 0 → scale < 1
        var (_, scale) = DrawSystem.ComputeTileTransform(
            worldCoordinate: new Vector3(5, 5, 1),
            tileScale: 1f,
            depthOffset: 0f,
            playerZLevel: 3f,
            tileWidth: TileWidth,
            tileHeight: TileHeight,
            globalScale: GlobalScale,
            depthStrength: DepthStrength,
            vanishingPoint: VanishingPoint
        );

        // depth = -2 → perspectiveScale = 1 + (-2)*0.03 = 0.94
        Assert.True(scale < 1.0f);
        Assert.Equal(0.94f, scale, 0.001f);
    }

    [Fact]
    public void ComputeTileTransform_DepthOffset_Applies()
    {
        // depthOffset shifts the effective depth
        var (_, scaleNoOffset) = DrawSystem.ComputeTileTransform(
            worldCoordinate: new Vector3(5, 5, 3),
            tileScale: 1f,
            depthOffset: 0f,
            playerZLevel: 3f,
            tileWidth: TileWidth,
            tileHeight: TileHeight,
            globalScale: GlobalScale,
            depthStrength: DepthStrength,
            vanishingPoint: VanishingPoint
        );

        var (_, scaleWithOffset) = DrawSystem.ComputeTileTransform(
            worldCoordinate: new Vector3(5, 5, 3),
            tileScale: 1f,
            depthOffset: 1f,
            playerZLevel: 3f,
            tileWidth: TileWidth,
            tileHeight: TileHeight,
            globalScale: GlobalScale,
            depthStrength: DepthStrength,
            vanishingPoint: VanishingPoint
        );

        Assert.True(scaleWithOffset > scaleNoOffset);
    }
}
