using System.Diagnostics;
using Microsoft.Extensions.Logging;
using Pusula.Browse;
using Pusula.UnitTests.Support;
using Shouldly;
using Xunit;

namespace Pusula.UnitTests.Browse;

// A call of the file system that does not return (a disk that sleeps, a drive that does not answer) must not hold a request for long: the request is
// answered by its deadline with what the search had found by then, and the search that is stuck is given up on. The helpers are those of
// FolderFinderTests (the other half of this class).
public sealed partial class FolderFinderTests
{
    // Short limits, so that a deadline is a matter of fractions of a second: 0.6 s for the search and 0.2 s for the counting, each with 0.2 s of margin.
    private static readonly BrowseLimits Quick = BrowseLimits.Default with
    {
        SearchTime = TimeSpan.FromMilliseconds(600),
        CountTime = TimeSpan.FromMilliseconds(200),
        DeadlineMargin = TimeSpan.FromMilliseconds(200),
    };

    // The file system as the search sees it, where one folder does not answer: listing it does not return until the test lets it go. Nothing is
    // stuck for ever: a stall ends by itself after 30 seconds. Without a folder to stall on it only remembers which folders were listed, and how often.
    private sealed class StalledFolder(string? folder)
    {
        private readonly TaskCompletionSource _reached = new();
        private readonly TaskCompletionSource _released = new();
        private readonly List<string> _listed = [];

        /// <summary>Whether the search is in the call that does not return.</summary>
        public bool IsReached => _reached.Task.IsCompleted;

        public IEnumerable<FileSystemInfo> Listing(string path)
        {
            lock (_listed)
            {
                _listed.Add(path);
            }

            if (path == folder)
            {
                _reached.TrySetResult();
                _released.Task.Wait(TimeSpan.FromSeconds(30));
            }

            return FolderWalk.SystemEntries(path);
        }

        public bool WaitReached(TimeSpan timeout, CancellationToken cancellationToken) => _reached.Task.Wait(timeout, cancellationToken);

        public void Release() => _released.TrySetResult();

        public int Listings(string path)
        {
            lock (_listed)
            {
                return _listed.Count(listed => listed == path);
            }
        }
    }

    private static DriveFolders Drive(TempDirectory temp) => new([new SearchRoot(temp.Resolve("drive"), MaxDepth: 4)]);

    // Asks again and again until a search that ended has the answer: while the search that was given up on is still on its way out, the answer is the snapshot.
    private static FoundFolders CompleteSearch(FolderFinder finder)
    {
        FoundFolders? found = null;
        SpinWait.SpinUntil(() => (found = finder.Find(TestContext.Current.CancellationToken)).Complete, TimeSpan.FromSeconds(15)).ShouldBeTrue("A search that is complete should have been made once the call returned.");
        return found!;
    }

    [Fact]
    public void Find_CallOfTheFileSystemThatDoesNotReturn_IsNotWaitedForAndTheFoldersFoundBeforeItAreGiven()
    {
        using var temp = new TempDirectory();
        MakeVault(temp, "home/Vault", "n01.md");
        MakeNotes(temp, "home/Notes", notes: 12);
        MakeNotes(temp, "home/p/q/r/Stuck", notes: 12);
        MakeVault(temp, "drive/Dvault");
        MakeNotes(temp, "drive/a/b/Docs", notes: 12);
        MakeNotes(temp, "drive/a/b/c/Deep", notes: 12);
        string home = Home(temp);
        string stuck = temp.Resolve("home/p/q/r/Stuck");
        var stalled = new StalledFolder(stuck);
        var logger = new CapturingLogger<FolderFinder>();
        FolderFinder finder = Finder(temp, Drive(temp), Quick, logger: logger, listing: stalled.Listing);
        try
        {
            long started = Stopwatch.GetTimestamp();
            FoundFolders found = finder.Find(TestContext.Current.CancellationToken);
            TimeSpan took = Stopwatch.GetElapsedTime(started);

            // The call is asking the folder at the last level whether it has an entry note, is not released, and would not return for 30 seconds: the answer came
            // by the deadline all the same, with the vaults the walk had met and the folders of notes that the candidates had given. Nothing was counted.
            stalled.IsReached.ShouldBeTrue();
            took.ShouldBeLessThan(TimeSpan.FromSeconds(10));
            found.Complete.ShouldBeFalse();
            found.Folders.Select(folder => (folder.Name, folder.Kind)).ShouldBe(
                [("Docs", FolderKind.Notes), ("Dvault", FolderKind.Vault), ("Notes", FolderKind.Notes), ("Vault", FolderKind.Vault)]);
            found.Folders.ShouldAllBe(folder => folder.MarkdownCount == null && folder.Listed == false);
            logger.Entries.ShouldContain(entry => entry.Level == LogLevel.Warning && entry.Message.Contains("did not answer while searching", StringComparison.Ordinal));

            // A request while the call is stuck gets the same answer at once, and no second search starts on the same file system.
            FoundFolders second = finder.Find(TestContext.Current.CancellationToken);
            second.ShouldBeSameAs(found);
            stalled.Listings(home).ShouldBe(1);
            stalled.Listings(stuck).ShouldBe(1);
            logger.Entries.Count(entry => entry.Level == LogLevel.Warning).ShouldBe(1);

            // When the call has returned, the search that was given up on ends and what it had is not kept: the next request searches anew, and is complete.
            // (The folder at the last level of the drive is not asked, whatever the file system does.)
            stalled.Release();
            FoundFolders next = CompleteSearch(finder);
            Paths(next).ShouldBe(
                [temp.Resolve("drive/a/b/Docs"), temp.Resolve("drive/Dvault"), temp.Resolve("home/Notes"), stuck, temp.Resolve("home/Vault")],
                ignoreOrder: true);
            next.Folders.ShouldAllBe(folder => folder.MarkdownCount != null);
            stalled.Listings(home).ShouldBe(2);
            finder.Find(TestContext.Current.CancellationToken).ShouldBeSameAs(next);
        }
        finally
        {
            stalled.Release();
        }
    }

    [Fact]
    public void Find_CallOfTheFileSystemThatDoesNotReturnWhileCounting_IsWaitedForOnlyAsLongAsTheCountingMayTake()
    {
        using var temp = new TempDirectory();
        MakeVault(temp, "home/Vault", "n01.md");
        string vault = temp.Resolve("home/Vault");
        var stalled = new StalledFolder(vault);
        var logger = new CapturingLogger<FolderFinder>();

        // The search may take five seconds here and the counting 0.2 s: a request that waited as long for the counting as for the search would wait five seconds.
        FolderFinder finder = Finder(temp, limits: Quick with { SearchTime = TimeSpan.FromSeconds(5) }, logger: logger, listing: stalled.Listing);
        try
        {
            long started = Stopwatch.GetTimestamp();
            FoundFolders found = finder.Find(TestContext.Current.CancellationToken);
            TimeSpan took = Stopwatch.GetElapsedTime(started);

            // The vault was found by the walk, which does not enter it; the call that does not return is the one that counts what is in it.
            stalled.IsReached.ShouldBeTrue();
            took.ShouldBeLessThan(TimeSpan.FromSeconds(3));
            found.Complete.ShouldBeFalse();
            Paths(found).ShouldBe([vault]);
            found.Folders.Single().MarkdownCount.ShouldBeNull();
            logger.Entries.ShouldContain(entry => entry.Level == LogLevel.Warning && entry.Message.Contains("did not answer while counting", StringComparison.Ordinal));

            // The search that was given up on counts what is in the vault once the call returns, and that is thrown away: the next request searches anew.
            stalled.Release();
            FoundFolders next = CompleteSearch(finder);
            next.Folders.Single().MarkdownCount.ShouldBe(1);
            stalled.Listings(Home(temp)).ShouldBe(2);
        }
        finally
        {
            stalled.Release();
        }
    }

    [Fact]
    public void Find_CallOfTheFileSystemThatDoesNotReturnInTheWalk_GivesTheVaultsThatTheWalkHadMetByThen()
    {
        using var temp = new TempDirectory();
        MakeVault(temp, "home/Vault");
        MakeVault(temp, "home/a/b/Deeper");
        temp.CreateDirectory("drive/Slow");
        var stalled = new StalledFolder(temp.Resolve("drive/Slow"));
        FolderFinder finder = Finder(temp, Drive(temp), Quick, listing: stalled.Listing);
        try
        {
            FoundFolders found = finder.Find(TestContext.Current.CancellationToken);

            // The walk goes level by level over the home directory and the drive together: when it is in the first folder of the drive that does not answer, it has met the
            // vault in the first level of the home directory, and has not got as far as the one three levels down.
            stalled.IsReached.ShouldBeTrue();
            found.Complete.ShouldBeFalse();
            Paths(found).ShouldBe([temp.Resolve("home/Vault")]);

            stalled.Release();
            Paths(CompleteSearch(finder)).ShouldBe([temp.Resolve("home/Vault"), temp.Resolve("home/a/b/Deeper")], ignoreOrder: true);
        }
        finally
        {
            stalled.Release();
        }
    }

    [Fact]
    public void Find_StuckCallAfterAFolderOfNotesWithAVaultInside_GivesTheVaultAndNotTheFolderAroundItUntilTheVaultHasBeenLookedInto()
    {
        using var temp = new TempDirectory();
        MakeNotes(temp, "home/Project", notes: 12);
        MakeVault(temp, "home/Project/Vault");
        WriteNotes(temp, "home/Project/Vault", "v", count: 2, linked: 2);
        MakeNotes(temp, "home/p/q/r/Stuck", notes: 12);
        string stuck = temp.Resolve("home/p/q/r/Stuck");
        var stalled = new StalledFolder(stuck);
        FolderFinder finder = Finder(temp, limits: Quick, listing: stalled.Listing);
        try
        {
            FoundFolders found = finder.Find(TestContext.Current.CancellationToken);

            // What the vault holds of the notes of the project is not known yet, which keeps the project out, as a budget that ran out would.
            found.Complete.ShouldBeFalse();
            Paths(found).ShouldBe([temp.Resolve("home/Project/Vault")]);

            stalled.Release();
            FoundFolders next = CompleteSearch(finder);

            // The vault holds two of the fourteen wikilinked notes: the project is listed next to it.
            Paths(next).ShouldBe([temp.Resolve("home/Project"), temp.Resolve("home/Project/Vault"), stuck], ignoreOrder: true);
        }
        finally
        {
            stalled.Release();
        }
    }

    [Fact]
    public void Find_StuckCallAfterAFolderOfNotesWithSubfoldersOfNotes_GivesTheFolderAndNotItsSubfoldersAsTheFinalAnswerDoes()
    {
        using var temp = new TempDirectory();
        MakeDocsWithTwoSubfolders(temp, "home/Docs");
        MakeNotes(temp, "home/p/q/r/Stuck", notes: 12);
        string stuck = temp.Resolve("home/p/q/r/Stuck");
        var stalled = new StalledFolder(stuck);
        FolderFinder finder = Finder(temp, limits: Quick, listing: stalled.Listing);
        try
        {
            FoundFolders found = finder.Find(TestContext.Current.CancellationToken);

            // What the candidates gave is settled the same way as the final answer: Guide and Reference lie inside Docs, which is listed, and are part of it.
            found.Complete.ShouldBeFalse();
            Paths(found).ShouldBe([temp.Resolve("home/Docs")]);

            stalled.Release();
            Paths(CompleteSearch(finder)).ShouldBe([temp.Resolve("home/Docs"), stuck], ignoreOrder: true);
        }
        finally
        {
            stalled.Release();
        }
    }

    [Fact]
    public void Find_StuckCallWhileTheVaultInsideAFolderOfNotesIsNotLookedInto_ListsTheFolderOfNotesInsideItAndTheVaultAndLaterOnlyTheFolderAroundThem()
    {
        using var temp = new TempDirectory();
        MakeNotes(temp, "home/Project", notes: 12);
        MakeNotes(temp, "home/Project/Guide", notes: 12);
        MakeVault(temp, "home/Project/Vault");
        WriteNotes(temp, "home/Project/Vault", "v", count: 2, linked: 2);
        MakeNotes(temp, "home/p/q/r/Stuck", notes: 12);
        string stuck = temp.Resolve("home/p/q/r/Stuck");
        var stalled = new StalledFolder(stuck);
        FolderFinder finder = Finder(temp, limits: Quick, listing: stalled.Listing);
        try
        {
            FoundFolders found = finder.Find(TestContext.Current.CancellationToken);

            // What the vault holds of the notes of Project is not known yet, which keeps Project out; Guide does not lie inside a folder of notes that is listed.
            Paths(found).ShouldBe([temp.Resolve("home/Project/Guide"), temp.Resolve("home/Project/Vault")], ignoreOrder: true);

            // The vault holds 2 of the 26 wikilinked notes of Project and Guide 12: Project is listed, with the vault next to it, and Guide is part of Project.
            stalled.Release();
            Paths(CompleteSearch(finder)).ShouldBe([temp.Resolve("home/Project"), temp.Resolve("home/Project/Vault"), stuck], ignoreOrder: true);
        }
        finally
        {
            stalled.Release();
        }
    }

    [Fact]
    public async Task Find_RequestThatIsGoneWhileItWaits_StopsWaitingAndTheSearchGoesOnAndIsRemembered()
    {
        using var temp = new TempDirectory();
        MakeVault(temp, "home/Vault", "n01.md");
        string vault = temp.Resolve("home/Vault");
        var stalled = new StalledFolder(vault);

        // Time enough for the stall to last: nobody gives up on the search here, the request goes away by itself.
        BrowseLimits patient = Quick with { SearchTime = TimeSpan.FromSeconds(30), CountTime = TimeSpan.FromSeconds(30) };
        FolderFinder finder = Finder(temp, limits: patient, listing: stalled.Listing);
        using var gone = new CancellationTokenSource();
        try
        {
            Task<FoundFolders> request = Task.Run(() => finder.Find(gone.Token), TestContext.Current.CancellationToken);
            stalled.WaitReached(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken).ShouldBeTrue();

            await gone.CancelAsync();
            await Should.ThrowAsync<OperationCanceledException>(request);

            // The search is not the request's: it goes on when the call returns, is remembered, and is what the next request gets.
            stalled.Release();
            FoundFolders found = CompleteSearch(finder);
            Paths(found).ShouldBe([vault]);
            found.Folders.Single().MarkdownCount.ShouldBe(1);
            stalled.Listings(Home(temp)).ShouldBe(1);
        }
        finally
        {
            stalled.Release();
        }
    }

    [Fact]
    public void Find_SearchThatFailsWithSomethingUnexpected_GivesTheFailureToTheRequestAndTheNextRequestSearchesAgain()
    {
        using var temp = new TempDirectory();
        MakeVault(temp, "home/Vault");
        bool fails = true;
        FolderFinder finder = Finder(
            temp,
            listing: path => fails ? throw new InvalidOperationException("The folder cannot be listed.") : FolderWalk.SystemEntries(path));

        Should.Throw<InvalidOperationException>(() => finder.Find(TestContext.Current.CancellationToken)).Message.ShouldBe("The folder cannot be listed.");
        fails = false;
        FoundFolders found = finder.Find(TestContext.Current.CancellationToken);

        Paths(found).ShouldBe([temp.Resolve("home/Vault")]);
        found.Complete.ShouldBeTrue();
    }
}
