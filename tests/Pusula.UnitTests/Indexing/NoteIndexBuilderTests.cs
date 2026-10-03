using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Pusula.Indexing;
using Pusula.Links;
using Pusula.UnitTests.Support;
using Shouldly;
using Xunit;

namespace Pusula.UnitTests.Indexing;

/// <summary>The vault and Markdown profiles of the index builder, over made-up folders on disk.</summary>
public sealed class NoteIndexBuilderTests
{
    private static ConfigIndex Build(TempDirectory temp, SourceProfile profile = SourceProfile.Vault, ILogger<IndexBuilder>? logger = null) =>
        new IndexBuilder(logger ?? NullLogger<IndexBuilder>.Instance).Build(temp.Path, profile);

    private static string[] Keys(ConfigIndex index) => [.. index.Files.Keys];

    private static string[] Describe(ConfigFile file) =>
        [.. file.Links.Select(link => $"{link.Kind}|{link.Raw}|{link.Line}|{link.Status}|{link.Target}")];

    private static TempDirectory SmallVault()
    {
        var temp = new TempDirectory();
        temp.CreateDirectory(".obsidian");
        temp.Write(
            "Home.md",
            "---\ntags: [moc]\n---\nSee [[Alpha]] and ![[pic.png]] and [[Missing Note]] and [beta](folder/Beta.md) and [gone](gone.md).\nAlso ~/.claude/rules/x.md and `rules/y.md`. #todo\n");
        temp.Write("Alpha.md", "# Alpha\nBack to [[Home]]. #todo #Done\n");
        temp.Write("folder/Beta.md", "# Beta\n");
        temp.Write("folder/Lonely.md", "nobody links here\n");
        temp.WriteBytes("_attachments/pic.png", [1, 2, 3]);
        return temp;
    }

    [Fact]
    public void Build_Vault_IndexesEveryMarkdownFileExceptHiddenAndDependencyFolders()
    {
        using TempDirectory temp = SmallVault();
        temp.Write(".obsidian/plugins/x/README.md", "x");
        temp.Write(".trash/Deleted.md", "x");
        temp.Write(".hidden.md", "x");
        temp.Write("node_modules/pkg/README.md", "x");
        temp.Write("sub/node_modules/pkg/README.md", "x");
        temp.Write("notes.txt", "not markdown");
        temp.Write("Upper.MD", "# the extension is matched ignoring case");

        ConfigIndex index = Build(temp);

        Keys(index).ShouldBe(["Alpha.md", "folder/Beta.md", "folder/Lonely.md", "Home.md", "Upper.MD"], ignoreOrder: true);
        index.Profile.ShouldBe(SourceProfile.Vault);
    }

    [Fact]
    public void Build_Vault_EveryFileIsANoteThatLoadsOnlyOnDemand()
    {
        using TempDirectory temp = SmallVault();
        temp.Write("CLAUDE.md", "# Not special here\n");
        temp.Write("rules/style.md", "---\npaths: [\"*.cs\"]\n---\nnot a rule here\n");
        temp.Write("settings.json", "{\"outputStyle\":\"Terse\"}");

        ConfigIndex index = Build(temp);

        index.Files.Values.ShouldAllBe(file => file.Layer == Layer.Note && file.LoadMode == LoadMode.OnDemand);
        index.Files.Values.ShouldAllBe(file => file.Tokens.EverySession == 0 && file.Tokens.ProjectSession == 0 && file.Tokens.Total > 0);
        index.OutputStyle.ShouldBeNull();
    }

    [Fact]
    public void Build_MarkdownProfile_ScansLikeAVaultWithoutNeedingTheObsidianDirectory()
    {
        using TempDirectory temp = SmallVault();
        Directory.Delete(temp.Resolve(".obsidian"));

        ConfigIndex index = Build(temp, SourceProfile.Markdown);

        index.Profile.ShouldBe(SourceProfile.Markdown);
        Keys(index).ShouldBe(["Alpha.md", "folder/Beta.md", "folder/Lonely.md", "Home.md"], ignoreOrder: true);
        index.Files["Home.md"].Layer.ShouldBe(Layer.Note);
    }

    [Fact]
    public void Build_Vault_ResolvesWikiLinksEmbedsAndMarkdownLinksAndDropsThePathsOfAClaudeFolder()
    {
        using TempDirectory temp = SmallVault();

        ConfigIndex index = Build(temp);

        Describe(index.Files["Home.md"]).ShouldBe(
        [
            "WikiLink|Alpha|4|Resolved|Alpha.md",
            "Embed|pic.png|4|NonMarkdown|_attachments/pic.png",
            "WikiLink|Missing Note|4|Pending|",
            "MarkdownLink|folder/Beta.md|4|Resolved|folder/Beta.md",
            "MarkdownLink|gone.md|4|Broken|gone.md",
        ]);
    }

    [Fact]
    public void Build_Vault_BacklinksComeFromResolvedLinksOnlyAndAnUnlinkedNoteIsAnOrphan()
    {
        using TempDirectory temp = SmallVault();

        ConfigIndex index = Build(temp);

        index.Backlinks.Keys.ShouldBe(["Alpha.md", "Home.md", "folder/Beta.md"], ignoreOrder: true);
        index.Backlinks["Home.md"].ShouldBe([new Backlink("Alpha.md", LinkKind.WikiLink, 2)]);
        index.Files.Values.Where(file => file.IsOrphan).Select(file => file.Path).ShouldBe(["folder/Lonely.md"]);
    }

    // A note is an orphan when it has no links at all, as in the graph of Obsidian: the entry note of a vault, which
    // links to others and that nothing links back to, is not.
    [Theory]
    [InlineData(SourceProfile.Vault)]
    [InlineData(SourceProfile.Markdown)]
    public void Build_Notes_NoteThatLinksToAnotherNoteButNothingLinksToItIsNotAnOrphanAndANoteWithoutLinksIs(SourceProfile profile)
    {
        using var temp = new TempDirectory();
        temp.Write("Home.md", "Start with [[Plan]].\n");
        temp.Write("Plan.md", "# Plan\n");
        temp.Write("Alone.md", "# Alone\nNo links here.\n");
        temp.Write("folder/Deep.md", "Goes to [Plan](../Plan.md).\n");

        ConfigIndex index = Build(temp, profile);

        index.Backlinks.ContainsKey("Home.md").ShouldBeFalse();
        index.Files["Home.md"].IsOrphan.ShouldBeFalse();
        index.Files["folder/Deep.md"].IsOrphan.ShouldBeFalse();
        index.Files["Plan.md"].IsOrphan.ShouldBeFalse();
        index.Files["Alone.md"].IsOrphan.ShouldBeTrue();
        index.Files.Values.Where(file => file.IsOrphan).Select(file => file.Path).ShouldBe(["Alone.md"]);
    }

    // Only a link that leads to another note makes a note less lonely: not one to an attachment, to a note that is not
    // written yet, to nothing, to the note itself or to the web.
    [Fact]
    public void Build_Vault_LinksThatLeadToNoOtherNoteDoNotMakeANoteLessLonely()
    {
        using var temp = new TempDirectory();
        temp.WriteBytes("pic.png", [1, 2, 3]);
        temp.Write("Loner.md", "![[pic.png]] [[Unwritten]] [gone](gone.md) [[Loner]] [web](https://example.com/x)\n");

        ConfigIndex index = Build(temp);

        Describe(index.Files["Loner.md"]).ShouldBe(
        [
            "Embed|pic.png|1|NonMarkdown|pic.png",
            "WikiLink|Unwritten|1|Pending|",
            "MarkdownLink|gone.md|1|Broken|gone.md",
            "WikiLink|Loner|1|Resolved|Loner.md",
        ]);
        index.Backlinks.ShouldBeEmpty();
        index.Files["Loner.md"].IsOrphan.ShouldBeTrue();
    }

    [Fact]
    public void Build_Vault_NoteThatIsOnlyLinkedToByAnotherNoteIsNotAnOrphanEither()
    {
        using var temp = new TempDirectory();
        temp.Write("Hub.md", "[[Leaf]]\n");
        temp.Write("Leaf.md", "no links out\n");

        ConfigIndex index = Build(temp);

        index.Files.Values.ShouldAllBe(file => !file.IsOrphan);
    }

    [Fact]
    public void Build_Vault_FindsTheTagsOfEveryNote()
    {
        using TempDirectory temp = SmallVault();

        ConfigIndex index = Build(temp);

        index.Files["Home.md"].Tags.ShouldBe(["moc", "todo"]);
        index.Files["Alpha.md"].Tags.ShouldBe(["todo", "Done"]);
        index.Files["folder/Beta.md"].Tags.ShouldBeEmpty();
    }

    [Fact]
    public void Build_MarkdownFileTooLargeToIndex_IsStillSomethingALinkCanPointAt()
    {
        using var temp = new TempDirectory();
        temp.Write("Note.md", "See [[Huge]].\n");
        temp.WriteBytes("Huge.md", new byte[IndexBuilder.MaxFileBytes + 1]);
        var logger = new CapturingLogger<IndexBuilder>();

        ConfigIndex index = Build(temp, logger: logger);

        Keys(index).ShouldBe(["Note.md"]);
        Describe(index.Files["Note.md"]).ShouldBe(["WikiLink|Huge|1|NonMarkdown|Huge.md"]);
        logger.Entries.ShouldContain(entry => entry.Level == LogLevel.Warning && entry.Message.Contains("Huge.md", StringComparison.Ordinal));
    }

    [Fact]
    public void Build_Vault_NamesAreComparedIgnoringCaseButTheSpellingOfTheFileIsKept()
    {
        using var temp = new TempDirectory();
        temp.Write("Ünite/Şablon.md", "# x\n");
        temp.Write("Index.md", "[[ünite/ŞABLON]] and [[şablon]]\n");

        ConfigIndex index = Build(temp);

        Describe(index.Files["Index.md"]).ShouldBe(
        [
            "WikiLink|ünite/ŞABLON|1|Resolved|Ünite/Şablon.md",
            "WikiLink|şablon|1|Resolved|Ünite/Şablon.md",
        ]);
    }

    [Fact]
    public void Build_NeverWritesToTheVault()
    {
        using TempDirectory temp = SmallVault();
        string[] before = Snapshot(temp.Path);

        _ = Build(temp);

        Snapshot(temp.Path).ShouldBe(before);

        static string[] Snapshot(string root) =>
        [
            .. Directory.EnumerateFileSystemEntries(root, "*", SearchOption.AllDirectories)
                .Order(StringComparer.Ordinal)
                .Select(path => $"{Path.GetRelativePath(root, path)}|{(File.Exists(path) ? File.GetLastWriteTimeUtc(path).Ticks + File.ReadAllText(path) : "dir")}"),
        ];
    }

    [Fact]
    public void Build_ClaudeProfile_IsStillTheDefaultAndKnowsNoTags()
    {
        using var temp = new TempDirectory();
        temp.Write("CLAUDE.md", "See [[x]] #tag\n");

        ConfigIndex index = new IndexBuilder(NullLogger<IndexBuilder>.Instance).Build(temp.Path);

        index.Profile.ShouldBe(SourceProfile.Claude);
        index.Files["CLAUDE.md"].Layer.ShouldBe(Layer.ClaudeMd);
        index.Files["CLAUDE.md"].Tags.ShouldBeEmpty();
    }

    // The old rule stays for a Claude Code folder: a file that links out is still an orphan when nothing links to it.
    [Fact]
    public void Build_ClaudeProfile_FileThatLinksOutButNothingLinksToItIsStillAnOrphan()
    {
        using var temp = new TempDirectory();
        temp.Write("a.md", "See [b](b.md).\n");
        temp.Write("b.md", "linked to\n");
        temp.Write("c.md", "alone\n");

        ConfigIndex index = new IndexBuilder(NullLogger<IndexBuilder>.Instance).Build(temp.Path);

        index.Files["a.md"].Layer.ShouldBe(Layer.Reference);
        index.Files["a.md"].IsOrphan.ShouldBeTrue();
        index.Files["b.md"].IsOrphan.ShouldBeFalse();
        index.Files["c.md"].IsOrphan.ShouldBeTrue();
    }

    [Theory]
    [InlineData(SourceProfile.Claude)]
    [InlineData(SourceProfile.Vault)]
    [InlineData(SourceProfile.Markdown)]
    public void Build_EveryFile_CarriesTheTimeItWasLastWrittenInUtc(SourceProfile profile)
    {
        using var temp = new TempDirectory();
        string first = temp.Write("a.md", "x");
        string second = temp.Write("folder/b.md", "y");
        var firstTime = new DateTime(2025, 3, 4, 5, 6, 7, DateTimeKind.Utc);
        var secondTime = new DateTime(2026, 9, 30, 23, 59, 58, DateTimeKind.Utc);
        File.SetLastWriteTimeUtc(first, firstTime);
        File.SetLastWriteTimeUtc(second, secondTime);

        ConfigIndex index = new IndexBuilder(NullLogger<IndexBuilder>.Instance).Build(temp.Path, profile);

        index.Files["a.md"].ModifiedAt.ShouldBe(new DateTimeOffset(firstTime));
        index.Files["folder/b.md"].ModifiedAt.ShouldBe(new DateTimeOffset(secondTime));
        index.Files["a.md"].ModifiedAt.Offset.ShouldBe(TimeSpan.Zero);
    }

    [Fact]
    public void Build_ClaudeProfile_ResolvesEmbedsLikeWikiLinks()
    {
        using var temp = new TempDirectory();
        temp.Write("rules/a.md", "![[b]] and ![[missing]]\n");
        temp.Write("rules/b.md", "x\n");

        ConfigIndex index = new IndexBuilder(NullLogger<IndexBuilder>.Instance).Build(temp.Path);

        Describe(index.Files["rules/a.md"]).ShouldBe(
        [
            "Embed|b|1|Resolved|rules/b.md",
            "Embed|missing|1|Broken|",
        ]);
    }
}
