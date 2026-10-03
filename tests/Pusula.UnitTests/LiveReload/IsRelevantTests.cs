using Pusula.Indexing;
using Pusula.LiveReload;
using Shouldly;
using Xunit;

namespace Pusula.UnitTests.LiveReload;

public sealed class IsRelevantTests
{
    private static bool IsRelevant(string path, StringComparison comparison = StringComparison.Ordinal) =>
        IndexHost.IsRelevant(path, comparison);

    [Theory]
    [InlineData("CLAUDE.md")]
    [InlineData("README")]
    [InlineData("notes.MD")]
    [InlineData("rules/a.md")]
    [InlineData("rules/deep/er/a.md")]
    [InlineData("skills/deploy/SKILL.md")]
    [InlineData("shared/x.Md")]
    [InlineData("a b/c d.md")]
    public void IsRelevant_MarkdownFile_IsTrue(string path) =>
        IsRelevant(path).ShouldBeTrue();

    [Theory]
    [InlineData("rules")]
    [InlineData("skills/deploy")]
    [InlineData("shared/new-folder")]
    [InlineData("a/b/c")]
    public void IsRelevant_NameWithoutAnExtension_MayBeADirectoryAndIsTrue(string path) =>
        IsRelevant(path).ShouldBeTrue();

    [Theory]
    [InlineData("settings.json")]
    [InlineData("projects/p")]
    [InlineData("projects/p/memory")]
    [InlineData("projects/p/memory/MEMORY.md")]
    [InlineData("projects/p/memory/note.md")]
    [InlineData("projects/p/memory/sub/deep.md")]
    [InlineData("projects/p/memory/data.json")]
    public void IsRelevant_SettingsAndProjectMemory_IsTrue(string path) =>
        IsRelevant(path).ShouldBeTrue();

    [Theory]
    [InlineData("")]
    [InlineData("/")]
    [InlineData(".")]
    [InlineData("..")]
    [InlineData("../outside.md")]
    [InlineData("rules/../../outside.md")]
    [InlineData(".git")]
    [InlineData(".git/config")]
    [InlineData(".credentials.json")]
    [InlineData(".hidden.md")]
    [InlineData("rules/.draft.md")]
    [InlineData("rules/.cache/x.md")]
    [InlineData("projects/p/memory/.hidden.md")]
    [InlineData("projects/p/memory/.obsidian/x.md")]
    public void IsRelevant_HiddenNamesAndPathsLeavingTheRoot_IsFalse(string path) =>
        IsRelevant(path).ShouldBeFalse();

    [Theory]
    [InlineData("plugins")]
    [InlineData("plugins/x.md")]
    [InlineData("plugins/market/deep/x.md")]
    [InlineData("cache/a.md")]
    [InlineData("file-history/s/v1")]
    [InlineData("sessions/abc.json")]
    [InlineData("debug/log.txt")]
    [InlineData("backups/x.md")]
    [InlineData("paste-cache/x")]
    [InlineData("chrome/x.md")]
    [InlineData("ide/x.md")]
    [InlineData("jobs/x.md")]
    [InlineData("daemon/x.md")]
    [InlineData("downloads/x.md")]
    [InlineData("session-env/x.md")]
    [InlineData("security/x.md")]
    [InlineData("usage-data/report.md")]
    [InlineData("todos/x.md")]
    [InlineData("shell-snapshots/x.md")]
    [InlineData("statsig/x.md")]
    [InlineData("telemetry/x.md")]
    [InlineData("state/x.md")]
    public void IsRelevant_RuntimeDirectoriesAtTheRoot_IsFalse(string path) =>
        IsRelevant(path).ShouldBeFalse();

    [Theory]
    [InlineData("rules/cache/y.md")]
    [InlineData("rules/plugins/z.md")]
    [InlineData("skills/state")]
    public void IsRelevant_RuntimeDirectoryNamesBelowTheTopLevel_AreOrdinaryDirectories(string path) =>
        IsRelevant(path).ShouldBeTrue();

    // projects/ may be created after the application started, with a project below it that no event was seen for:
    // the directory itself has to trigger a rebuild.
    [Theory]
    [InlineData("projects")]
    [InlineData("projects/")]
    [InlineData("/projects")]
    public void IsRelevant_TheProjectsDirectoryItself_IsTrue(string path) =>
        IsRelevant(path).ShouldBeTrue();

    [Theory]
    [InlineData("projects/p/transcript.jsonl")]
    [InlineData("projects/p/session.md")]
    [InlineData("projects/p/sessions/x.md")]
    [InlineData("projects/p/subagents/memory/x.md")]
    [InlineData("projects/p/subagents")]
    [InlineData("projects/p/MEMORY.md")]
    [InlineData("projects/p/memoryx/a.md")]
    public void IsRelevant_OutsideTheMemoryDirectoryOfAProject_IsFalse(string path) =>
        IsRelevant(path).ShouldBeFalse();

    [Theory]
    [InlineData("rules/run.sh")]
    [InlineData("hooks/run.sh")]
    [InlineData("data.json")]
    [InlineData("rules/settings.json")]
    [InlineData("skills/x/script.py")]
    [InlineData("image.png")]
    [InlineData("rules/a.md.bak")]
    [InlineData("rules/a.md~")]
    [InlineData("rules/a.swp")]
    [InlineData("a.txt")]
    public void IsRelevant_OtherFiles_IsFalse(string path) =>
        IsRelevant(path).ShouldBeFalse();

    [Theory]
    [InlineData("rules//a.md")]
    [InlineData("rules/a.md/")]
    [InlineData("/rules/a.md")]
    public void IsRelevant_RedundantSeparators_AreIgnored(string path) =>
        IsRelevant(path).ShouldBeTrue();

    [Theory]
    [InlineData("Plugins/x.md", StringComparison.Ordinal, true)]
    [InlineData("Plugins/x.md", StringComparison.OrdinalIgnoreCase, false)]
    [InlineData("Projects", StringComparison.OrdinalIgnoreCase, true)]
    [InlineData("Projects/p/sub", StringComparison.Ordinal, true)]
    [InlineData("Projects/p/sub", StringComparison.OrdinalIgnoreCase, false)]
    [InlineData("Projects/p/Memory/x.md", StringComparison.OrdinalIgnoreCase, true)]
    [InlineData("Projects/p/transcript.jsonl", StringComparison.Ordinal, false)]
    [InlineData("Settings.json", StringComparison.Ordinal, false)]
    [InlineData("Settings.json", StringComparison.OrdinalIgnoreCase, true)]
    [InlineData("Rules/A.MD", StringComparison.Ordinal, true)]
    public void IsRelevant_NameComparison_FollowsThePlatformPolicy(string path, StringComparison comparison, bool expected) =>
        IsRelevant(path, comparison).ShouldBe(expected);

    // ---- Vaults and plain Markdown folders: every path counts, but the hidden and the dependencies ---------------

    [Theory]
    [InlineData(SourceProfile.Vault)]
    [InlineData(SourceProfile.Markdown)]
    public void IsRelevant_NotesProfile_EveryPathThatIsNotHiddenOrADependencyIsTrue(SourceProfile profile)
    {
        foreach (string path in new[]
        {
            "Note.md", "folder/Note.md", "_attachments/pic.png", "Doc.pdf", "folder", "data.csv", "notes.txt",
            "settings.json", "plugins/x.md", "projects/demo/transcript.jsonl", "node_modules_notes/x.md",
        })
        {
            IndexHost.IsRelevant(path, StringComparison.Ordinal, profile).ShouldBeTrue(path);
        }
    }

    [Theory]
    [InlineData(SourceProfile.Vault)]
    [InlineData(SourceProfile.Markdown)]
    public void IsRelevant_NotesProfile_HiddenPartsAndDependencyFolders_AreFalse(SourceProfile profile)
    {
        foreach (string path in new[]
        {
            "", ".obsidian/workspace.json", ".trash/Old.md", "folder/.git/config", ".hidden.md", "../outside.md",
            "node_modules", "node_modules/pkg/index.md", "a/node_modules/b.md",
        })
        {
            IndexHost.IsRelevant(path, StringComparison.Ordinal, profile).ShouldBeFalse(path);
        }
    }

    [Fact]
    public void IsRelevant_NotesProfileOnAPlatformThatIgnoresCase_AlsoSkipsNodeModulesInAnyCase()
    {
        IndexHost.IsRelevant("Node_Modules/x.md", StringComparison.OrdinalIgnoreCase, SourceProfile.Vault).ShouldBeFalse();
        IndexHost.IsRelevant("Node_Modules/x.md", StringComparison.Ordinal, SourceProfile.Vault).ShouldBeTrue();
    }

    [Fact]
    public void IsRelevant_WithoutAProfile_IsTheClaudeRule() =>
        IndexHost.IsRelevant("notes.txt", StringComparison.Ordinal).ShouldBeFalse();
}
