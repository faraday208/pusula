using Pusula.Indexing;
using Pusula.Sources;
using Shouldly;
using Xunit;

namespace Pusula.UnitTests.Sources;

public sealed class ClaudeScopeTests
{
    // A home directory as the platform writes one; nothing is created there.
    private static readonly string Home = Path.GetFullPath(Path.Join(Path.GetTempPath(), "home-of-someone"));

    private static SourceDefinition Claude(string path) => new("claude", "Claude", path, SourceProfile.Claude);

    [Fact]
    public void ScopeOf_ClaudeFolderInTheHomeDirectory_IsGlobal() =>
        SourceEndpoints.ScopeOf(Claude(Path.Join(Home, ".claude")), Home).ShouldBe(ClaudeScope.Global);

    [Fact]
    public void ScopeOf_ClaudeFolderInTheHomeDirectoryWrittenWithASeparatorAtTheEnd_IsGlobal()
    {
        SourceEndpoints.ScopeOf(Claude(Path.Join(Home, ".claude") + Path.DirectorySeparatorChar), Home).ShouldBe(ClaudeScope.Global);
        SourceEndpoints.ScopeOf(Claude(Path.Join(Home, ".claude")), Home + Path.DirectorySeparatorChar).ShouldBe(ClaudeScope.Global);
    }

    [Fact]
    public void ScopeOf_ClaudeFolderOfAProject_IsProject()
    {
        SourceEndpoints.ScopeOf(Claude(Path.Join(Home, "Workspace", "shop", ".claude")), Home).ShouldBe(ClaudeScope.Project);
        SourceEndpoints.ScopeOf(Claude(Path.Join(Home, "Workspace", "shop", ".claude") + Path.DirectorySeparatorChar), Home).ShouldBe(ClaudeScope.Project);
    }

    [Fact]
    public void ScopeOf_ClaudeFolderOutsideTheHomeDirectory_IsProject() =>
        SourceEndpoints.ScopeOf(Claude(Path.GetFullPath(Path.Join(Path.GetTempPath(), "srv", "api", ".claude"))), Home).ShouldBe(ClaudeScope.Project);

    // A copy of a configuration, or the demo in samples/: a Claude Code folder, but neither the global one nor a project's.
    [Fact]
    public void ScopeOf_ClaudeFolderWithAnotherName_HasNone() =>
        SourceEndpoints.ScopeOf(Claude(Path.Join(Home, "Workspace", "pusula", "samples", "claude")), Home).ShouldBeNull();

    [Theory]
    [InlineData(SourceProfile.Vault)]
    [InlineData(SourceProfile.Markdown)]
    public void ScopeOf_SourceOfNotes_HasNoneWhateverItsFolderIsCalled(SourceProfile profile) =>
        SourceEndpoints.ScopeOf(new SourceDefinition("notes", "Notes", Path.Join(Home, ".claude"), profile), Home).ShouldBeNull();

    [Fact]
    public void ScopeOf_HomeDirectoryThatIsNotKnown_HasNone()
    {
        SourceEndpoints.ScopeOf(Claude(Path.Join(Home, ".claude")), string.Empty).ShouldBeNull();
        SourceEndpoints.ScopeOf(Claude(Path.Join(Home, "Workspace", "shop", ".claude")), string.Empty).ShouldBeNull();
    }

    // The name is compared the way the platform compares paths: on Windows and macOS .CLAUDE is the same folder as .claude.
    [Fact]
    public void ScopeOf_NameInAnotherCase_CountsOnlyWherePathsIgnoreCase()
    {
        bool ignoresCase = string.Equals("a", "A", PathComparison.Current);

        SourceEndpoints.ScopeOf(Claude(Path.Join(Home, ".CLAUDE")), Home).ShouldBe(ignoresCase ? ClaudeScope.Global : null);
        SourceEndpoints.ScopeOf(Claude(Path.Join(Home, "Workspace", "shop", ".Claude")), Home).ShouldBe(ignoresCase ? ClaudeScope.Project : null);
    }
}
