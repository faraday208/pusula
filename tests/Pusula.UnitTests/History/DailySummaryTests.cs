using Microsoft.Extensions.Logging.Abstractions;
using Pusula.History;
using Pusula.Indexing;
using Pusula.Links;
using Pusula.UnitTests.Support;
using Shouldly;
using Xunit;

namespace Pusula.UnitTests.History;

public sealed class DailySummaryTests
{
    private static readonly DateOnly Day = new(2026, 10, 12);
    private static readonly DateTimeOffset TakenAt = new(2026, 10, 12, 9, 14, 3, TimeSpan.Zero);

    private static DailySummary Of(params ConfigFile[] files) =>
        DailySummary.Of(IndexFactory.Index(files), Day, TakenAt);

    private static ConfigFile File(string path, int total, int everySession, params Link[] links) =>
        IndexFactory.File(path, tokens: new TokenCounts(total, everySession, 0), links: links);

    private static Link Broken(string raw, int line = 1, LinkKind kind = LinkKind.MarkdownLink) =>
        IndexFactory.Link(kind, raw, line, LinkStatus.Broken, raw);

    [Fact]
    public void Of_Index_KeepsTheDayAndWhenItWasTaken()
    {
        DailySummary summary = Of(File("a.md", 1, 1));

        summary.Day.ShouldBe(Day);
        summary.TakenAt.ShouldBe(TakenAt);
    }

    [Fact]
    public void Of_Files_KeepTheirTokensInAllAndInEverySessionByIndexKey()
    {
        DailySummary summary = Of(File("CLAUDE.md", 3495, 3495), File("rules/a.md", 800, 0), File("skills/s/SKILL.md", 1200, 64));

        summary.Files.Count.ShouldBe(3);
        summary.Files["CLAUDE.md"].ShouldBe(new FileTokens(3495, 3495));
        summary.Files["rules/a.md"].ShouldBe(new FileTokens(800, 0));
        summary.Files["skills/s/SKILL.md"].ShouldBe(new FileTokens(1200, 64));
    }

    [Fact]
    public void Of_EmptyIndex_HasNoFilesAndNoBrokenLinks()
    {
        DailySummary summary = Of();

        summary.Files.ShouldBeEmpty();
        summary.Broken.ShouldBeEmpty();
    }

    [Fact]
    public void Of_BrokenLinks_AreListedInFileOrderThenDocumentOrder()
    {
        DailySummary summary = Of(
            File("z.md", 1, 0, Broken("later.md", line: 9), Broken("first.md", line: 2, kind: LinkKind.WikiLink)),
            File("a.md", 1, 0, Broken("~/.claude/x.md", line: 5, kind: LinkKind.ClaudePath)));

        summary.Broken.ShouldBe(
        [
            new BrokenLink("a.md", LinkKind.ClaudePath, "~/.claude/x.md"),
            new BrokenLink("z.md", LinkKind.MarkdownLink, "later.md"),
            new BrokenLink("z.md", LinkKind.WikiLink, "first.md"),
        ]);
    }

    [Fact]
    public void Of_LinksThatAreNotBroken_AreNotKept()
    {
        DailySummary summary = Of(
            File(
                "a.md",
                1,
                0,
                IndexFactory.Link(LinkKind.MarkdownLink, "ok.md", 1, LinkStatus.Resolved, "b.md"),
                IndexFactory.Link(LinkKind.WikiLink, "later", 2, LinkStatus.Pending),
                IndexFactory.Link(LinkKind.MarkdownLink, "dir/", 3, LinkStatus.NonMarkdown, "dir"),
                IndexFactory.Link(LinkKind.MarkdownLink, "../x", 4, LinkStatus.External),
                Broken("gone.md", line: 5)),
            File("b.md", 1, 0));

        summary.Broken.ShouldBe([new BrokenLink("a.md", LinkKind.MarkdownLink, "gone.md")]);
    }

    [Fact]
    public void Of_SameBrokenLinkTwice_IsKeptOnceAndOnlyTheSameFileKindAndTextAreTheSame()
    {
        DailySummary summary = Of(
            File(
                "a.md",
                1,
                0,
                Broken("gone.md", line: 3),
                Broken("gone.md", line: 8),
                Broken("gone.md", line: 9, kind: LinkKind.RelativePath),
                Broken("Gone.md", line: 10)),
            File("b.md", 1, 0, Broken("gone.md", line: 1)));

        summary.Broken.ShouldBe(
        [
            new BrokenLink("a.md", LinkKind.MarkdownLink, "gone.md"),
            new BrokenLink("a.md", LinkKind.RelativePath, "gone.md"),
            new BrokenLink("a.md", LinkKind.MarkdownLink, "Gone.md"),
            new BrokenLink("b.md", LinkKind.MarkdownLink, "gone.md"),
        ]);
    }

    // The files of a vault or a plain Markdown folder load only on demand: nothing is added to every session, and the summary says so.
    [Fact]
    public void Of_NotesOfAVault_HaveNoTokensInEverySession()
    {
        using var temp = new TempDirectory();
        temp.CreateDirectory(".obsidian");
        temp.Write("Home.md", "Start with [[Plan]] and [gone](gone.md).\n");
        temp.Write("Plan.md", "# Plan\nA longer note, so that it has more than a few tokens in it.\n");
        ConfigIndex index = new IndexBuilder(NullLogger<IndexBuilder>.Instance).Build(temp.Path, SourceProfile.Vault);

        DailySummary summary = DailySummary.Of(index, Day, TakenAt);

        summary.Files.Keys.ShouldBe(["Home.md", "Plan.md"], ignoreOrder: true);
        summary.Files.Values.ShouldAllBe(file => file.EverySession == 0 && file.Total > 0);
        summary.Broken.ShouldBe([new BrokenLink("Home.md", LinkKind.MarkdownLink, "gone.md")]);
    }

    // The keys are compared the way an index compares them: ignoring case where paths are.
    [Fact]
    public void Of_Files_AreLookedUpTheWayTheIndexLooksUpAKey()
    {
        DailySummary summary = Of(File("Rules/A.md", 5, 5));

        summary.Files.ContainsKey("Rules/A.md").ShouldBeTrue();
        summary.Files.ContainsKey("rules/a.md").ShouldBe(OperatingSystem.IsWindows());
    }

    [Fact]
    public void BrokenLink_SameFileKindAndText_AreEqualWhateverTheLine()
    {
        var first = new BrokenLink("a.md", LinkKind.WikiLink, "gone");

        first.ShouldBe(new BrokenLink("a.md", LinkKind.WikiLink, "gone"));
        first.GetHashCode().ShouldBe(new BrokenLink("a.md", LinkKind.WikiLink, "gone").GetHashCode());
        first.ShouldNotBe(new BrokenLink("a.md", LinkKind.Embed, "gone"));
        first.ShouldNotBe(new BrokenLink("a.md", LinkKind.WikiLink, "Gone"));
        first.ShouldNotBe(new BrokenLink("b.md", LinkKind.WikiLink, "gone"));
    }

    // The file is compared as the platform compares paths; the text of the link is always compared as it is written.
    [Fact]
    public void BrokenLink_FileInAnotherCase_IsTheSameLinkOnlyWherePathsIgnoreCase()
    {
        var lower = new BrokenLink("rules/a.md", LinkKind.WikiLink, "gone");
        var upper = new BrokenLink("Rules/A.md", LinkKind.WikiLink, "gone");

        lower.Equals(upper).ShouldBe(OperatingSystem.IsWindows());
        if (OperatingSystem.IsWindows())
        {
            lower.GetHashCode().ShouldBe(upper.GetHashCode());
        }
    }
}
