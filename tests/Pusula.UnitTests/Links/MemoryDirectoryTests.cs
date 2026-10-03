using Pusula.Links;
using Shouldly;
using Xunit;

namespace Pusula.UnitTests.Links;

public sealed class MemoryDirectoryTests
{
    [Theory]
    [InlineData("projects/p/memory/a.md", "projects/p/memory")]
    [InlineData("projects/-home-ai/memory/sub/dir/a.md", "projects/-home-ai/memory")]
    public void TryGetFor_FileInsideMemoryDirectory_ReturnsTheDirectory(string path, string expected)
    {
        MemoryDirectory.TryGetFor(path, StringComparison.Ordinal, out string? directory).ShouldBeTrue();

        directory.ShouldBe(expected);
    }

    [Theory]
    [InlineData("projects/p/memory")]
    [InlineData("projects/p/notes/a.md")]
    [InlineData("projects//memory/a.md")]
    [InlineData("other/p/memory/a.md")]
    [InlineData("memory/a.md")]
    [InlineData("a.md")]
    [InlineData("Projects/p/Memory/a.md")]
    public void TryGetFor_OtherPaths_ReturnsFalse(string path)
    {
        MemoryDirectory.TryGetFor(path, StringComparison.Ordinal, out string? directory).ShouldBeFalse();

        directory.ShouldBeNull();
    }

    [Fact]
    public void TryGetFor_DifferentCaseWithIgnoreCase_MatchesAndKeepsTheSpelling()
    {
        MemoryDirectory.TryGetFor("Projects/P/Memory/a.md", StringComparison.OrdinalIgnoreCase, out string? directory).ShouldBeTrue();

        directory.ShouldBe("Projects/P/Memory");
    }
}
