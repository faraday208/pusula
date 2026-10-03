using Pusula.Startup;
using Shouldly;
using Xunit;

namespace Pusula.UnitTests.Startup;

public sealed class PusulaOptionsTests
{
    private static readonly string Home = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "pusula-home"));

    [Fact]
    public void ExpandHome_Tilde_IsTheHomeDirectory() =>
        PusulaOptions.ExpandHome("~", Home).ShouldBe(Home);

    [Theory]
    [InlineData("~/.claude", ".claude")]
    [InlineData("~/a/b", "a/b")]
    [InlineData("~\\a", "a")]
    [InlineData("~/", "")]
    public void ExpandHome_TildeSlash_IsRelativeToTheHomeDirectory(string path, string expectedRelative) =>
        PusulaOptions.ExpandHome(path, Home).ShouldBe(Path.Join(Home, expectedRelative));

    [Theory]
    [InlineData("/etc/claude")]
    [InlineData("relative/dir")]
    [InlineData("~other/dir")]
    [InlineData("a~/b")]
    [InlineData("")]
    public void ExpandHome_AnythingElse_IsLeftAlone(string path) =>
        PusulaOptions.ExpandHome(path, Home).ShouldBe(path);

    [Fact]
    public void ExpandHome_TildeWithoutAHomeDirectory_Throws()
    {
        Should.Throw<InvalidOperationException>(() => PusulaOptions.ExpandHome("~/x", string.Empty));
        Should.Throw<InvalidOperationException>(() => PusulaOptions.ExpandHome("~", string.Empty));
        PusulaOptions.ExpandHome("/x", string.Empty).ShouldBe("/x");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void ResolveRoot_NothingConfigured_IsTheClaudeFolderInTheHomeDirectory(string? root) =>
        PusulaOptions.ResolveRoot(root, Home).ShouldBe(Path.Join(Home, ".claude"));

    [Fact]
    public void ResolveRoot_ConfiguredRoot_IsExpandedTrimmedAndMadeAbsolute()
    {
        string absolute = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "some", "..", "other"));

        PusulaOptions.ResolveRoot("~/cfg", Home).ShouldBe(Path.Join(Home, "cfg"));
        PusulaOptions.ResolveRoot($"  {absolute}  ", Home).ShouldBe(absolute);
        Path.IsPathFullyQualified(PusulaOptions.ResolveRoot("relative", Home)).ShouldBeTrue();
    }

    // Changing the sources from another machine has to be asked for.
    [Fact]
    public void AllowRemoteEdit_NothingConfigured_IsOff() =>
        new PusulaOptions().AllowRemoteEdit.ShouldBeFalse();

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ; ;")]
    public void ParseAllowedHosts_NothingConfigured_IsEmpty(string? value) =>
        new PusulaOptions { AllowedHosts = value }.ParseAllowedHosts().ShouldBeEmpty();

    [Theory]
    [InlineData("a", new[] { "a" })]
    [InlineData("a;b", new[] { "a", "b" })]
    [InlineData(" a ; ;b; ", new[] { "a", "b" })]
    [InlineData("pusula.example.com;testhost", new[] { "pusula.example.com", "testhost" })]
    public void ParseAllowedHosts_SemicolonSeparatedList_IsSplitAndTrimmed(string value, string[] expected) =>
        new PusulaOptions { AllowedHosts = value }.ParseAllowedHosts().ShouldBe(expected);
}
