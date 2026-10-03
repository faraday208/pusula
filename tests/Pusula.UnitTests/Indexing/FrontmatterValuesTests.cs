using System.Text.Json.Nodes;
using Pusula.Indexing;
using Shouldly;
using Xunit;

namespace Pusula.UnitTests.Indexing;

public sealed class FrontmatterValuesTests
{
    private static readonly JsonObject Sample = new()
    {
        ["name"] = "foo",
        ["flag"] = "TRUE",
        ["off"] = "false",
        ["list"] = new JsonArray("a"),
        ["map"] = new JsonObject(),
    };

    [Fact]
    public void GetString_StringValue_ReturnsIt() =>
        FrontmatterValues.GetString(Sample, "name").ShouldBe("foo");

    [Theory]
    [InlineData("list")]
    [InlineData("map")]
    [InlineData("missing")]
    public void GetString_NonStringOrMissingValue_ReturnsNull(string key) =>
        FrontmatterValues.GetString(Sample, key).ShouldBeNull();

    [Fact]
    public void GetString_NullFrontmatter_ReturnsNull() =>
        FrontmatterValues.GetString(null, "name").ShouldBeNull();

    [Theory]
    [InlineData("name", true)]
    [InlineData("list", true)]
    [InlineData("missing", false)]
    public void Has_ReportsKeyPresenceRegardlessOfValue(string key, bool expected) =>
        FrontmatterValues.Has(Sample, key).ShouldBe(expected);

    [Fact]
    public void Has_NullFrontmatter_ReturnsFalse() =>
        FrontmatterValues.Has(null, "name").ShouldBeFalse();

    [Theory]
    [InlineData("flag", true)]
    [InlineData("off", false)]
    [InlineData("name", false)]
    [InlineData("list", false)]
    [InlineData("missing", false)]
    public void IsTrue_OnlyTheStringTrueIgnoringCase_IsTrue(string key, bool expected) =>
        FrontmatterValues.IsTrue(Sample, key).ShouldBe(expected);
}
