using System.Xml.Linq;
using epoch.ECS;
using epoch.Utilities;
using Microsoft.Xna.Framework;

namespace epoch.Tests;

public class ParsingTests
{
    // ── ParseColor ──────────────────────────────────────────────────

    [Fact]
    public void ParseColor_RGB()
    {
        var color = Utils.ParseColor("255,0,0");
        Assert.Equal(new Color(255, 0, 0, 255), color);
    }

    [Fact]
    public void ParseColor_RGBA()
    {
        var color = Utils.ParseColor("255,0,0,128");
        Assert.Equal(new Color(255, 0, 0, 128), color);
    }

    [Fact]
    public void ParseColor_Named()
    {
        var color = Utils.ParseColor("White");
        Assert.Equal(Color.White, color);
    }

    [Fact]
    public void ParseColor_Invalid_Throws()
    {
        Assert.Throws<ArgumentException>(() => Utils.ParseColor("notacolor"));
    }

    // ── ParseVector ─────────────────────────────────────────────────

    [Fact]
    public void ParseVector2_Parses()
    {
        var v = Utils.ParseVector2("1.5,2.5");
        Assert.Equal(new Vector2(1.5f, 2.5f), v);
    }

    [Fact]
    public void ParseVector3_Parses()
    {
        var v = Utils.ParseVector3("1,2,3");
        Assert.Equal(new Vector3(1, 2, 3), v);
    }

    // ── ConvertValue ────────────────────────────────────────────────

    [Fact]
    public void ConvertValue_Int()
    {
        var result = Utils.ConvertValue("42", typeof(int));
        Assert.Equal(42, result);
    }

    [Fact]
    public void ConvertValue_String()
    {
        var result = Utils.ConvertValue("hello", typeof(string));
        Assert.Equal("hello", result);
    }

    [Fact]
    public void ConvertValue_Bool()
    {
        var result = Utils.ConvertValue("true", typeof(bool));
        Assert.Equal(true, result);
    }

    [Fact]
    public void ConvertValue_NullType_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => Utils.ConvertValue("42", null!));
    }

    // ── ParseComponentElement ───────────────────────────────────────

    [Fact]
    public void ParseComponentElement_Simple()
    {
        var xml = XElement.Parse(
            """<component component_name="Position" Passable="true" IsBlock="false" />"""
        );

        var def = EntityManager.ParseComponentElement(xml);

        Assert.Equal("Position", def.TypeName);
        Assert.Equal("true", def.Properties["Passable"]);
        Assert.Equal("false", def.Properties["IsBlock"]);
        Assert.Empty(def.SubCompositeParts);
    }

    [Fact]
    public void ParseComponentElement_WithSubparts()
    {
        var xml = XElement.Parse(
            """
            <component component_name="GraphicalTileList">
                <subparts>
                    <part component_name="GraphicalTile" TileId="1" Scale="1.0" />
                    <part component_name="GraphicalTile" TileId="2" Scale="0.5" />
                </subparts>
            </component>
            """
        );

        var def = EntityManager.ParseComponentElement(xml);

        Assert.Equal("GraphicalTileList", def.TypeName);
        Assert.Equal(2, def.SubCompositeParts.Count);
        Assert.Equal("GraphicalTile", def.SubCompositeParts[0].TypeName);
        Assert.Equal("1", def.SubCompositeParts[0].Properties["TileId"]);
        Assert.Equal("2", def.SubCompositeParts[1].Properties["TileId"]);
    }
}
