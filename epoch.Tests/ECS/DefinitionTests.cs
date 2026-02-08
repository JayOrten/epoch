using epoch.ECS;

namespace epoch.Tests.ECS;

public class DefinitionTests
{
    // ── Clone ───────────────────────────────────────────────────────

    [Fact]
    public void Clone_IsIndependent()
    {
        var original = new EntityDefinition("Enemy");
        original.Add("Position", "X", "10");

        var clone = original.Clone();
        clone.Add("Position", "X", "99");

        Assert.Equal("10", original["Position"]["X"]);
        Assert.Equal("99", clone["Position"]["X"]);
    }

    [Fact]
    public void Clone_DeepCopiesProperties()
    {
        var original = new EntityDefinition("Block");
        original.Add("Position", "Passable", "false");
        original.Add("GraphicalTile", "TileId", "5");

        var clone = original.Clone();

        // Mutate clone's properties
        clone["Position"].Properties["Passable"] = "true";

        Assert.Equal("false", original["Position"]["Passable"]);
        Assert.Equal("5", clone["GraphicalTile"]["TileId"]);
    }

    [Fact]
    public void Clone_CopiesTypeName()
    {
        var original = new EntityDefinition("Player");
        var clone = original.Clone();

        Assert.Equal("Player", clone.TypeName);
    }

    // ── Merge ───────────────────────────────────────────────────────

    [Fact]
    public void Merge_AddsNewComponents()
    {
        var baseEntity = new EntityDefinition("Base");
        baseEntity.Add("Position", "X", "0");

        var overlay = new EntityDefinition();
        overlay.Add("Movement", "MoveDelay", "0.5");

        baseEntity.Merge(overlay);

        Assert.True(baseEntity.Components.ContainsKey("Movement"));
        Assert.Equal("0.5", baseEntity["Movement"]["MoveDelay"]);
    }

    [Fact]
    public void Merge_OverwritesExistingProperties()
    {
        var baseEntity = new EntityDefinition("Base");
        baseEntity.Add("Position", "X", "0");
        baseEntity.Add("Position", "Passable", "true");

        var overlay = new EntityDefinition();
        overlay.Add("Position", "X", "42");

        baseEntity.Merge(overlay);

        Assert.Equal("42", baseEntity["Position"]["X"]);
        // Untouched properties survive
        Assert.Equal("true", baseEntity["Position"]["Passable"]);
    }

    [Fact]
    public void Merge_MutatesThis()
    {
        var entity = new EntityDefinition("Test");
        var overlay = new EntityDefinition();
        overlay.Add("Health", "Max", "100");

        var returned = entity.Merge(overlay);

        Assert.Same(entity, returned);
        Assert.True(entity.Components.ContainsKey("Health"));
    }
}
