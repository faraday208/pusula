using Pusula.Links;
using Shouldly;
using Xunit;

namespace Pusula.UnitTests.Links;

public sealed class TryNormalizeTests
{
    [Theory]
    [InlineData("", "a/b.md", "a/b.md")]
    [InlineData("rules", "x.md", "rules/x.md")]
    [InlineData("rules", "../x.md", "x.md")]
    [InlineData("a/b", "../../c", "c")]
    [InlineData("a", "..", "")]
    [InlineData("", "", "")]
    [InlineData("a", "", "a")]
    [InlineData("a", ".", "a")]
    [InlineData("a", "./b/./c", "a/b/c")]
    [InlineData("a", "b//c", "a/b/c")]
    [InlineData("a", "b/", "a/b")]
    [InlineData("a", "/b", "a/b")]
    [InlineData("a", "b/../c", "a/c")]
    [InlineData("a", "b/..", "a")]
    [InlineData("", "a/b/../../c", "c")]
    [InlineData("a/../b", "c", "b/c")]
    [InlineData("a", "%2e%2e/b", "a/%2e%2e/b")]
    [InlineData("a", "..b/c", "a/..b/c")]
    [InlineData("a", "b..", "a/b..")]
    [InlineData("a", "...", "a/...")]
    [InlineData("we\\ird", "x", "we\\ird/x")]
    public void TryNormalize_PathInsideTheRoot_ReturnsTheNormalizedPath(string baseDirectory, string relative, string expected)
    {
        LinkResolver.TryNormalize(baseDirectory, relative, out string? normalized).ShouldBeTrue();

        normalized.ShouldBe(expected);
    }

    [Theory]
    [InlineData("", "..")]
    [InlineData("", "../x")]
    [InlineData("", "a/../..")]
    [InlineData("", "./../x")]
    [InlineData("a", "../..")]
    [InlineData("a", "../../x")]
    [InlineData("a/b", "../../../x")]
    [InlineData("..", "x")]
    [InlineData("a", "b\\c")]
    [InlineData("a", "..\\x")]
    [InlineData("a", "\\x")]
    [InlineData("a", "b\0c")]
    [InlineData("a", "\0")]
    public void TryNormalize_EscapeOrForbiddenCharacter_ReturnsFalse(string baseDirectory, string relative)
    {
        LinkResolver.TryNormalize(baseDirectory, relative, out string? normalized).ShouldBeFalse();

        normalized.ShouldBeNull();
    }
}
