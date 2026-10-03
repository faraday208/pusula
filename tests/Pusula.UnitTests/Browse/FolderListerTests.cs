using System.Runtime.Versioning;
using Pusula.Browse;
using Pusula.Indexing;
using Pusula.UnitTests.Support;
using Shouldly;
using Xunit;

namespace Pusula.UnitTests.Browse;

public sealed class FolderListerTests
{
    private static readonly string Separator = Path.DirectorySeparatorChar.ToString();

    private static FolderListing List(
        string folder,
        bool includeHidden = false,
        string? home = null,
        ListedFolders? listed = null,
        BrowseLimits? limits = null) =>
        FolderLister.List(folder, includeHidden, home, listed ?? ListedFolders.None, limits ?? BrowseLimits.Default, TestContext.Current.CancellationToken);

    private static string[] Names(FolderListing listing) => [.. listing.Folders.Select(folder => folder.Name)];

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

    // ---- What is listed -----------------------------------------------------------------------------------------

    [Fact]
    public void List_Folder_HasItsFoldersAndNothingElse()
    {
        using var temp = new TempDirectory();
        temp.CreateDirectory("Documents");
        temp.CreateDirectory("Music");
        temp.Write("secret-plan.txt", "x");
        temp.Write("notes.md", "x");
        temp.Write("Documents/inner-file.md", "x");

        FolderListing listing = List(temp.Path);

        Names(listing).ShouldBe(["Documents", "Music"]);
        listing.Truncated.ShouldBeFalse();
    }

    [Fact]
    public void List_NameOfAFile_IsInNoFieldOfTheListing()
    {
        using var temp = new TempDirectory();
        temp.CreateDirectory("Documents");
        temp.Write("secret-plan.txt", "x");
        temp.Write("Documents/another-secret.md", "x");

        FolderListing listing = List(temp.Path);

        string everything = string.Join('|', listing.Folders.SelectMany(folder => new[] { folder.Name, folder.Path, folder.Display }));
        everything.ShouldNotContain("secret");
        everything.ShouldContain("Documents");
    }

    [Fact]
    public void List_Folder_GivesTheFullPathAndTheNameOfEachFolder()
    {
        using var temp = new TempDirectory();
        string documents = temp.CreateDirectory("Documents");

        BrowseFolder folder = List(temp.Path).Folders.Single();

        folder.Name.ShouldBe("Documents");
        folder.Path.ShouldBe(documents);
        folder.Display.ShouldBe(documents);
    }

    [Fact]
    public void List_FolderBelowTheHome_HasItsDisplayRelativeToTheHome()
    {
        using var temp = new TempDirectory();
        string home = temp.CreateDirectory("home");
        temp.CreateDirectory("home/Documents");

        BrowseFolder folder = List(home, home: home).Folders.Single();

        folder.Display.ShouldBe("~" + Separator + "Documents");
    }

    [Fact]
    public void List_EmptyFolder_HasNothing()
    {
        using var temp = new TempDirectory();

        FolderListing listing = List(temp.Path);

        listing.Folders.ShouldBeEmpty();
        listing.Truncated.ShouldBeFalse();
    }

    [Fact]
    public void List_FolderThatIsNotThere_HasNothing()
    {
        using var temp = new TempDirectory();

        FolderListing listing = List(temp.Resolve("gone"));

        listing.Folders.ShouldBeEmpty();
        listing.Truncated.ShouldBeFalse();
    }

    [Fact]
    public void List_SymbolicLinkToAFolder_IsAFolderOfTheListing()
    {
        using var temp = new TempDirectory();
        temp.CreateDirectory("real");
        temp.Write("a-file.md", "x");
        CreateDirectoryLink(temp.Resolve("link"), temp.Resolve("real"));

        Names(List(temp.Path)).ShouldBe(["link", "real"]);
    }

    // ---- Hidden folders and node_modules ------------------------------------------------------------------------

    [Fact]
    public void List_HiddenFolders_AreLeftOutUnlessAskedFor()
    {
        using var temp = new TempDirectory();
        temp.CreateDirectory("visible");
        temp.CreateDirectory(".git");
        temp.CreateDirectory(".config");

        Names(List(temp.Path)).ShouldBe(["visible"]);
        Names(List(temp.Path, includeHidden: true)).ShouldBe([".config", ".git", "visible"]);
    }

    [Fact]
    public void List_ClaudeFolderOfTheHome_IsListedWhateverHiddenSays()
    {
        using var temp = new TempDirectory();
        string home = temp.CreateDirectory("home");
        temp.CreateDirectory("home/.claude");
        temp.CreateDirectory("home/.ssh");
        temp.CreateDirectory("home/Documents");

        Names(List(home, home: home)).ShouldBe([".claude", "Documents"]);
        Names(List(home, includeHidden: true, home: home)).ShouldBe([".claude", ".ssh", "Documents"]);
    }

    [Fact]
    public void List_ClaudeFolderAnywhereElse_IsHiddenLikeTheOthers()
    {
        using var temp = new TempDirectory();
        string home = temp.CreateDirectory("home");
        string project = temp.CreateDirectory("home/project");
        temp.CreateDirectory("home/project/.claude");
        temp.CreateDirectory("home/project/src");

        Names(List(project, home: home)).ShouldBe(["src"]);
        Names(List(project)).ShouldBe(["src"]);
        Names(List(project, includeHidden: true, home: home)).ShouldBe([".claude", "src"]);
    }

    [Fact]
    public void List_FolderThatIsNotTheHomeButCalledLikeIt_HasNoClaudeException()
    {
        using var temp = new TempDirectory();
        string home = temp.CreateDirectory("home");
        string other = temp.CreateDirectory("other");
        temp.CreateDirectory("other/.claude");

        Names(List(other, home: home)).ShouldBeEmpty();
    }

    [Fact]
    public void List_NodeModules_IsNeverListed()
    {
        using var temp = new TempDirectory();
        temp.CreateDirectory("node_modules");
        temp.CreateDirectory("src");

        Names(List(temp.Path)).ShouldBe(["src"]);
        Names(List(temp.Path, includeHidden: true)).ShouldBe(["src"]);
    }

    [Fact]
    public void List_FolderThatOnlyContainsNodeModulesInItsName_IsListed()
    {
        using var temp = new TempDirectory();
        temp.CreateDirectory("node_modules_backup");
        temp.CreateDirectory("my-node_modules");

        Names(List(temp.Path)).ShouldBe(["my-node_modules", "node_modules_backup"]);
    }

    // ---- Order and limit ----------------------------------------------------------------------------------------

    [Fact]
    public void List_Folders_AreSortedByTheTurkishAlphabetIgnoringCase()
    {
        using var temp = new TempDirectory();
        foreach (string name in new[] { "Zeytin", "Çiçek", "Şeker", "ankara", "Ömür", "Bursa", "İzmir", "ığdır" })
        {
            temp.CreateDirectory(name);
        }

        Names(List(temp.Path)).ShouldBe(["ankara", "Bursa", "Çiçek", "ığdır", "İzmir", "Ömür", "Şeker", "Zeytin"]);
    }

    [Fact]
    public void List_MoreFoldersThanTheLimit_ListsTheFirstOnesInOrderAndSaysItWasCut()
    {
        using var temp = new TempDirectory();
        foreach (string name in new[] { "e", "b", "d", "a", "c" })
        {
            temp.CreateDirectory(name);
        }

        FolderListing listing = List(temp.Path, limits: BrowseLimits.Default with { MaxFolders = 3 });

        Names(listing).ShouldBe(["a", "b", "c"]);
        listing.Truncated.ShouldBeTrue();
    }

    [Fact]
    public void List_ExactlyTheLimit_IsNotCut()
    {
        using var temp = new TempDirectory();
        foreach (string name in new[] { "a", "b", "c" })
        {
            temp.CreateDirectory(name);
        }

        FolderListing listing = List(temp.Path, limits: BrowseLimits.Default with { MaxFolders = 3 });

        Names(listing).ShouldBe(["a", "b", "c"]);
        listing.Truncated.ShouldBeFalse();
    }

    [Fact]
    public void List_HiddenFoldersThatAreLeftOut_DoNotCountForTheLimit()
    {
        using var temp = new TempDirectory();
        temp.CreateDirectory("a");
        temp.CreateDirectory(".b");
        temp.CreateDirectory("node_modules");

        FolderListing listing = List(temp.Path, limits: BrowseLimits.Default with { MaxFolders = 1 });

        Names(listing).ShouldBe(["a"]);
        listing.Truncated.ShouldBeFalse();
    }

    [Fact]
    public void List_LimitOfTheApplication_Is500()
    {
        using var temp = new TempDirectory();
        for (int i = 0; i < 501; i++)
        {
            temp.CreateDirectory($"d{i:D3}");
        }

        FolderListing listing = List(temp.Path, limits: BrowseLimits.Default with { CountEntries = 0 });

        listing.Folders.Count.ShouldBe(500);
        listing.Truncated.ShouldBeTrue();
        listing.Folders[0].Name.ShouldBe("d000");
        listing.Folders[^1].Name.ShouldBe("d499");
    }

    // ---- What a folder looks like -------------------------------------------------------------------------------

    [Fact]
    public void List_FolderWithAnObsidianDirectory_IsAVault()
    {
        using var temp = new TempDirectory();
        temp.CreateDirectory("Vault/.obsidian");
        temp.CreateDirectory("Plain");

        FolderListing listing = List(temp.Path);

        listing.Folders.Select(folder => (folder.Name, folder.Kind)).ShouldBe([("Plain", null), ("Vault", SourceProfile.Vault)]);
    }

    [Fact]
    public void List_ClaudeCodeFolders_AreClaude()
    {
        using var temp = new TempDirectory();
        string home = temp.CreateDirectory("home");
        temp.CreateDirectory("home/.claude");
        temp.Write("home/config/CLAUDE.md", "x");
        temp.CreateDirectory("home/config/rules");
        temp.Write("home/only-claude-md/CLAUDE.md", "x");
        temp.CreateDirectory("home/only-rules/rules");

        FolderListing listing = List(home, home: home);

        listing.Folders.Select(folder => (folder.Name, folder.Kind)).ShouldBe(
        [
            (".claude", SourceProfile.Claude),
            ("config", SourceProfile.Claude),
            ("only-claude-md", null),
            ("only-rules", null),
        ]);
    }

    [Fact]
    public void List_FolderThatHasBothSigns_IsAVaultAsAnAutoSourceWouldBe()
    {
        using var temp = new TempDirectory();
        temp.CreateDirectory("both/.obsidian");
        temp.Write("both/CLAUDE.md", "x");
        temp.CreateDirectory("both/rules");

        List(temp.Path).Folders.Single().Kind.ShouldBe(SourceProfile.Vault);
    }

    // ---- The Markdown count -------------------------------------------------------------------------------------

    [Fact]
    public void List_Folders_HaveTheNumberOfMarkdownFilesBelowThem()
    {
        using var temp = new TempDirectory();
        temp.Write("Notes/a.md", "x");
        temp.Write("Notes/deep/b.md", "x");
        temp.Write("Notes/deep/c.txt", "x");
        temp.CreateDirectory("Empty");

        FolderListing listing = List(temp.Path);

        listing.Folders[0].Name.ShouldBe("Empty");
        listing.Folders[0].MarkdownCount.ShouldBe(0);
        listing.Folders[0].More.ShouldBeNull();
        listing.Folders[1].Name.ShouldBe("Notes");
        listing.Folders[1].MarkdownCount.ShouldBe(2);
        listing.Folders[1].More.ShouldBeNull();
    }

    [Fact]
    public void List_FolderWithMoreFilesThanTheLimit_HasTheLimitAndMore()
    {
        using var temp = new TempDirectory();
        for (int i = 0; i < 5; i++)
        {
            temp.Write($"Big/{i}.md", "x");
        }

        BrowseFolder folder = List(temp.Path, limits: BrowseLimits.Default with { MaxMarkdownFiles = 3 }).Folders.Single();

        folder.MarkdownCount.ShouldBe(3);
        folder.More.ShouldBe(true);
    }

    [Fact]
    public void List_FoldersAfterTheBudgetOfTheRequest_HaveNoCountButAreDescribedAllTheSame()
    {
        using var temp = new TempDirectory();
        temp.Write("a/1.md", "x");
        temp.Write("b/2.md", "x");
        temp.CreateDirectory("b/.obsidian");
        temp.Write("c/3.md", "x");

        // One entry is all the budget has: a/1.md. b is where it runs out.
        FolderListing listing = List(temp.Path, limits: BrowseLimits.Default with { CountEntries = 1 });

        listing.Folders.Select(folder => (folder.Name, folder.MarkdownCount)).ShouldBe([("a", 1), ("b", null), ("c", null)]);
        listing.Folders[1].Kind.ShouldBe(SourceProfile.Vault);
        listing.Folders[1].More.ShouldBeNull();
    }

    [Fact]
    public void List_NoTime_HasNoCountAtAll()
    {
        using var temp = new TempDirectory();
        temp.Write("a/1.md", "x");
        temp.Write("b/2.md", "x");

        FolderListing listing = List(temp.Path, limits: BrowseLimits.Default with { CountTime = TimeSpan.Zero });

        Names(listing).ShouldBe(["a", "b"]);
        listing.Folders.ShouldAllBe(folder => folder.MarkdownCount == null && folder.More == null);
    }

    [Fact]
    public void List_FolderThatTakesMoreEntriesThanOneFolderMay_HasWhatWasCountedSoFarAndMoreWhileTheOthersAreCountedAllTheSame()
    {
        using var temp = new TempDirectory();
        for (int i = 0; i < 6; i++)
        {
            temp.Write($"a-big/{i}.txt", "x");
        }

        temp.Write("b-small/1.md", "x");
        temp.Write("c-small/1.md", "x");
        temp.Write("c-small/2.md", "x");

        FolderListing listing = List(temp.Path, limits: BrowseLimits.Default with { CountEntriesPerFolder = 3 });

        listing.Folders.Select(folder => (folder.Name, folder.MarkdownCount, folder.More)).ShouldBe([("a-big", 0, true), ("b-small", 1, null), ("c-small", 2, null)]);
    }

    [Fact]
    public void List_LimitOfOneFolder_IsTwoThousandEntriesInTheApplication() =>
        BrowseLimits.Default.CountEntriesPerFolder.ShouldBe(2000);

    [Fact]
    public void List_ClaudeCodeFolder_IsCountedLikeASourceOverItFindsItsFiles()
    {
        using var temp = new TempDirectory();
        string home = temp.CreateDirectory("home");
        temp.Write("home/.claude/CLAUDE.md", "x");
        temp.Write("home/.claude/rules/style.md", "x");
        temp.Write("home/.claude/plugins/cache/ignored.md", "x");
        temp.Write("home/.claude/sessions/log.md", "x");
        temp.Write("home/.claude/projects/demo/memory/MEMORY.md", "x");
        temp.Write("home/.claude/projects/demo/transcript.md", "x");
        temp.Write("home/vault/Home.md", "x");
        temp.Write("home/vault/plugins/cache/kept.md", "x");
        temp.CreateDirectory("home/vault/.obsidian");

        FolderListing listing = List(home, home: home);

        // The Claude Code folder has three files, the vault two: its plugins folder is read.
        listing.Folders.Select(folder => (folder.Name, folder.Kind, folder.MarkdownCount)).ShouldBe([(".claude", SourceProfile.Claude, 3), ("vault", SourceProfile.Vault, 2)]);
    }

    // ---- Listed -------------------------------------------------------------------------------------------------

    [Fact]
    public void List_FolderThatASourceShows_IsListedAndTheOthersAreNot()
    {
        using var temp = new TempDirectory();
        string notes = temp.CreateDirectory("notes");
        temp.CreateDirectory("work");
        temp.CreateDirectory("notes/sub");

        FolderListing listing = List(temp.Path, listed: new ListedFolders(new FakeSourceRegistry(notes + Separator)));

        listing.Folders.Select(folder => (folder.Name, folder.Listed)).ShouldBe([("notes", true), ("work", false)]);
        List(notes, listed: new ListedFolders(new FakeSourceRegistry(notes))).Folders.Single().Listed.ShouldBeFalse();
    }

    // ---- The file system ----------------------------------------------------------------------------------------

    [Fact]
    [UnsupportedOSPlatform("windows")]
    public void List_FolderThatCannotBeRead_HasNothingToList()
    {
        Assert.SkipWhen(OperatingSystem.IsWindows() || Environment.IsPrivilegedProcess, "Needs POSIX permissions and an unprivileged user.");
        using var temp = new TempDirectory();
        temp.CreateDirectory("locked/inside");
        string locked = temp.Resolve("locked");
        File.SetUnixFileMode(locked, UnixFileMode.None);
        try
        {
            FolderListing listing = List(locked);

            listing.Folders.ShouldBeEmpty();
            listing.Truncated.ShouldBeFalse();
        }
        finally
        {
            File.SetUnixFileMode(locked, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }
    }

    [Fact]
    public void List_RequestThatIsGone_StopsTheListing()
    {
        using var temp = new TempDirectory();
        temp.CreateDirectory("a");
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();

        Should.Throw<OperationCanceledException>(() => FolderLister.List(temp.Path, includeHidden: false, home: null, ListedFolders.None, BrowseLimits.Default, cancelled.Token));
    }
}
