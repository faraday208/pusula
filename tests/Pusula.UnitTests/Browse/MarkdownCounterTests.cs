using System.Runtime.Versioning;
using Microsoft.Extensions.Logging.Abstractions;
using Pusula.Browse;
using Pusula.Indexing;
using Pusula.UnitTests.Support;
using Shouldly;
using Xunit;

namespace Pusula.UnitTests.Browse;

public sealed class MarkdownCounterTests
{
    private static readonly TimeSpan Plenty = TimeSpan.FromMinutes(10);

    private static ScanBudget Unlimited() => new(maxEntries: int.MaxValue, Plenty);

    // A folder of notes unless told otherwise; with no limit on the entries one folder may take unless told otherwise.
    private static MarkdownCount? Count(
        string folder,
        int limit = 999,
        ScanBudget? budget = null,
        SourceProfile profile = SourceProfile.Markdown,
        int maxEntries = int.MaxValue) =>
        MarkdownCounter.Count(folder, profile, limit, maxEntries, budget ?? Unlimited(), TestContext.Current.CancellationToken);

    private static int SourceFileCount(string folder, SourceProfile profile) =>
        new IndexBuilder(NullLogger<IndexBuilder>.Instance).Build(folder, profile).Files.Count;

    private static void CreateDirectoryLink(string linkPath, string target)
    {
        try
        {
            Directory.CreateSymbolicLink(linkPath, target);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or PlatformNotSupportedException)
        {
            Assert.Skip($"Symbolic links are not available here: {exception.Message}");
        }
    }

    [Fact]
    public void Count_FolderThatIsALinkIntoTheNetwork_IsNotEnteredAndHasNoCountOfItsOwn()
    {
        using var temp = new TempDirectory();
        temp.Write("home/a.md", "x");
        temp.Write("net/inside/b.md", "x");
        temp.Write("elsewhere/c.md", "x");
        TestLinks.ToFolder(temp.Resolve("home/network"), temp.Resolve("net/inside"));
        TestLinks.ToFolder(temp.Resolve("home/here"), temp.Resolve("elsewhere"));
        using IDisposable network = FakeNetwork.In(temp.Resolve("net"));

        // The folder with the link in it counts what it can read; the link itself is a folder that is not read at all.
        Count(temp.Resolve("home")).ShouldBe(new MarkdownCount(2, More: false));
        Count(temp.Resolve("home/network")).ShouldBeNull();
    }

    [Fact]
    public void Count_MarkdownFilesAtAnyDepth_AreCounted()
    {
        using var temp = new TempDirectory();
        temp.Write("a.md", "x");
        temp.Write("one/b.md", "x");
        temp.Write("one/two/three/c.md", "x");
        temp.Write("other/d.md", "x");

        Count(temp.Path).ShouldBe(new MarkdownCount(4, More: false));
    }

    [Fact]
    public void Count_FolderWithoutMarkdown_IsZero()
    {
        using var temp = new TempDirectory();
        temp.Write("note.txt", "x");
        temp.Write("sub/image.png", "x");
        temp.CreateDirectory("empty");

        Count(temp.Path).ShouldBe(new MarkdownCount(0, More: false));
    }

    [Fact]
    public void Count_OtherFilesAndFoldersCalledMd_AreNotCounted()
    {
        using var temp = new TempDirectory();
        temp.Write("a.md", "x");
        temp.Write("notes.markdown", "x");
        temp.Write("md", "x");
        temp.Write("a.md.txt", "x");
        temp.CreateDirectory("folder.md");

        Count(temp.Path).ShouldBe(new MarkdownCount(1, More: false));
    }

    [Fact]
    public void Count_ExtensionInAnyCase_IsCounted()
    {
        using var temp = new TempDirectory();
        temp.Write("a.MD", "x");
        temp.Write("b.Md", "x");

        Count(temp.Path).ShouldBe(new MarkdownCount(2, More: false));
    }

    [Fact]
    public void Count_HiddenFoldersAndFiles_AreLeftOut()
    {
        using var temp = new TempDirectory();
        temp.Write("visible.md", "x");
        temp.Write(".hidden.md", "x");
        temp.Write(".git/readme.md", "x");
        temp.Write(".obsidian/notes/inside.md", "x");
        temp.Write("sub/.cache/deep.md", "x");
        temp.Write("sub/ok.md", "x");

        Count(temp.Path).ShouldBe(new MarkdownCount(2, More: false));
    }

    [Fact]
    public void Count_NodeModules_IsLeftOutAtAnyDepth()
    {
        using var temp = new TempDirectory();
        temp.Write("a.md", "x");
        temp.Write("node_modules/pkg/README.md", "x");
        temp.Write("app/node_modules/pkg/README.md", "x");
        temp.Write("app/readme.md", "x");

        Count(temp.Path).ShouldBe(new MarkdownCount(2, More: false));
    }

    [Fact]
    public void Count_MoreFilesThanTheLimit_IsTheLimitAndSaysThereAreMore()
    {
        using var temp = new TempDirectory();
        for (int i = 0; i < 5; i++)
        {
            temp.Write($"sub/{i}.md", "x");
        }

        Count(temp.Path, limit: 3).ShouldBe(new MarkdownCount(3, More: true));
    }

    [Fact]
    public void Count_ExactlyTheLimit_DoesNotSayThereAreMore()
    {
        using var temp = new TempDirectory();
        for (int i = 0; i < 3; i++)
        {
            temp.Write($"{i}.md", "x");
        }

        Count(temp.Path, limit: 3).ShouldBe(new MarkdownCount(3, More: false));
    }

    // The limit of the application: nobody reads a count off a list beyond 999.
    [Fact]
    public void Count_ThousandFilesWithTheLimitsOfTheApplication_Is999AndMore()
    {
        using var temp = new TempDirectory();
        for (int i = 0; i < 1000; i++)
        {
            temp.Write($"{i}.md", "x");
        }

        Count(temp.Path, BrowseLimits.Default.MaxMarkdownFiles).ShouldBe(new MarkdownCount(999, More: true));
    }

    [Fact]
    public void Count_NineHundredNinetyNineFiles_IsExactlyThat()
    {
        using var temp = new TempDirectory();
        for (int i = 0; i < 999; i++)
        {
            temp.Write($"{i}.md", "x");
        }

        Count(temp.Path, BrowseLimits.Default.MaxMarkdownFiles).ShouldBe(new MarkdownCount(999, More: false));
    }

    // ---- The entries one folder may take -------------------------------------------------------------------------

    [Fact]
    public void Count_MoreEntriesThanOneFolderMayTake_IsWhatWasCountedSoFarAndSaysThereAreMore()
    {
        using var temp = new TempDirectory();
        for (int i = 0; i < 6; i++)
        {
            temp.Write($"{i}.md", "x");
        }

        Count(temp.Path, maxEntries: 4).ShouldBe(new MarkdownCount(4, More: true));
    }

    // Nothing was counted yet, and there is more to read: "0 and more" is what is known.
    [Fact]
    public void Count_EntriesThatAreNotMarkdownUpToTheCap_IsZeroAndMore()
    {
        using var temp = new TempDirectory();
        for (int i = 0; i < 6; i++)
        {
            temp.Write($"{i}.txt", "x");
        }

        Count(temp.Path, maxEntries: 4).ShouldBe(new MarkdownCount(0, More: true));
    }

    [Fact]
    public void Count_ExactlyTheEntriesOneFolderMayTake_IsTheWholeCount()
    {
        using var temp = new TempDirectory();
        for (int i = 0; i < 3; i++)
        {
            temp.Write($"{i}.md", "x");
        }

        Count(temp.Path, maxEntries: 3).ShouldBe(new MarkdownCount(3, More: false));
        Count(temp.Path, maxEntries: 2).ShouldBe(new MarkdownCount(2, More: true));
    }

    [Fact]
    public void Count_EntriesOfTheFoldersBelow_CountForTheCapOfTheFolder()
    {
        using var temp = new TempDirectory();
        temp.Write("sub/1.md", "x");
        temp.Write("sub/2.md", "x");
        temp.Write("sub/3.md", "x");

        // sub is one entry, its three files three more.
        MarkdownCount count = Count(temp.Path, maxEntries: 3).ShouldNotBeNull();

        count.More.ShouldBeTrue();
        count.Count.ShouldBeLessThan(3);
        Count(temp.Path, maxEntries: 4).ShouldBe(new MarkdownCount(3, More: false));
    }

    [Fact]
    public void Count_BiggerFolderThanTheCap_DoesNotSpendMoreOfTheBudgetThanTheCap()
    {
        using var temp = new TempDirectory();
        for (int i = 0; i < 8; i++)
        {
            temp.Write($"big/{i}.txt", "x");
        }

        temp.Write("small/a.md", "x");
        temp.Write("small/b.md", "x");
        var budget = new ScanBudget(maxEntries: 5, Plenty);

        MarkdownCount? big = Count(temp.Resolve("big"), budget: budget, maxEntries: 3);
        MarkdownCount? small = Count(temp.Resolve("small"), budget: budget, maxEntries: 3);

        // The big folder takes the three entries that it may; the two of the small one are what is left of the budget.
        big.ShouldBe(new MarkdownCount(0, More: true));
        small.ShouldBe(new MarkdownCount(2, More: false));
        budget.IsSpent.ShouldBeFalse();
    }

    [Fact]
    public void Count_BudgetOfTheRequestThatRunsOutBeforeTheCap_IsNotKnown()
    {
        using var temp = new TempDirectory();
        for (int i = 0; i < 8; i++)
        {
            temp.Write($"{i}.md", "x");
        }

        Count(temp.Path, budget: new ScanBudget(maxEntries: 3, Plenty), maxEntries: 5).ShouldBeNull();
    }

    // ---- A Claude Code folder: counted the way a source over it finds its files ---------------------------------

    [Theory]
    [InlineData("plugins")]
    [InlineData("cache")]
    [InlineData("sessions")]
    [InlineData("file-history")]
    [InlineData("todos")]
    [InlineData("shell-snapshots")]
    public void Count_ClaudeFolder_DoesNotEnterTheRuntimeFoldersDirectlyInIt(string runtime)
    {
        using var temp = new TempDirectory();
        temp.Write("CLAUDE.md", "x");
        temp.Write("rules/style.md", "x");
        temp.Write($"{runtime}/ignored.md", "x");
        temp.Write($"{runtime}/deep/ignored-too.md", "x");

        Count(temp.Path, profile: SourceProfile.Claude).ShouldBe(new MarkdownCount(2, More: false));
        Count(temp.Path, profile: SourceProfile.Vault).ShouldBe(new MarkdownCount(4, More: false));
    }

    [Fact]
    public void Count_ClaudeFolder_EntersFoldersWithThoseNamesWhenTheyAreNotDirectlyInIt()
    {
        using var temp = new TempDirectory();
        temp.Write("skills/plugins/readme.md", "x");
        temp.Write("rules/cache/note.md", "x");
        temp.Write("agents/sessions/log.md", "x");

        Count(temp.Path, profile: SourceProfile.Claude).ShouldBe(new MarkdownCount(3, More: false));
    }

    [Fact]
    public void Count_ClaudeFolder_ReadsOnlyTheMemoryOfEveryProject()
    {
        using var temp = new TempDirectory();
        temp.Write("projects/demo/memory/MEMORY.md", "x");
        temp.Write("projects/demo/memory/alpha.md", "x");
        temp.Write("projects/demo/memory/deep/beta.md", "x");
        temp.Write("projects/demo/transcript.md", "x");
        temp.Write("projects/demo/notes/other.md", "x");
        temp.Write("projects/stray.md", "x");
        temp.Write("projects/other/memory/gamma.md", "x");

        Count(temp.Path, profile: SourceProfile.Claude).ShouldBe(new MarkdownCount(4, More: false));
        Count(temp.Path, profile: SourceProfile.Vault).ShouldBe(new MarkdownCount(7, More: false));
    }

    [Fact]
    public void Count_NodeModules_IsReadInAClaudeFolderLikeASourceDoesAndLeftOutInAFolderOfNotes()
    {
        using var temp = new TempDirectory();
        temp.Write("skills/deploy/SKILL.md", "x");
        temp.Write("skills/deploy/node_modules/pkg/README.md", "x");

        Count(temp.Path, profile: SourceProfile.Claude).ShouldBe(new MarkdownCount(2, More: false));
        Count(temp.Path, profile: SourceProfile.Markdown).ShouldBe(new MarkdownCount(1, More: false));
    }

    [Fact]
    public void Count_ClaudeFolder_StillLeavesHiddenFoldersAndFilesOut()
    {
        using var temp = new TempDirectory();
        temp.Write("CLAUDE.md", "x");
        temp.Write(".hidden.md", "x");
        temp.Write(".git/readme.md", "x");
        temp.Write("rules/.draft/old.md", "x");

        Count(temp.Path, profile: SourceProfile.Claude).ShouldBe(new MarkdownCount(1, More: false));
    }

    // The count is what the index of a source says: the number of files that the source over the same folder has.
    [Theory]
    [InlineData(SourceProfile.Claude)]
    [InlineData(SourceProfile.Vault)]
    [InlineData(SourceProfile.Markdown)]
    public void Count_FolderOfEveryKind_IsTheFileCountOfASourceOverIt(SourceProfile profile)
    {
        using var temp = new TempDirectory();
        temp.Write("CLAUDE.md", "x");
        temp.Write("Home.md", "x");
        temp.Write("image.png", "x");
        temp.Write("rules/style.md", "x");
        temp.Write("rules/cache/not-top-level.md", "x");
        temp.Write("skills/deploy/SKILL.md", "x");
        temp.Write("skills/deploy/node_modules/pkg/README.md", "x");
        temp.Write("agents/reviewer.md", "x");
        temp.Write("notes/Plan.md", "x");
        temp.Write("notes/deep/Idea.md", "x");
        temp.Write("plugins/cache/ignored.md", "x");
        temp.Write("sessions/log.md", "x");
        temp.Write("todos/one.md", "x");
        temp.Write("projects/demo/memory/MEMORY.md", "x");
        temp.Write("projects/demo/memory/alpha.md", "x");
        temp.Write("projects/demo/transcript.md", "x");
        temp.Write("projects/demo/other/x.md", "x");
        temp.Write("projects/stray.md", "x");
        temp.Write(".hidden/secret.md", "x");
        temp.Write(".dotfile.md", "x");
        temp.Write("node_modules/dependency/README.md", "x");
        int files = SourceFileCount(temp.Path, profile);

        Count(temp.Path, profile: profile).ShouldBe(new MarkdownCount(files, More: false));

        // The folder is not one that every kind counts alike: the rules are what the numbers differ by.
        files.ShouldBe(profile == SourceProfile.Claude ? 12 : 16);
    }

    [Theory]
    [InlineData(SourceProfile.Claude)]
    [InlineData(SourceProfile.Vault)]
    public void Count_TwoLinksToTheSameFolder_AreCountedForEachLikeASourceDoes(SourceProfile profile)
    {
        using var temp = new TempDirectory();
        temp.Write("real/a.md", "x");
        temp.Write("real/sub/b.md", "x");
        CreateDirectoryLink(temp.Resolve("first"), temp.Resolve("real"));
        CreateDirectoryLink(temp.Resolve("second"), temp.Resolve("real"));
        int files = SourceFileCount(temp.Path, profile);

        Count(temp.Path, profile: profile).ShouldBe(new MarkdownCount(files, More: false));
        files.ShouldBe(6);
    }

    [Theory]
    [InlineData(SourceProfile.Claude)]
    [InlineData(SourceProfile.Vault)]
    public void Count_LinkThatLeadsBackIntoAFolderBeingRead_IsAsManyAsTheSourceHas(SourceProfile profile)
    {
        using var temp = new TempDirectory();
        temp.Write("a.md", "x");
        temp.Write("sub/b.md", "x");
        CreateDirectoryLink(temp.Resolve("sub/loop"), temp.Path);
        CreateDirectoryLink(temp.Resolve("sub/up"), temp.Resolve("sub"));
        int files = SourceFileCount(temp.Path, profile);

        Count(temp.Path, profile: profile).ShouldBe(new MarkdownCount(files, More: false));
    }

    // ---- The budget ---------------------------------------------------------------------------------------------

    [Fact]
    public void Count_FolderBiggerThanTheEntriesOfTheBudget_IsNotKnownAndNoPartOfItIsGiven()
    {
        using var temp = new TempDirectory();
        for (int i = 0; i < 6; i++)
        {
            temp.Write($"{i}.md", "x");
        }

        Count(temp.Path, budget: new ScanBudget(maxEntries: 4, Plenty)).ShouldBeNull();
    }

    [Fact]
    public void Count_EntriesOfTheBudgetThatJustSuffice_IsKnown()
    {
        using var temp = new TempDirectory();
        temp.Write("a.md", "x");
        temp.Write("sub/b.md", "x");

        // Three entries: a.md, sub and sub/b.md.
        Count(temp.Path, budget: new ScanBudget(maxEntries: 3, Plenty)).ShouldBe(new MarkdownCount(2, More: false));
        Count(temp.Path, budget: new ScanBudget(maxEntries: 2, Plenty)).ShouldBeNull();
    }

    [Fact]
    public void Count_BudgetThatIsSpentAlready_IsNotKnownWithoutReadingAnything()
    {
        using var temp = new TempDirectory();
        temp.Write("a.md", "x");
        var budget = new ScanBudget(maxEntries: 1, Plenty);
        budget.TrySpend();
        budget.TrySpend();

        Count(temp.Path, budget: budget).ShouldBeNull();
    }

    [Fact]
    public void Count_NoTime_IsNotKnown()
    {
        using var temp = new TempDirectory();
        temp.Write("a.md", "x");

        Count(temp.Path, budget: new ScanBudget(maxEntries: 100, TimeSpan.Zero)).ShouldBeNull();
    }

    [Fact]
    public void Count_FoldersOfOneRequest_ShareTheBudget()
    {
        using var temp = new TempDirectory();
        temp.Write("first/a.md", "x");
        temp.Write("first/b.md", "x");
        temp.Write("second/c.md", "x");
        var budget = new ScanBudget(maxEntries: 2, Plenty);

        MarkdownCount? first = Count(temp.Resolve("first"), budget: budget);
        MarkdownCount? second = Count(temp.Resolve("second"), budget: budget);

        first.ShouldBe(new MarkdownCount(2, More: false));
        second.ShouldBeNull();
    }

    // ---- The file system ----------------------------------------------------------------------------------------

    [Fact]
    public void Count_FolderThatIsNotThere_HasNoFilesToCount()
    {
        using var temp = new TempDirectory();

        Count(temp.Resolve("gone")).ShouldBe(new MarkdownCount(0, More: false));
    }

    [Fact]
    public void Count_SymbolicLinkThatLeadsBack_IsFollowedOnceAndDoesNotLoop()
    {
        using var temp = new TempDirectory();
        temp.Write("a.md", "x");
        temp.Write("sub/b.md", "x");
        CreateDirectoryLink(temp.Resolve("sub/loop"), temp.Path);
        CreateDirectoryLink(temp.Resolve("self"), temp.Resolve("self"));

        Count(temp.Path).ShouldBe(new MarkdownCount(2, More: false));
    }

    [Fact]
    public void Count_SymbolicLinkToAFolderElsewhere_IsFollowed()
    {
        using var temp = new TempDirectory();
        temp.Write("home/a.md", "x");
        temp.Write("elsewhere/b.md", "x");
        temp.Write("elsewhere/deep/c.md", "x");
        CreateDirectoryLink(temp.Resolve("home/linked"), temp.Resolve("elsewhere"));

        Count(temp.Resolve("home")).ShouldBe(new MarkdownCount(3, More: false));
    }

    [Fact]
    public void Count_FolderThatIsASymbolicLink_IsCountedThroughTheLink()
    {
        using var temp = new TempDirectory();
        temp.Write("real/a.md", "x");
        temp.Write("real/sub/b.md", "x");
        CreateDirectoryLink(temp.Resolve("link"), temp.Resolve("real"));

        Count(temp.Resolve("link")).ShouldBe(new MarkdownCount(2, More: false));
    }

    // Two links that lead to each other are no folders: what is inside is only counted for what it is, a file that is not Markdown.
    [Fact]
    public void Count_SymbolicLinksThatLeadToEachOther_AreNoFoldersAndTheRestIsCounted()
    {
        using var temp = new TempDirectory();
        temp.Write("a.md", "x");
        CreateDirectoryLink(temp.Resolve("a-link"), temp.Resolve("b-link"));
        CreateDirectoryLink(temp.Resolve("b-link"), temp.Resolve("a-link"));

        Count(temp.Path).ShouldBe(new MarkdownCount(1, More: false));
    }

    [Fact]
    public void Count_FolderThatIsASymbolicLinkThatNeverEnds_IsNotKnown()
    {
        using var temp = new TempDirectory();
        CreateDirectoryLink(temp.Resolve("a-link"), temp.Resolve("b-link"));
        CreateDirectoryLink(temp.Resolve("b-link"), temp.Resolve("a-link"));

        Count(temp.Resolve("a-link")).ShouldBeNull();
    }

    [Fact]
    [UnsupportedOSPlatform("windows")]
    public void Count_FolderThatCannotBeRead_IsLeftOutWithoutAnError()
    {
        Assert.SkipWhen(OperatingSystem.IsWindows() || Environment.IsPrivilegedProcess, "Needs POSIX permissions and an unprivileged user.");
        using var temp = new TempDirectory();
        temp.Write("a.md", "x");
        temp.Write("locked/b.md", "x");
        string locked = temp.Resolve("locked");
        File.SetUnixFileMode(locked, UnixFileMode.None);
        try
        {
            Count(temp.Path).ShouldBe(new MarkdownCount(1, More: false));
        }
        finally
        {
            File.SetUnixFileMode(locked, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }
    }

    [Fact]
    public void Count_RequestThatIsGone_StopsTheCounting()
    {
        using var temp = new TempDirectory();
        temp.Write("a.md", "x");
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();

        Should.Throw<OperationCanceledException>(() => MarkdownCounter.Count(temp.Path, SourceProfile.Markdown, 999, int.MaxValue, Unlimited(), cancelled.Token));
    }
}
