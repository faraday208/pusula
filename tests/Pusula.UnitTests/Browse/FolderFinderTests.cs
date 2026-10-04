using System.Runtime.Versioning;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Pusula.Browse;
using Pusula.Startup;
using Pusula.UnitTests.Support;
using Shouldly;
using Xunit;

namespace Pusula.UnitTests.Browse;

// Every home directory and drive folder here is made in a scratch directory: the real ones are never looked at.
public sealed partial class FolderFinderTests
{
    private static readonly string Separator = Path.DirectorySeparatorChar.ToString();

    // A home directory (and the scratch directory around it, where drives can be made).
    private static string Home(TempDirectory temp) => temp.CreateDirectory("home");

    private static FolderFinder Finder(
        TempDirectory temp,
        DriveFolders? drives = null,
        BrowseLimits? limits = null,
        TimeProvider? time = null,
        string? home = null,
        ILogger<FolderFinder>? logger = null,
        Func<string, IEnumerable<FileSystemInfo>>? listing = null) =>
        new(
            new UserDirectories(home ?? Home(temp), string.Empty),
            drives ?? DriveFolders.None,
            limits ?? BrowseLimits.Default,
            time ?? TimeProvider.System,
            logger ?? NullLogger<FolderFinder>.Instance)
        {
            Listing = listing,
        };

    private static string[] Paths(FoundFolders found) => [.. found.Folders.Select(folder => folder.Path)];

    private static void MakeVault(TempDirectory temp, string relativePath, params string[] notes)
    {
        temp.CreateDirectory(relativePath + "/.obsidian");
        foreach (string note in notes)
        {
            temp.Write(relativePath + "/" + note, "x");
        }
    }

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

    // ---- What is found ------------------------------------------------------------------------------------------

    [Fact]
    public void Find_VaultsInTheHomeDirectory_AreFoundWithWhatTheyLookLike()
    {
        using var temp = new TempDirectory();
        MakeVault(temp, "home/Documents/Work", "Home.md", "Plan.md", "sub/Idea.md");
        MakeVault(temp, "home/Notes");
        temp.Write("home/Documents/Plain/readme.md", "x");

        FoundFolders found = Finder(temp).Find(TestContext.Current.CancellationToken);

        found.Complete.ShouldBeTrue();
        // By name: Notes before Work, whatever their folders are called.
        found.Folders.Select(folder => (folder.Display, folder.Kind, folder.MarkdownCount, folder.More, folder.Listed)).ShouldBe(
        [
            ("~/Notes".Replace('/', Path.DirectorySeparatorChar), FolderKind.Vault, 0, null, false),
            ("~/Documents/Work".Replace('/', Path.DirectorySeparatorChar), FolderKind.Vault, 3, null, false),
        ]);
        found.Folders[1].Name.ShouldBe("Work");
        found.Folders[1].Path.ShouldBe(temp.Resolve("home/Documents/Work"));
    }

    [Fact]
    public void Find_VaultsDownToTheFourthLevel_AreFoundAndTheFifthIsNot()
    {
        using var temp = new TempDirectory();
        MakeVault(temp, "home/a/b/c/Level4");
        MakeVault(temp, "home/a/b/c/d/Level5");
        MakeVault(temp, "home/Level1");

        FoundFolders found = Finder(temp).Find(TestContext.Current.CancellationToken);

        Paths(found).ShouldBe([temp.Resolve("home/a/b/c/Level4"), temp.Resolve("home/Level1")], ignoreOrder: true);
    }

    [Fact]
    public void Find_TheHomeDirectoryItself_IsNeverFoundEvenWhenItIsAVault()
    {
        using var temp = new TempDirectory();
        temp.CreateDirectory("home/.obsidian");
        MakeVault(temp, "home/Inside");

        FoundFolders found = Finder(temp).Find(TestContext.Current.CancellationToken);

        Paths(found).ShouldBe([temp.Resolve("home/Inside")]);
    }

    [Fact]
    public void Find_InsideAVault_NothingIsSearched()
    {
        using var temp = new TempDirectory();
        MakeVault(temp, "home/Outer", "a.md");
        MakeVault(temp, "home/Outer/Inner");

        FoundFolders found = Finder(temp).Find(TestContext.Current.CancellationToken);

        Paths(found).ShouldBe([temp.Resolve("home/Outer")]);
    }

    [Theory]
    [InlineData(".hidden")]
    [InlineData(".git")]
    [InlineData("node_modules")]
    [InlineData("bin")]
    [InlineData("obj")]
    public void Find_FolderThatIsNotEntered_HidesTheVaultsInIt(string skipped)
    {
        using var temp = new TempDirectory();
        MakeVault(temp, $"home/{skipped}/Vault");
        MakeVault(temp, $"home/project/{skipped}/Vault");
        MakeVault(temp, "home/project/Visible");

        FoundFolders found = Finder(temp).Find(TestContext.Current.CancellationToken);

        Paths(found).ShouldBe([temp.Resolve("home/project/Visible")]);
    }

    [Fact]
    public void Find_FoldersWithTheHiddenOrSystemAttributeOnWindows_AreNotEntered()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "Needs the attributes of Windows.");
        using var temp = new TempDirectory();
        MakeVault(temp, "home/AppData/Local/Vault");
        MakeVault(temp, "home/Config/Vault");
        MakeVault(temp, "home/HiddenVault");
        MakeVault(temp, "home/Documents/Vault");
        File.SetAttributes(temp.Resolve("home/AppData"), FileAttributes.Hidden);
        File.SetAttributes(temp.Resolve("home/Config"), FileAttributes.System);
        File.SetAttributes(temp.Resolve("home/HiddenVault"), FileAttributes.Hidden | FileAttributes.System);

        FoundFolders found = Finder(temp).Find(TestContext.Current.CancellationToken);

        // A vault that is hidden itself is not found, like one inside a hidden folder.
        Paths(found).ShouldBe([temp.Resolve("home/Documents/Vault")]);
        found.Complete.ShouldBeTrue();
    }

    [Fact]
    public void Find_FolderCalledLikeOneThatIsNotEnteredButIsNot_IsSearched()
    {
        using var temp = new TempDirectory();
        MakeVault(temp, "home/binaries/Vault");
        MakeVault(temp, "home/my-obj/Vault");
        MakeVault(temp, "home/node_modules_old/Vault");

        FoundFolders found = Finder(temp).Find(TestContext.Current.CancellationToken);

        found.Folders.Count.ShouldBe(3);
    }

    [Fact]
    public void Find_ClaudeFolderOfTheHome_IsFoundAsAClaudeFolder()
    {
        using var temp = new TempDirectory();
        temp.Write("home/.claude/CLAUDE.md", "x");
        temp.Write("home/.claude/rules/a.md", "x");
        MakeVault(temp, "home/Notes");

        FoundFolders found = Finder(temp).Find(TestContext.Current.CancellationToken);

        found.Folders.Select(folder => (folder.Name, folder.Kind)).ShouldBe([(".claude", FolderKind.Claude), ("Notes", FolderKind.Vault)]);
        found.Folders[0].MarkdownCount.ShouldBe(2);
    }

    [Fact]
    public void Find_NoClaudeFolderInTheHome_IsNotMadeUpAndOneElsewhereIsNotFound()
    {
        using var temp = new TempDirectory();
        temp.Write("home/project/.claude/CLAUDE.md", "x");
        temp.Write("home/.claude", "a file, not a folder");

        FoundFolders found = Finder(temp).Find(TestContext.Current.CancellationToken);

        found.Folders.ShouldBeEmpty();
        found.Complete.ShouldBeTrue();
    }

    [Fact]
    public void Find_VaultThatSourcesShowAlready_IsNotMarkedAsListed()
    {
        using var temp = new TempDirectory();
        MakeVault(temp, "home/Notes");

        // Whether a source shows it is told when the answer is made, not here: what is remembered does not know.
        Finder(temp).Find(TestContext.Current.CancellationToken).Folders.Single().Listed.ShouldBeFalse();
    }

    [Fact]
    public void Find_DriveFolders_AreSearchedToTheirOwnDepth()
    {
        using var temp = new TempDirectory();
        MakeVault(temp, "drives/mnt/usb/Vault");
        MakeVault(temp, "drives/mnt/usb/deeper/Vault");
        MakeVault(temp, "drives/media/user/stick/Vault");
        MakeVault(temp, "drives/media/user/stick/a/Vault");
        var drives = new DriveFolders([new SearchRoot(temp.Resolve("drives/mnt"), MaxDepth: 2), new SearchRoot(temp.Resolve("drives/media"), MaxDepth: 3)]);

        FoundFolders found = Finder(temp, drives).Find(TestContext.Current.CancellationToken);

        Paths(found).ShouldBe([temp.Resolve("drives/media/user/stick/Vault"), temp.Resolve("drives/mnt/usb/Vault")], ignoreOrder: true);
    }

    [Fact]
    public void Find_DriveFolderThatIsNotThere_IsSkippedAndTheHomeDirectoryIsSearchedAllTheSame()
    {
        using var temp = new TempDirectory();
        MakeVault(temp, "home/Notes");
        var drives = new DriveFolders([new SearchRoot(temp.Resolve("no-such-drive"), MaxDepth: 4)]);

        FoundFolders found = Finder(temp, drives).Find(TestContext.Current.CancellationToken);

        Paths(found).ShouldBe([temp.Resolve("home/Notes")]);
        found.Complete.ShouldBeTrue();
    }

    [Fact]
    public void Find_DriveFolderThatItselfIsAVault_IsNotFound()
    {
        using var temp = new TempDirectory();
        temp.CreateDirectory("drive/.obsidian");
        var drives = new DriveFolders([new SearchRoot(temp.Resolve("drive"), MaxDepth: 4)]);

        Finder(temp, drives).Find(TestContext.Current.CancellationToken).Folders.ShouldBeEmpty();
    }

    [Fact]
    public void Find_DriveFolderWithNoDepth_IsNotSearched()
    {
        using var temp = new TempDirectory();
        MakeVault(temp, "drive/Vault");
        var drives = new DriveFolders([new SearchRoot(temp.Resolve("drive"), MaxDepth: 0)]);

        Finder(temp, drives).Find(TestContext.Current.CancellationToken).Folders.ShouldBeEmpty();
    }

    [Fact]
    public void Find_NamesThatARootSkipsAtItsTop_AreNotEnteredAndNotFoundThere()
    {
        using var temp = new TempDirectory();
        MakeVault(temp, "drive/System/Vault");
        MakeVault(temp, "drive/Temp-1/Vault");
        MakeVault(temp, "drive/Temp-2/Vault");
        temp.CreateDirectory("drive/Config/.obsidian");
        MakeVault(temp, "drive/Work/Vault");
        var drives = new DriveFolders([new SearchRoot(temp.Resolve("drive"), MaxDepth: 4) { SkippedAtTop = ["System", "Config", "Temp-*"] }]);

        FoundFolders found = Finder(temp, drives).Find(TestContext.Current.CancellationToken);

        // The Config folder is a vault itself and is not found either: what is skipped is neither entered nor looked at.
        Paths(found).ShouldBe([temp.Resolve("drive/Work/Vault")]);
        found.Complete.ShouldBeTrue();
    }

    [Fact]
    public void Find_NameThatARootSkipsAtItsTop_IsSearchedFurtherDown()
    {
        using var temp = new TempDirectory();
        MakeVault(temp, "drive/System/Vault");
        MakeVault(temp, "drive/Work/System/Vault");
        MakeVault(temp, "drive/Work/Temp-1/Vault");
        var drives = new DriveFolders([new SearchRoot(temp.Resolve("drive"), MaxDepth: 4) { SkippedAtTop = ["System", "Temp-*"] }]);

        FoundFolders found = Finder(temp, drives).Find(TestContext.Current.CancellationToken);

        Paths(found).ShouldBe([temp.Resolve("drive/Work/System/Vault"), temp.Resolve("drive/Work/Temp-1/Vault")], ignoreOrder: true);
    }

    [Fact]
    public void Find_NamesThatOneRootSkips_AreSearchedInTheOtherRootsAndInTheHomeDirectory()
    {
        using var temp = new TempDirectory();
        MakeVault(temp, "home/System/Vault");
        MakeVault(temp, "first/System/Vault");
        MakeVault(temp, "second/System/Vault");
        var drives = new DriveFolders(
        [
            new SearchRoot(temp.Resolve("first"), MaxDepth: 4) { SkippedAtTop = ["System"] },
            new SearchRoot(temp.Resolve("second"), MaxDepth: 4),
        ]);

        FoundFolders found = Finder(temp, drives).Find(TestContext.Current.CancellationToken);

        Paths(found).ShouldBe([temp.Resolve("home/System/Vault"), temp.Resolve("second/System/Vault")], ignoreOrder: true);
    }

    [Fact]
    public void Find_DriveOfWindows_IsSearchedFourLevelsDeepAndNotInItsSystemFolders()
    {
        using var temp = new TempDirectory();
        string[] systemFolders =
        [
            "Windows", "Program Files", "Program Files (x86)", "ProgramData", "Users", "PerfLogs", "Recovery", "System Volume Information",
            "$Recycle.Bin", "$WinREAgent",
        ];
        foreach (string name in systemFolders)
        {
            MakeVault(temp, $"drive/{name}/Vault");
        }

        MakeVault(temp, "drive/a/b/c/Vault");
        MakeVault(temp, "drive/a/b/c/d/TooDeep");
        MakeVault(temp, "drive/Documents/Vault");
        MakeVault(temp, "drive/work/Users/Vault");
        DriveFolders drives = DriveFolders.ForWindowsDrives([temp.Resolve("drive")]);

        FoundFolders found = Finder(temp, drives).Find(TestContext.Current.CancellationToken);

        // Level 4 is the deepest, like below /mnt; a system folder further down is a folder like any other.
        Paths(found).ShouldBe(
            [temp.Resolve("drive/a/b/c/Vault"), temp.Resolve("drive/Documents/Vault"), temp.Resolve("drive/work/Users/Vault")],
            ignoreOrder: true);
        found.Complete.ShouldBeTrue();
    }

    [Fact]
    public void Find_NoHomeDirectory_SearchesTheDriveFoldersOnly()
    {
        using var temp = new TempDirectory();
        MakeVault(temp, "drive/Vault");
        var drives = new DriveFolders([new SearchRoot(temp.Resolve("drive"), MaxDepth: 4)]);

        FoundFolders found = Finder(temp, drives, home: string.Empty).Find(TestContext.Current.CancellationToken);

        Paths(found).ShouldBe([temp.Resolve("drive/Vault")]);
        found.Folders.Single().Display.ShouldBe(temp.Resolve("drive/Vault"));
    }

    [Fact]
    public void Find_HomeDirectoryThatIsNotThere_FindsNothingAndIsComplete()
    {
        using var temp = new TempDirectory();

        FoundFolders found = Finder(temp, home: temp.Resolve("gone")).Find(TestContext.Current.CancellationToken);

        found.Folders.ShouldBeEmpty();
        found.Complete.ShouldBeTrue();
    }

    [Fact]
    public void Find_Folders_AreSortedByNameByTheTurkishAlphabetIgnoringCase()
    {
        using var temp = new TempDirectory();
        MakeVault(temp, "home/Zeta");
        MakeVault(temp, "home/Çiçek");
        MakeVault(temp, "home/alpha");
        MakeVault(temp, "drive/Vault");
        temp.CreateDirectory("home/.claude");
        var drives = new DriveFolders([new SearchRoot(temp.Resolve("drive"), MaxDepth: 4)]);

        FoundFolders found = Finder(temp, drives).Find(TestContext.Current.CancellationToken);

        // The name decides, wherever the folder is: a dot first, then the Turkish alphabet; the drive's Vault comes between Çiçek and Zeta.
        found.Folders.Select(folder => folder.Name).ShouldBe([".claude", "alpha", "Çiçek", "Vault", "Zeta"]);
    }

    [Fact]
    public void Find_FoldersWithTheSameName_AreSortedByDisplay()
    {
        using var temp = new TempDirectory();
        MakeVault(temp, "home/b/Notes");
        MakeVault(temp, "home/a/Notes");
        MakeVault(temp, "home/c/notes");
        MakeVault(temp, "home/Other");
        MakeVault(temp, "drive/Notes");
        var drives = new DriveFolders([new SearchRoot(temp.Resolve("drive"), MaxDepth: 4)]);

        FoundFolders found = Finder(temp, drives).Find(TestContext.Current.CancellationToken);

        // Notes and notes are the same name; the display tells them apart: "/" sorts before "~", then the folders in the home directory by their paths.
        // A path on Windows starts with the letter of its drive instead, which sorts after "~".
        string drive = temp.Resolve("drive/Notes");
        string[] inTheHome =
        [
            "~/a/Notes".Replace('/', Path.DirectorySeparatorChar),
            "~/b/Notes".Replace('/', Path.DirectorySeparatorChar),
            "~/c/notes".Replace('/', Path.DirectorySeparatorChar),
        ];
        string[] sameName = OperatingSystem.IsWindows() ? [.. inTheHome, drive] : [drive, .. inTheHome];
        found.Folders.Select(folder => folder.Display).ShouldBe([.. sameName, "~/Other".Replace('/', Path.DirectorySeparatorChar)]);
    }

    [Fact]
    public void Find_ClaudeFolderOfTheHome_IsCountedLikeASourceOverItFindsItsFiles()
    {
        using var temp = new TempDirectory();
        temp.Write("home/.claude/CLAUDE.md", "x");
        temp.Write("home/.claude/rules/a.md", "x");
        temp.Write("home/.claude/plugins/cache/ignored.md", "x");
        temp.Write("home/.claude/projects/demo/memory/MEMORY.md", "x");
        temp.Write("home/.claude/projects/demo/transcript.md", "x");

        BrowseFolder claude = Finder(temp).Find(TestContext.Current.CancellationToken).Folders.Single();

        claude.Kind.ShouldBe(FolderKind.Claude);
        claude.MarkdownCount.ShouldBe(3);
        claude.More.ShouldBeNull();
    }

    [Fact]
    public void Find_FolderThatTakesMoreEntriesThanOneFolderMay_HasWhatWasCountedAndMoreWhileTheNextOneIsCounted()
    {
        using var temp = new TempDirectory();
        MakeVault(temp, "home/a-big");
        for (int i = 0; i < 6; i++)
        {
            temp.Write($"home/a-big/{i}.txt", "x");
        }

        MakeVault(temp, "home/b-small", "1.md");

        FoundFolders found = Finder(temp, limits: BrowseLimits.Default with { CountEntriesPerFolder = 3 }).Find(TestContext.Current.CancellationToken);

        found.Folders.Select(folder => (folder.Name, folder.MarkdownCount, folder.More)).ShouldBe([("a-big", 0, true), ("b-small", 1, null)]);
    }

    // ---- Symbolic links -----------------------------------------------------------------------------------------

    [Fact]
    public void Find_SymbolicLinkToAFolderWithAVault_IsFollowed()
    {
        using var temp = new TempDirectory();
        MakeVault(temp, "elsewhere/Notes");
        temp.CreateDirectory("home");
        CreateDirectoryLink(temp.Resolve("home/linked"), temp.Resolve("elsewhere"));

        FoundFolders found = Finder(temp).Find(TestContext.Current.CancellationToken);

        Paths(found).ShouldBe([temp.Resolve("home/linked/Notes")]);
    }

    [Fact]
    public void Find_VaultThatTwoPathsLeadTo_IsFoundOnceByTheFirstOne()
    {
        using var temp = new TempDirectory();
        MakeVault(temp, "home/Notes");
        CreateDirectoryLink(temp.Resolve("home/alias"), temp.Resolve("home/Notes"));
        CreateDirectoryLink(temp.Resolve("home/up"), temp.Resolve("home"));

        FoundFolders found = Finder(temp).Find(TestContext.Current.CancellationToken);

        found.Folders.Count.ShouldBe(1);
        found.Complete.ShouldBeTrue();
    }

    // ---- The budget ---------------------------------------------------------------------------------------------

    [Fact]
    public void Find_MoreEntriesThanTheBudget_IsNotCompleteAndFindsWhatIsNearestFirst()
    {
        using var temp = new TempDirectory();
        MakeVault(temp, "home/Near");
        MakeVault(temp, "home/a/b/c/Far");
        for (int i = 0; i < 20; i++)
        {
            temp.CreateDirectory($"home/filler{i:D2}/inner");
        }

        // Breadth first: the folders of the first level are read before any below them.
        FoundFolders found = Finder(temp, limits: BrowseLimits.Default with { SearchEntries = 25 }).Find(TestContext.Current.CancellationToken);

        found.Complete.ShouldBeFalse();
        Paths(found).ShouldBe([temp.Resolve("home/Near")]);
    }

    [Fact]
    public void Find_EntriesThatJustSuffice_IsComplete()
    {
        using var temp = new TempDirectory();
        temp.CreateDirectory("home/one");
        temp.CreateDirectory("home/two");
        MakeVault(temp, "home/three");

        // Three entries in the home directory and none in the folders below it.
        Finder(temp, limits: BrowseLimits.Default with { SearchEntries = 3 }).Find(TestContext.Current.CancellationToken).Complete.ShouldBeTrue();
        Finder(temp, limits: BrowseLimits.Default with { SearchEntries = 2 }).Find(TestContext.Current.CancellationToken).Complete.ShouldBeFalse();
    }

    [Fact]
    public void Find_NoTime_IsNotComplete()
    {
        using var temp = new TempDirectory();
        MakeVault(temp, "home/Notes");

        FoundFolders found = Finder(temp, limits: BrowseLimits.Default with { SearchTime = TimeSpan.Zero }).Find(TestContext.Current.CancellationToken);

        found.Complete.ShouldBeFalse();
        found.Folders.ShouldBeEmpty();
    }

    [Fact]
    public void Find_ClaudeFolderOfTheHome_IsFoundWhateverTheBudgetOfTheSearch()
    {
        using var temp = new TempDirectory();
        temp.CreateDirectory("home/.claude");
        MakeVault(temp, "home/Notes");

        FoundFolders found = Finder(temp, limits: BrowseLimits.Default with { SearchEntries = 0 }).Find(TestContext.Current.CancellationToken);

        found.Complete.ShouldBeFalse();
        found.Folders.Select(folder => folder.Name).ShouldBe([".claude"]);
    }

    [Fact]
    public void Find_CountsOfTheFoundFolders_ShareOneBudgetInTheOrderOfTheList()
    {
        using var temp = new TempDirectory();
        MakeVault(temp, "home/alpha", "1.md", "2.md");
        MakeVault(temp, "home/beta", "3.md");

        // Three entries are all the budget has: the .obsidian and the two notes of alpha. beta is where it runs out.
        FoundFolders found = Finder(temp, limits: BrowseLimits.Default with { CountEntries = 3 }).Find(TestContext.Current.CancellationToken);

        found.Complete.ShouldBeTrue();
        found.Folders.Select(folder => (folder.Name, folder.MarkdownCount)).ShouldBe([("alpha", 2), ("beta", null)]);
    }

    [Fact]
    public void Find_VaultWithMoreNotesThanTheLimit_HasTheLimitAndMore()
    {
        using var temp = new TempDirectory();
        MakeVault(temp, "home/Big", "1.md", "2.md", "3.md", "4.md");

        FoundFolders found = Finder(temp, limits: BrowseLimits.Default with { MaxMarkdownFiles = 2 }).Find(TestContext.Current.CancellationToken);

        found.Folders.Single().MarkdownCount.ShouldBe(2);
        found.Folders.Single().More.ShouldBe(true);
    }

    // ---- What is remembered -------------------------------------------------------------------------------------

    [Fact]
    public void Find_SecondTimeWithinTheLifetime_IsTheSameResultWithoutSearchingAgain()
    {
        using var temp = new TempDirectory();
        MakeVault(temp, "home/First");
        var time = new ManualTimeProvider();
        FolderFinder finder = Finder(temp, time: time);

        FoundFolders first = finder.Find(TestContext.Current.CancellationToken);
        MakeVault(temp, "home/Second");
        time.Advance(TimeSpan.FromSeconds(59));
        FoundFolders second = finder.Find(TestContext.Current.CancellationToken);

        second.ShouldBeSameAs(first);
        Paths(second).ShouldBe([temp.Resolve("home/First")]);
    }

    [Fact]
    public void Find_AfterTheLifetime_SearchesAgain()
    {
        using var temp = new TempDirectory();
        MakeVault(temp, "home/First");
        var time = new ManualTimeProvider();
        FolderFinder finder = Finder(temp, time: time);

        FoundFolders first = finder.Find(TestContext.Current.CancellationToken);
        MakeVault(temp, "home/Second");
        time.Advance(TimeSpan.FromSeconds(60));
        FoundFolders second = finder.Find(TestContext.Current.CancellationToken);

        second.ShouldNotBeSameAs(first);
        Paths(second).ShouldBe([temp.Resolve("home/First"), temp.Resolve("home/Second")], ignoreOrder: true);
    }

    [Fact]
    public void Find_LifetimeOfTheApplication_Is60Seconds() =>
        BrowseLimits.Default.CacheLifetime.ShouldBe(TimeSpan.FromSeconds(60));

    [Fact]
    public void Find_MarginOfTheDeadlineOfTheApplication_Is250Milliseconds() =>
        BrowseLimits.Default.DeadlineMargin.ShouldBe(TimeSpan.FromMilliseconds(250));

    [Fact]
    public void Find_SearchThatWasStoppedBecauseTheRequestWasGone_IsNotRemembered()
    {
        using var temp = new TempDirectory();
        MakeVault(temp, "home/Notes");
        FolderFinder finder = Finder(temp);
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();

        Should.Throw<OperationCanceledException>(() => finder.Find(cancelled.Token));
        FoundFolders found = finder.Find(TestContext.Current.CancellationToken);

        Paths(found).ShouldBe([temp.Resolve("home/Notes")]);
    }

    [Fact]
    public async Task Find_RequestsAtTheSameTime_ShareOneSearch()
    {
        using var temp = new TempDirectory();
        MakeVault(temp, "home/Notes");
        FolderFinder finder = Finder(temp);

        FoundFolders[] results = await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => Task.Run(() => finder.Find(TestContext.Current.CancellationToken), TestContext.Current.CancellationToken)));

        results.ShouldAllBe(result => ReferenceEquals(result, results[0]));
    }

    // ---- The file system ----------------------------------------------------------------------------------------

    // A folder that went away while the search was on its way to it (a drive that was unplugged, a folder that was deleted).
    [Fact]
    public void Visit_FolderThatWentAway_IsSkippedWithoutAnError()
    {
        using var temp = new TempDirectory();
        string gone = temp.Resolve("gone");
        var queue = new Queue<FolderFinder.Level>();
        var walked = new FolderFinder.WalkFindings();

        FolderFinder.Visit(new FolderFinder.Level(gone, gone, Depth: 0, MaxDepth: 4), new ScanBudget(maxEntries: 10, TimeSpan.FromMinutes(1)), new HashSet<string>(), queue, walked);

        queue.ShouldBeEmpty();
        walked.Vaults.ShouldBeEmpty();
        walked.Candidates.ShouldBeEmpty();
        walked.Deepest.ShouldBeEmpty();
    }

    [Fact]
    [UnsupportedOSPlatform("windows")]
    public void Find_FolderThatCannotBeRead_IsSearchedNoFurtherAndTheRestIsFound()
    {
        Assert.SkipWhen(OperatingSystem.IsWindows() || Environment.IsPrivilegedProcess, "Needs POSIX permissions and an unprivileged user.");
        using var temp = new TempDirectory();
        MakeVault(temp, "home/locked/Vault");
        MakeVault(temp, "home/Open");
        string locked = temp.Resolve("home/locked");
        File.SetUnixFileMode(locked, UnixFileMode.None);
        try
        {
            FoundFolders found = Finder(temp).Find(TestContext.Current.CancellationToken);

            Paths(found).ShouldBe([temp.Resolve("home/Open")]);
            found.Complete.ShouldBeTrue();
        }
        finally
        {
            File.SetUnixFileMode(locked, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }
    }

    [Fact]
    public void Find_NamesOfFiles_AreInNoFieldOfTheResult()
    {
        using var temp = new TempDirectory();
        MakeVault(temp, "home/Notes", "secret-plan.md");
        temp.Write("home/passwords.txt", "x");
        temp.Write("home/Documents/diary.docx", "x");

        FoundFolders found = Finder(temp).Find(TestContext.Current.CancellationToken);

        string everything = string.Join('|', found.Folders.SelectMany(folder => new[] { folder.Name, folder.Path, folder.Display }));
        everything.ShouldNotContain("secret");
        everything.ShouldNotContain("passwords");
        everything.ShouldNotContain("diary");
    }
}
