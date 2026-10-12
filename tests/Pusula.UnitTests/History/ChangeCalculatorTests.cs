using Pusula.History;
using Pusula.Indexing;
using Pusula.Links;
using Pusula.UnitTests.Support;
using Shouldly;
using Xunit;

namespace Pusula.UnitTests.History;

public sealed class ChangeCalculatorTests
{
    private static readonly DateOnly Yesterday = new(2026, 10, 11);
    private static readonly DateTimeOffset TakenAt = new(2026, 10, 11, 6, 14, 3, TimeSpan.Zero);

    // The summary of yesterday: files as (path, tokens, every-session tokens), and the links that were broken as (file, kind, text).
    private static DailySummary Before((string Path, int Total, int EverySession)[] files, params BrokenLink[] broken) =>
        new(
            Yesterday,
            TakenAt,
            files.ToDictionary(file => file.Path, file => new FileTokens(file.Total, file.EverySession), DailySummary.Keys),
            broken);

    private static ConfigFile Now(string path, int total, int everySession, LoadMode loadMode = LoadMode.EverySession, string? content = null, params Link[] links) =>
        IndexFactory.File(path, Layer.Rule, loadMode, new TokenCounts(total, everySession, 0), links, content: content);

    private static ChangesSince Compare(DailySummary before, params ConfigFile[] now) =>
        ChangeCalculator.Compare(before, IndexFactory.Index(now));

    private static Link Broken(string raw, int line, LinkKind kind = LinkKind.MarkdownLink) =>
        IndexFactory.Link(kind, raw, line, LinkStatus.Broken, raw);

    // ---- Files --------------------------------------------------------------------------------------------------

    [Fact]
    public void Compare_NothingChanged_HasNoListsAndTheSameTotals()
    {
        ChangesSince changes = Compare(
            Before([("CLAUDE.md", 100, 100), ("rules/a.md", 50, 0)]),
            Now("CLAUDE.md", 100, 100),
            Now("rules/a.md", 50, 0, LoadMode.Conditional));

        changes.Day.ShouldBe(Yesterday);
        changes.TakenAt.ShouldBe(TakenAt);
        changes.FilesBefore.ShouldBe(2);
        changes.FilesAfter.ShouldBe(2);
        changes.EverySessionTokensBefore.ShouldBe(100);
        changes.EverySessionTokensAfter.ShouldBe(100);
        changes.Added.ShouldBeEmpty();
        changes.Removed.ShouldBeEmpty();
        changes.Resized.ShouldBeEmpty();
        changes.NewlyBroken.ShouldBeEmpty();
        changes.NoLongerBroken.ShouldBeEmpty();
    }

    [Fact]
    public void Compare_EmptySummaryAndEmptyIndex_HasNothingToTell()
    {
        ChangesSince changes = Compare(Before([]));

        changes.FilesBefore.ShouldBe(0);
        changes.FilesAfter.ShouldBe(0);
        changes.EverySessionTokensBefore.ShouldBe(0);
        changes.EverySessionTokensAfter.ShouldBe(0);
        changes.Added.ShouldBeEmpty();
        changes.Removed.ShouldBeEmpty();
    }

    [Fact]
    public void Compare_FileThatIsOnlyInTheIndex_IsAddedWithItsCurrentValuesAndLoadMode()
    {
        ChangesSince changes = Compare(Before([("CLAUDE.md", 100, 100)]), Now("CLAUDE.md", 100, 100), Now("rules/new.md", 1200, 1200), Now("memory/m.md", 40, 0, LoadMode.OnDemand));

        changes.Added.ShouldBe(
        [
            new ChangedFile("rules/new.md", 1200, 1200, LoadMode.EverySession),
            new ChangedFile("memory/m.md", 40, 0, LoadMode.OnDemand),
        ]);
        changes.FilesBefore.ShouldBe(1);
        changes.FilesAfter.ShouldBe(3);
        changes.EverySessionTokensBefore.ShouldBe(100);
        changes.EverySessionTokensAfter.ShouldBe(1300);
    }

    [Fact]
    public void Compare_FileThatIsOnlyInTheSummary_IsRemovedWithTheValuesOfTheSummaryAndNoLoadMode()
    {
        ChangesSince changes = Compare(Before([("CLAUDE.md", 100, 100), ("rules/old.md", 300, 300)]), Now("CLAUDE.md", 100, 100));

        changes.Removed.ShouldBe([new ChangedFile("rules/old.md", 300, 300)]);
        changes.Removed[0].LoadMode.ShouldBeNull();
        changes.FilesBefore.ShouldBe(2);
        changes.FilesAfter.ShouldBe(1);
        changes.EverySessionTokensBefore.ShouldBe(400);
        changes.EverySessionTokensAfter.ShouldBe(100);
    }

    [Fact]
    public void Compare_AddedFiles_AreSortedByEverySessionTokensThenTokensThenPath()
    {
        ChangesSince changes = Compare(
            Before([]),
            Now("b.md", 500, 10),
            Now("a.md", 500, 10),
            Now("c.md", 900, 10),
            Now("d.md", 5, 50),
            Now("e.md", 4000, 0, LoadMode.OnDemand),
            Now("Z.md", 500, 10));

        // Every-session tokens first, then the whole size, then the path as it is written (upper case before lower case).
        changes.Added.Select(file => file.Path).ShouldBe(["d.md", "c.md", "Z.md", "a.md", "b.md", "e.md"]);
    }

    [Fact]
    public void Compare_RemovedFiles_AreSortedByEverySessionTokensThenTokensThenPath()
    {
        ChangesSince changes = Compare(
            Before([("b.md", 500, 10), ("a.md", 500, 10), ("c.md", 900, 10), ("d.md", 5, 50), ("e.md", 4000, 0), ("Z.md", 500, 10)]));

        changes.Removed.Select(file => file.Path).ShouldBe(["d.md", "c.md", "Z.md", "a.md", "b.md", "e.md"]);
    }

    [Fact]
    public void Compare_FileInBothWithOtherTokens_IsResizedWithBothValuesAndTheCurrentLoadMode()
    {
        ChangesSince changes = Compare(
            Before([("CLAUDE.md", 3400, 3400), ("rules/a.md", 800, 0)]),
            Now("CLAUDE.md", 3495, 3495),
            Now("rules/a.md", 800, 0, LoadMode.Conditional));

        changes.Resized.ShouldBe([new ResizedFile("CLAUDE.md", LoadMode.EverySession, 3400, 3495, 3400, 3495)]);
        changes.Added.ShouldBeEmpty();
        changes.Removed.ShouldBeEmpty();
        changes.EverySessionTokensBefore.ShouldBe(3400);
        changes.EverySessionTokensAfter.ShouldBe(3495);
    }

    [Fact]
    public void Compare_FileWhoseEverySessionTokensChangedAndWholeSizeDidNot_IsResized()
    {
        // A rule that got a paths: key stops loading every session, and its size does not change.
        ChangesSince changes = Compare(Before([("rules/a.md", 800, 800)]), Now("rules/a.md", 800, 0, LoadMode.Conditional));

        changes.Resized.ShouldBe([new ResizedFile("rules/a.md", LoadMode.Conditional, 800, 800, 800, 0)]);
    }

    [Fact]
    public void Compare_FileWhoseTextChangedButWhoseTokenCountsDidNot_IsNotResized()
    {
        ChangesSince changes = Compare(Before([("a.md", 2, 2)]), Now("a.md", 2, 2, content: "four words here"));

        changes.Resized.ShouldBeEmpty();
        changes.Added.ShouldBeEmpty();
        changes.Removed.ShouldBeEmpty();
    }

    [Fact]
    public void Compare_ResizedFiles_AreSortedByTheSizeOfTheChangeInEverySessionTokensThenInTokensThenPath()
    {
        ChangesSince changes = Compare(
            Before([("a.md", 100, 100), ("b.md", 100, 100), ("c.md", 100, 100), ("d.md", 100, 100), ("e.md", 100, 100), ("f.md", 100, 100)]),
            Now("a.md", 90, 90),
            Now("b.md", 130, 130),
            Now("c.md", 400, 100),
            Now("d.md", 100, 70),
            Now("e.md", 100, 125),
            Now("f.md", 80, 130));

        // Changes in every-session tokens: a 10, b 30, c 0, d 30, e 25, f 30 (the size of a change counts, not its direction). b, f and d
        // tie, and come by the size of the change in tokens: 30, 20, 0. c changed in tokens only, so it comes last.
        changes.Resized.Select(file => file.Path).ShouldBe(["b.md", "f.md", "d.md", "e.md", "a.md", "c.md"]);
    }

    [Fact]
    public void Compare_ResizedFilesThatChangedAsMuch_AreSortedByPath()
    {
        ChangesSince changes = Compare(
            Before([("b.md", 10, 10), ("a.md", 10, 10), ("C.md", 10, 10)]),
            Now("b.md", 20, 20),
            Now("a.md", 20, 20),
            Now("C.md", 20, 20));

        changes.Resized.Select(file => file.Path).ShouldBe(["C.md", "a.md", "b.md"]);
    }

    [Fact]
    public void Compare_Totals_CountTheFilesAndTheTokensInEverySessionOfBothSides()
    {
        ChangesSince changes = Compare(
            Before([("a.md", 10, 10), ("b.md", 20, 5), ("c.md", 30, 0)]),
            Now("a.md", 12, 12),
            Now("b.md", 20, 5),
            Now("d.md", 7, 7),
            Now("e.md", 9, 0, LoadMode.OnDemand));

        changes.FilesBefore.ShouldBe(3);
        changes.FilesAfter.ShouldBe(4);
        changes.EverySessionTokensBefore.ShouldBe(15);
        changes.EverySessionTokensAfter.ShouldBe(24);
    }

    // For a vault or a plain Markdown folder every file loads on demand, so both sides have no tokens in every session.
    [Fact]
    public void Compare_NotesOfAVault_HaveNoTokensInEverySessionAndAreComparedByTheirSize()
    {
        ChangesSince changes = ChangeCalculator.Compare(
            Before([("Home.md", 40, 0), ("Old.md", 10, 0)]),
            IndexFactory.Index(
                [
                    IndexFactory.File("Home.md", Layer.Note, LoadMode.OnDemand, new TokenCounts(55, 0, 0)),
                    IndexFactory.File("New.md", Layer.Note, LoadMode.OnDemand, new TokenCounts(20, 0, 0)),
                ],
                profile: SourceProfile.Vault));

        changes.EverySessionTokensBefore.ShouldBe(0);
        changes.EverySessionTokensAfter.ShouldBe(0);
        changes.Added.ShouldBe([new ChangedFile("New.md", 20, 0, LoadMode.OnDemand)]);
        changes.Removed.ShouldBe([new ChangedFile("Old.md", 10, 0)]);
        changes.Resized.ShouldBe([new ResizedFile("Home.md", LoadMode.OnDemand, 40, 55, 0, 0)]);
    }

    // A summary comes from a file that someone could have edited: a total that does not fit is capped, and nothing throws.
    [Fact]
    public void Compare_SummaryWithHugeNumbers_DoesNotWrapAround()
    {
        ChangesSince changes = Compare(
            Before([("a.md", int.MaxValue, int.MaxValue), ("b.md", int.MaxValue, int.MaxValue)]),
            Now("a.md", 0, 0));

        changes.EverySessionTokensBefore.ShouldBe(int.MaxValue);
        changes.Resized.Select(file => file.Path).ShouldBe(["a.md"]);
        changes.Removed.Select(file => file.Path).ShouldBe(["b.md"]);
    }

    // ---- Broken links -------------------------------------------------------------------------------------------

    [Fact]
    public void Compare_LinkThatIsBrokenNowAndWasNotInTheSummary_IsNewlyBrokenWithItsLine()
    {
        ChangesSince changes = Compare(
            Before([("guides/setup.md", 10, 0)]),
            Now("guides/setup.md", 10, 0, links: [Broken("~/.claude/X.md", 78, LinkKind.ClaudePath)]));

        changes.NewlyBroken.ShouldBe([new ChangedLink("guides/setup.md", LinkKind.ClaudePath, "~/.claude/X.md", 78)]);
        changes.NoLongerBroken.ShouldBeEmpty();
    }

    [Fact]
    public void Compare_BrokenLinkWhoseLineMoved_IsNeitherNewNorFixed()
    {
        ChangesSince changes = Compare(
            Before([("a.md", 10, 0)], new BrokenLink("a.md", LinkKind.MarkdownLink, "../gone.md")),
            Now("a.md", 10, 0, links: [Broken("../gone.md", 40)]));

        changes.NewlyBroken.ShouldBeEmpty();
        changes.NoLongerBroken.ShouldBeEmpty();
    }

    [Fact]
    public void Compare_SameBrokenLinkInAnotherFileOrOfAnotherKind_IsANewLink()
    {
        ChangesSince changes = Compare(
            Before([("a.md", 10, 0), ("b.md", 10, 0)], new BrokenLink("a.md", LinkKind.MarkdownLink, "gone.md")),
            Now("a.md", 10, 0, links: [Broken("gone.md", 1), Broken("gone.md", 2, LinkKind.RelativePath)]),
            Now("b.md", 10, 0, links: [Broken("gone.md", 1)]));

        changes.NewlyBroken.ShouldBe(
        [
            new ChangedLink("a.md", LinkKind.RelativePath, "gone.md", 2),
            new ChangedLink("b.md", LinkKind.MarkdownLink, "gone.md", 1),
        ]);
    }

    [Fact]
    public void Compare_NewlyBrokenLinkThatAppearsTwice_IsListedOnceWithTheLineOfTheFirst()
    {
        ChangesSince changes = Compare(Before([("a.md", 10, 0)]), Now("a.md", 10, 0, links: [Broken("gone.md", 3), Broken("gone.md", 12)]));

        changes.NewlyBroken.ShouldBe([new ChangedLink("a.md", LinkKind.MarkdownLink, "gone.md", 3)]);
    }

    [Fact]
    public void Compare_NewlyBrokenLinks_AreSortedBySourceThenLine()
    {
        ChangesSince changes = Compare(
            Before([]),
            Now("beta.md", 1, 0, links: [Broken("x", 2), Broken("y", 1)]),
            Now("alpha.md", 1, 0, links: [Broken("z", 30), Broken("w", 4)]),
            Now("Zeta.md", 1, 0, links: [Broken("v", 1)]));

        // The files come as they are written (upper case before lower case), and the lines of a file in the order of the file.
        changes.NewlyBroken.Select(link => $"{link.Source}:{link.Line}").ShouldBe(["Zeta.md:1", "alpha.md:4", "alpha.md:30", "beta.md:1", "beta.md:2"]);
    }

    [Fact]
    public void Compare_NewlyBrokenLinksOnTheSameLine_KeepTheirDocumentOrder()
    {
        ChangesSince changes = Compare(
            Before([]),
            Now("a.md", 1, 0, links: [Broken("zzz", 5), Broken("aaa", 5, LinkKind.WikiLink)]));

        changes.NewlyBroken.Select(link => link.Raw).ShouldBe(["zzz", "aaa"]);
    }

    [Fact]
    public void Compare_LinkThatWasFixed_IsNoLongerBrokenWithoutALine()
    {
        ChangesSince changes = Compare(
            Before([("rules/a.md", 10, 0), ("b.md", 10, 0)], new BrokenLink("rules/a.md", LinkKind.MarkdownLink, "../gone.md")),
            Now("rules/a.md", 10, 0, links: [IndexFactory.Link(LinkKind.MarkdownLink, "../gone.md", 4, LinkStatus.Resolved, "gone.md")]),
            Now("b.md", 10, 0));

        changes.NoLongerBroken.ShouldBe([new ChangedLink("rules/a.md", LinkKind.MarkdownLink, "../gone.md")]);
        changes.NoLongerBroken[0].Line.ShouldBeNull();
        changes.NewlyBroken.ShouldBeEmpty();
    }

    [Fact]
    public void Compare_LinkThatWasRemovedFromItsFile_IsNoLongerBroken()
    {
        ChangesSince changes = Compare(
            Before([("a.md", 10, 0)], new BrokenLink("a.md", LinkKind.WikiLink, "gone")),
            Now("a.md", 10, 0));

        changes.NoLongerBroken.ShouldBe([new ChangedLink("a.md", LinkKind.WikiLink, "gone")]);
    }

    [Fact]
    public void Compare_LinkWhoseFileIsGone_IsNoLongerBroken()
    {
        ChangesSince changes = Compare(
            Before([("a.md", 10, 0), ("old.md", 10, 0)], new BrokenLink("old.md", LinkKind.MarkdownLink, "gone.md")),
            Now("a.md", 10, 0));

        changes.NoLongerBroken.ShouldBe([new ChangedLink("old.md", LinkKind.MarkdownLink, "gone.md")]);
        changes.Removed.Select(file => file.Path).ShouldBe(["old.md"]);
    }

    // Broken is the only status that counts: a link that is a note that is not written yet, or that leads outside, is not broken.
    [Theory]
    [InlineData(LinkStatus.Pending)]
    [InlineData(LinkStatus.NonMarkdown)]
    [InlineData(LinkStatus.External)]
    [InlineData(LinkStatus.Resolved)]
    public void Compare_LinkThatIsNotBrokenAnyMore_WhateverItIsNow_IsNoLongerBroken(LinkStatus status)
    {
        ChangesSince changes = Compare(
            Before([("a.md", 10, 0)], new BrokenLink("a.md", LinkKind.WikiLink, "later")),
            Now("a.md", 10, 0, links: [IndexFactory.Link(LinkKind.WikiLink, "later", 1, status, status == LinkStatus.Resolved ? "later.md" : null)]));

        changes.NoLongerBroken.ShouldBe([new ChangedLink("a.md", LinkKind.WikiLink, "later")]);
        changes.NewlyBroken.ShouldBeEmpty();
    }

    [Fact]
    public void Compare_NoLongerBrokenLinks_AreSortedBySourceThenKindThenText()
    {
        ChangesSince changes = Compare(
            Before(
                [("a.md", 1, 0)],
                new BrokenLink("beta.md", LinkKind.WikiLink, "x"),
                new BrokenLink("alpha.md", LinkKind.WikiLink, "z"),
                new BrokenLink("alpha.md", LinkKind.MarkdownLink, "z"),
                new BrokenLink("alpha.md", LinkKind.ClaudePath, "z"),
                new BrokenLink("alpha.md", LinkKind.MarkdownLink, "b"),
                new BrokenLink("Zeta.md", LinkKind.WikiLink, "x")),
            Now("alpha.md", 1, 0));

        // The files come as they are written (upper case before lower case), and the kinds by their names, which is what the answer shows:
        // ClaudePath, MarkdownLink, WikiLink.
        changes.NoLongerBroken.Select(link => $"{link.Source}|{link.Kind}|{link.Raw}").ShouldBe(
        [
            "Zeta.md|WikiLink|x",
            "alpha.md|ClaudePath|z",
            "alpha.md|MarkdownLink|b",
            "alpha.md|MarkdownLink|z",
            "alpha.md|WikiLink|z",
            "beta.md|WikiLink|x",
        ]);
    }

    [Fact]
    public void Compare_SummaryThatListsALinkTwice_ListsItOnce()
    {
        ChangesSince changes = Compare(
            Before([("a.md", 1, 0)], new BrokenLink("a.md", LinkKind.WikiLink, "z"), new BrokenLink("a.md", LinkKind.WikiLink, "z")),
            Now("a.md", 1, 0));

        changes.NoLongerBroken.Count.ShouldBe(1);
    }
}
