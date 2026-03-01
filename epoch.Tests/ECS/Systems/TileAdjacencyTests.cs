using Arch.Core;
using epoch.ECS;
using Microsoft.Xna.Framework;

namespace epoch.Tests.ECS.Systems;

public class TileAdjacencyTests
{
    // ── CalculateBorderMasks (Tier 1 — pure function) ───────────────

    [Fact]
    public void CalculateBorderMasks_AllClosed()
    {
        // spaceMask = 0 → no open faces → all masks zero
        var (middle, top, bottom) = TileAdjacencySystem.CalculateBorderMasks(0);

        Assert.Equal(0, middle);
        Assert.Equal(0, top);
        Assert.Equal(0, bottom);
    }

    [Fact]
    public void CalculateBorderMasks_AllCardinalsOpen()
    {
        // N, E, S, W all open (bits 0-3) → middle = 0xF
        int spaceMask = 0b1111; // bits 0-3

        var (middle, top, bottom) = TileAdjacencySystem.CalculateBorderMasks(spaceMask);

        Assert.Equal(0xF, middle);
        // Above not open → top = 0
        Assert.Equal(0, top);
        // Below not open → bottom = 0
        Assert.Equal(0, bottom);
    }

    [Fact]
    public void CalculateBorderMasks_AboveOpen_CardinalsOpen()
    {
        // All 4 cardinals + above open → top should have all 4 bits set
        // Bits: N(0), E(1), S(2), W(3), Above(4)
        // Above-adjacent bits: N-Above(10), E-Above(14), S-Above(12), W-Above(16)
        // Top rule: set bit if cardinal is open OR above-adjacent is NOT open
        // With cardinals open → all top bits set regardless of above-adjacent
        int spaceMask = 0b11111; // bits 0-4

        var (middle, top, bottom) = TileAdjacencySystem.CalculateBorderMasks(spaceMask);

        Assert.Equal(0xF, middle);
        Assert.Equal(0xF, top);
        Assert.Equal(0, bottom); // below not open
    }

    [Fact]
    public void CalculateBorderMasks_BelowOpen_NorthOpen()
    {
        // Below(5) + North(0) open
        int spaceMask = (1 << 0) | (1 << 5);

        var (middle, top, bottom) = TileAdjacencySystem.CalculateBorderMasks(spaceMask);

        Assert.Equal(1, middle); // only north bit
        Assert.Equal(0, top);    // above not open
        Assert.Equal(1, bottom); // belowIsOpen → bottom = middle = north only
    }

    [Fact]
    public void CalculateBorderMasks_AboveOnly_NoCardinals()
    {
        // Only above is open (bit 4), no cardinals open
        // Top rule for each cardinal i: adjacentOpen=false, aboveAdjacentOpen depends on bits 10,14,12,16
        // None of those set → !aboveAdjacentOpen = true → top bit set for all 4
        int spaceMask = (1 << 4);

        var (middle, top, bottom) = TileAdjacencySystem.CalculateBorderMasks(spaceMask);

        Assert.Equal(0, middle);
        Assert.Equal(0xF, top); // all 4 top bits set because no above-adjacent neighbors
        Assert.Equal(0, bottom);
    }

    [Fact]
    public void CalculateBorderMasks_AboveOpen_AboveAdjacentBlocks()
    {
        // Above open (bit 4), no cardinals open, but all above-adjacent neighbors are open
        // Above-adjacent: N-Above(10), E-Above(14), S-Above(12), W-Above(16)
        // Top rule: adjacentOpen=false, aboveAdjacentOpen=true → !true = false → bit NOT set
        int spaceMask = (1 << 4) | (1 << 10) | (1 << 14) | (1 << 12) | (1 << 16);

        var (middle, top, bottom) = TileAdjacencySystem.CalculateBorderMasks(spaceMask);

        Assert.Equal(0, middle);
        Assert.Equal(0, top); // above-adjacent all open → no top borders
        Assert.Equal(0, bottom);
    }

    // ── CalculateSpaceMask (Tier 2 — needs Arch World + ChunkRegistry) ─

    [Fact]
    public void CalculateSpaceMask_EmptySlot_IsOpen()
    {
        // Empty slot (Entity.Null) in a loaded chunk should be treated as open space
        using var world = World.Create();
        var registry = new ChunkRegistry(world, 16, 32);

        // Center entity at (1,1,1) — solid block
        var blockEntity = world.Create(new Position { WorldCoordinate = new Vector3(1, 1, 1), Passable = false });
        registry.Register(new Vector3(1, 1, 1), blockEntity);

        // North of center (1, 0, 1) — no entity registered, but chunk exists from center entity
        // This empty slot should count as open space

        // Place solid blocks at all other cardinal neighbors to isolate the north check
        Vector3[] neighbors = [
            new(2, 1, 1), // East
            new(1, 2, 1), // South
            new(0, 1, 1), // West
            new(1, 1, 2), // Above
            new(1, 1, 0), // Below
        ];
        foreach (var n in neighbors)
        {
            var solid = world.Create(new Position { WorldCoordinate = n, Passable = false });
            registry.Register(n, solid);
        }

        int mask = TileAdjacencySystem.CalculateSpaceMask(new Vector3(1, 1, 1), registry);

        // North (bit 0) should be set — empty slot = open space
        Assert.True((mask & (1 << 0)) != 0, "North bit should be set (empty = open)");
        // East (bit 1) should NOT be set — solid block
        Assert.True((mask & (1 << 1)) == 0, "East bit should not be set");
    }
}
