using System.Text.Json;
using Pusula.Browse;
using Pusula.UnitTests.Support;
using Shouldly;
using Xunit;

namespace Pusula.UnitTests.Browse;

// The folders of linked notes that have no .obsidian directory, which the search recognizes by what is in them. The helpers are those of
// FolderFinderTests (the other half of this class).
public sealed partial class FolderFinderTests
{
    private const string Linked = "# Note\nSee [[n02]] for more.\n";
    private const string Plain = "# Note\nNothing is linked from here.\n";

    // `count` notes named prefix01.md, prefix02.md, ... in a folder; the first `linked` of them have a wikilink.
    private static void WriteNotes(TempDirectory temp, string folder, string prefix, int count, int linked)
    {
        for (int i = 1; i <= count; i++)
        {
            temp.Write($"{folder}/{prefix}{i:D2}.md", i <= linked ? Linked : Plain);
        }
    }

    // A folder of notes with no .obsidian: `notes` Markdown files in all, the entry note being the first of them (none when `entry` is null),
    // `linked` of them (all unless said) with a wikilink, and `others` files that are not Markdown.
    private static void MakeNotes(TempDirectory temp, string folder, int notes, int? linked = null, string? entry = "Home.md", int others = 0)
    {
        int withLinks = linked ?? notes;
        for (int i = 1; i <= notes; i++)
        {
            string name = i == 1 && entry is not null ? entry : $"n{i:D2}.md";
            temp.Write($"{folder}/{name}", i <= withLinks ? Linked : Plain);
        }

        for (int i = 1; i <= others; i++)
        {
            temp.Write($"{folder}/file{i:D2}.png", "x");
        }
    }

    private static BrowseLimits WithSearchEntries(int entries) => BrowseLimits.Default with { SearchEntries = entries };

    // ---- What is found ------------------------------------------------------------------------------------------

    [Fact]
    public void Find_FolderOfLinkedNotesWithoutObsidian_IsFoundAsNotesWithWhatItLooksLike()
    {
        using var temp = new TempDirectory();
        MakeNotes(temp, "home/Documents/Ideas", notes: 12);
        MakeVault(temp, "home/Zeta");

        FoundFolders found = Finder(temp).Find(TestContext.Current.CancellationToken);

        found.Complete.ShouldBeTrue();
        found.Folders.Select(folder => (folder.Name, folder.Kind, folder.MarkdownCount, folder.More, folder.Listed)).ShouldBe(
        [
            ("Ideas", FolderKind.Notes, 12, null, false),
            ("Zeta", FolderKind.Vault, 0, null, false),
        ]);
        found.Folders[0].Path.ShouldBe(temp.Resolve("home/Documents/Ideas"));
        found.Folders[0].Display.ShouldBe("~/Documents/Ideas".Replace('/', Path.DirectorySeparatorChar));
    }

    [Fact]
    public void Find_DocumentationFolderWithAReadmeAndPagesButNoWikilinks_IsNotFound()
    {
        using var temp = new TempDirectory();
        temp.Write("home/docs/README.md", "# Documentation\n");
        WriteNotes(temp, "home/docs", "page", count: 15, linked: 0);
        MakeVault(temp, "home/Vault");

        FoundFolders found = Finder(temp).Find(TestContext.Current.CancellationToken);

        Paths(found).ShouldBe([temp.Resolve("home/Vault")]);
        found.Complete.ShouldBeTrue();
    }

    [Fact]
    public void Find_VaultsWithAnObsidianDirectory_AreStillFoundWhenTheyHaveNoEntryNoteAndNoWikilinks()
    {
        using var temp = new TempDirectory();
        MakeVault(temp, "home/Empty");
        MakeVault(temp, "home/Plain", "a.md", "b.md");

        FoundFolders found = Finder(temp).Find(TestContext.Current.CancellationToken);

        found.Folders.Select(folder => (folder.Name, folder.Kind)).ShouldBe([("Empty", FolderKind.Vault), ("Plain", FolderKind.Vault)]);
    }

    [Theory]
    [InlineData(9, false)]
    [InlineData(10, true)]
    [InlineData(11, true)]
    public void Find_NotesInTheFolder_NeedTenOfThemAtLeast(int notes, bool isFound)
    {
        using var temp = new TempDirectory();
        MakeNotes(temp, "home/Notes", notes);

        Paths(Finder(temp).Find(TestContext.Current.CancellationToken)).ShouldBe(isFound ? [temp.Resolve("home/Notes")] : []);
    }

    [Theory]
    [InlineData(13, false)] // 12 of 25 files are Markdown: 48%
    [InlineData(12, true)] // 12 of 24: exactly half
    [InlineData(0, true)]
    public void Find_FilesThatAreNotMarkdown_MayBeHalfOfTheFilesAtMost(int others, bool isFound)
    {
        using var temp = new TempDirectory();
        MakeNotes(temp, "home/Notes", notes: 12, others: others);

        Paths(Finder(temp).Find(TestContext.Current.CancellationToken)).ShouldBe(isFound ? [temp.Resolve("home/Notes")] : []);
    }

    [Theory]
    [InlineData(20, 3, false)] // 15% of the sample
    [InlineData(20, 4, true)] // exactly a fifth
    [InlineData(10, 1, false)]
    [InlineData(10, 2, true)]
    [InlineData(15, 2, false)]
    [InlineData(15, 3, true)]
    [InlineData(12, 0, false)]
    public void Find_WikilinksInTheSample_NeedAFifthOfItAtLeast(int notes, int linked, bool isFound)
    {
        using var temp = new TempDirectory();
        MakeNotes(temp, "home/Notes", notes, linked);

        Paths(Finder(temp).Find(TestContext.Current.CancellationToken)).ShouldBe(isFound ? [temp.Resolve("home/Notes")] : []);
    }

    [Theory]
    [InlineData("Home.md")]
    [InlineData("home.md")]
    [InlineData("HOME.MD")]
    [InlineData("index.md")]
    [InlineData("Index.md")]
    [InlineData("README.md")]
    [InlineData("readme.md")]
    [InlineData("Readme.MD")]
    [InlineData("MOC.md")]
    [InlineData("moc.md")]
    [InlineData("_index.md")]
    [InlineData("start.md")]
    [InlineData("START.md")]
    public void Find_FolderWithAnEntryNoteOfItsOwn_IsLookedInto(string entry)
    {
        using var temp = new TempDirectory();
        MakeNotes(temp, "home/Notes", notes: 12, entry: null);
        temp.Write($"home/Notes/{entry}", Linked);

        FoundFolders found = Finder(temp).Find(TestContext.Current.CancellationToken);

        Paths(found).ShouldBe([temp.Resolve("home/Notes")]);
    }

    [Theory]
    [InlineData("readme.txt")]
    [InlineData("README.markdown")]
    [InlineData("notes.md")]
    [InlineData("homepage.md")]
    [InlineData("start-here.md")]
    [InlineData("index.html")]
    [InlineData("README")]
    public void Find_FolderWithNoEntryNote_IsNotFoundHoweverManyLinkedNotesItHas(string other)
    {
        using var temp = new TempDirectory();
        MakeNotes(temp, "home/Notes", notes: 12, entry: null);
        temp.Write($"home/Notes/{other}", Linked);

        Finder(temp).Find(TestContext.Current.CancellationToken).Folders.ShouldBeEmpty();
    }

    [Fact]
    public void Find_EntryNoteThatIsInAFolderBelow_DoesNotMakeTheFolderAboveOneToLookInto()
    {
        using var temp = new TempDirectory();
        MakeNotes(temp, "home/Notes", notes: 12, entry: null);
        temp.Write("home/Notes/sub/README.md", Linked);

        Finder(temp).Find(TestContext.Current.CancellationToken).Folders.ShouldBeEmpty();
    }

    [Fact]
    public void Find_EntryNoteThatIsAFolder_IsNone()
    {
        using var temp = new TempDirectory();
        MakeNotes(temp, "home/Notes", notes: 12, entry: null);
        temp.CreateDirectory("home/Notes/README.md");

        Finder(temp).Find(TestContext.Current.CancellationToken).Folders.ShouldBeEmpty();
    }

    // ---- Where it looks ------------------------------------------------------------------------------------------

    [Fact]
    public void Find_FoldersOfNotes_AreFoundDownToTheFourthLevelAsVaultsAreAndTheFifthIsNotLookedAt()
    {
        using var temp = new TempDirectory();
        MakeNotes(temp, "home/Level1", notes: 12);
        MakeNotes(temp, "home/a/b/Level3", notes: 12);
        MakeNotes(temp, "home/a/b/c/Level4", notes: 12);
        MakeNotes(temp, "home/a/b/c/d/Level5", notes: 12);
        MakeVault(temp, "home/a/b/c/VaultLevel4");
        MakeVault(temp, "home/a/b/c/d/VaultLevel5");

        FoundFolders found = Finder(temp).Find(TestContext.Current.CancellationToken);

        // The fourth level is where the search stops: it is not read, so a vault there is found by its .obsidian directory and a folder of notes by asking
        // for an entry note. The fifth level is not looked at, for either of them.
        Paths(found).ShouldBe(
            [
                temp.Resolve("home/Level1"),
                temp.Resolve("home/a/b/Level3"),
                temp.Resolve("home/a/b/c/Level4"),
                temp.Resolve("home/a/b/c/VaultLevel4"),
            ],
            ignoreOrder: true);
        found.Complete.ShouldBeTrue();
    }

    [Fact]
    public void Find_FolderOfNotesAtTheLastLevelOfARootThatGoesLessDeepAndAsksItsLastLevel_IsFound()
    {
        using var temp = new TempDirectory();
        MakeNotes(temp, "volumes/a/b/Level3", notes: 12);
        MakeNotes(temp, "volumes/a/b/c/Level4", notes: 12);
        var drives = new DriveFolders([new SearchRoot(temp.Resolve("volumes"), MaxDepth: 3) { ProbesLastLevel = true }]);

        // The third level is the last one there, as on /Volumes of macOS (which does not ask it, see below), and it is asked: the fourth is not looked at.
        Paths(Finder(temp, drives).Find(TestContext.Current.CancellationToken)).ShouldBe([temp.Resolve("volumes/a/b/Level3")]);
    }

    // ---- Which roots ask their last level --------------------------------------------------------------------------

    [Fact]
    public void Find_FolderOfNotesAtTheLastLevelOfADriveFolder_IsNotFoundWhileOneAboveItIsAndAVaultAtTheLastLevelIs()
    {
        using var temp = new TempDirectory();
        MakeNotes(temp, "drive/a/b/Level3", notes: 12);
        MakeNotes(temp, "drive/a/b/c/Level4", notes: 12);
        MakeVault(temp, "drive/a/b/c/VaultLevel4");
        MakeNotes(temp, "home/a/b/c/HomeLevel4", notes: 12);
        var drives = new DriveFolders([new SearchRoot(temp.Resolve("drive"), MaxDepth: 4)]);

        FoundFolders found = Finder(temp, drives).Find(TestContext.Current.CancellationToken);

        // A drive folder like /mnt does not ask its last level for an entry note (an external disk may sleep or be slow): a folder of notes there is found one
        // level less deep than under the home directory, and a vault at every level.
        Paths(found).ShouldBe(
            [temp.Resolve("drive/a/b/Level3"), temp.Resolve("drive/a/b/c/VaultLevel4"), temp.Resolve("home/a/b/c/HomeLevel4")],
            ignoreOrder: true);
        found.Complete.ShouldBeTrue();
    }

    [Fact]
    public void Find_LastLevelOfADriveFolder_IsNotOpenedAndTheOneUnderTheHomeDirectoryIs()
    {
        using var temp = new TempDirectory();
        temp.CreateDirectory("drive/a/b/c/OnTheDrive");
        temp.CreateDirectory("home/a/b/c/UnderHome");
        var recorder = new StalledFolder(folder: null);
        var drives = new DriveFolders([new SearchRoot(temp.Resolve("drive"), MaxDepth: 4)]);

        Finder(temp, drives, listing: recorder.Listing).Find(TestContext.Current.CancellationToken);

        // The walk reads the folders down to the third level; the last one is opened only to ask it for an entry note, and that is not done on the drive.
        recorder.Listings(temp.Resolve("drive/a/b/c")).ShouldBe(1);
        recorder.Listings(temp.Resolve("drive/a/b/c/OnTheDrive")).ShouldBe(0);
        recorder.Listings(temp.Resolve("home/a/b/c/UnderHome")).ShouldBe(1);
    }

    [Fact]
    public void Find_DriveFolderThatAsksItsLastLevel_FindsFoldersOfNotesThereAsTheSystemDriveOfWindowsDoes()
    {
        using var temp = new TempDirectory();
        MakeNotes(temp, "system/a/b/c/Level4", notes: 12);
        MakeNotes(temp, "other/a/b/c/Level4", notes: 12);
        MakeNotes(temp, "other/a/b/Level3", notes: 12);
        DriveFolders drives = DriveFolders.ForWindowsDrives([temp.Resolve("system"), temp.Resolve("other")], systemDrive: temp.Resolve("system"));

        FoundFolders found = Finder(temp, drives).Find(TestContext.Current.CancellationToken);

        // Both drives are searched four levels down; only the system drive is asked for what is at its last level.
        Paths(found).ShouldBe([temp.Resolve("system/a/b/c/Level4"), temp.Resolve("other/a/b/Level3")], ignoreOrder: true);
    }

    [Theory]
    [InlineData("Home.md")]
    [InlineData("home.md")]
    [InlineData("HOME.MD")]
    [InlineData("index.md")]
    [InlineData("Index.md")]
    [InlineData("README.md")]
    [InlineData("readme.md")]
    [InlineData("Readme.MD")]
    [InlineData("MOC.md")]
    [InlineData("moc.md")]
    [InlineData("_index.md")]
    [InlineData("start.md")]
    [InlineData("START.md")]
    public void Find_FolderAtTheLastLevelWithAnEntryNoteOfItsOwn_IsFoundWhateverTheCaseOfItsName(string entry)
    {
        using var temp = new TempDirectory();
        MakeNotes(temp, "home/a/b/c/Notes", notes: 12, entry: null);
        temp.Write($"home/a/b/c/Notes/{entry}", Linked);

        // Asked for the way the folders above it are read: the case of the name does not matter, on a file system that tells cases apart too.
        Paths(Finder(temp).Find(TestContext.Current.CancellationToken)).ShouldBe([temp.Resolve("home/a/b/c/Notes")]);
    }

    [Theory]
    [InlineData("readme.txt")]
    [InlineData("README.markdown")]
    [InlineData("notes.md")]
    [InlineData("homepage.md")]
    [InlineData("index.html")]
    [InlineData("README")]
    public void Find_FolderAtTheLastLevelWithNoEntryNote_IsNotFound(string other)
    {
        using var temp = new TempDirectory();
        MakeNotes(temp, "home/a/b/c/Notes", notes: 12, entry: null);
        temp.Write($"home/a/b/c/Notes/{other}", Linked);

        Finder(temp).Find(TestContext.Current.CancellationToken).Folders.ShouldBeEmpty();
    }

    [Fact]
    public void Find_FolderAtTheLastLevelThatAnEntryNoteDoesNotMakeAFolderOfNotes_IsNotFound()
    {
        using var temp = new TempDirectory();
        temp.Write("home/a/b/c/Docs/README.md", Plain);
        WriteNotes(temp, "home/a/b/c/Docs", "page", count: 15, linked: 0);
        temp.Write("home/a/b/c/Small/README.md", Linked);
        WriteNotes(temp, "home/a/b/c/Small", "n", count: 3, linked: 3);

        // The same rules as anywhere else: documentation without wikilinks, and a folder with too few notes.
        Finder(temp).Find(TestContext.Current.CancellationToken).Folders.ShouldBeEmpty();
    }

    [Fact]
    public void Find_VaultWithoutObsidianAtTheLastLevelInsideADocsFolder_IsTheVaultAndNotTheDocs()
    {
        using var temp = new TempDirectory();
        temp.Write("home/a/b/docs/README.md", Plain);
        WriteNotes(temp, "home/a/b/docs", "p", count: 15, linked: 0);
        MakeNotes(temp, "home/a/b/docs/guide", notes: 12);

        FoundFolders found = Finder(temp).Find(TestContext.Current.CancellationToken);

        // docs is at the third level and guide, which has all the wikilinks, at the fourth: the one that holds them is listed, as it is
        // when it is a vault with a .obsidian directory.
        found.Folders.Select(folder => (folder.Path, folder.Kind)).ShouldBe([(temp.Resolve("home/a/b/docs/guide"), (FolderKind?)FolderKind.Notes)]);
        found.Complete.ShouldBeTrue();
    }

    [Fact]
    public void Find_FolderOfNotesAtTheLastLevelThatHoldsFewOfTheWikilinksOfTheOneAroundIt_IsPartOfTheOneAroundIt()
    {
        using var temp = new TempDirectory();
        temp.Write("home/a/b/Notes/Home.md", Linked);
        WriteNotes(temp, "home/a/b/Notes", "n", count: 6, linked: 6);
        MakeNotes(temp, "home/a/b/Notes/small", notes: 12, linked: 3);

        FoundFolders found = Finder(temp).Find(TestContext.Current.CancellationToken);

        // The folder around has 19 notes in all, 10 with a wikilink; small, at the fourth level, is a folder of notes too and holds 3 of those 10: not enough to
        // take the place of the one around it, which it lies in, and so it is no row of its own.
        found.Folders.Select(folder => (folder.Name, folder.Kind)).ShouldBe([("Notes", FolderKind.Notes)]);
    }

    [Fact]
    public void Find_RootsOfTheSearch_AreNeverFoundAsFoldersOfNotes()
    {
        using var temp = new TempDirectory();
        MakeNotes(temp, "home", notes: 12);
        MakeNotes(temp, "drive", notes: 12);
        MakeNotes(temp, "drive/Inside", notes: 12);
        var drives = new DriveFolders([new SearchRoot(temp.Resolve("drive"), MaxDepth: 4)]);

        FoundFolders found = Finder(temp, drives).Find(TestContext.Current.CancellationToken);

        Paths(found).ShouldBe([temp.Resolve("drive/Inside")]);
    }

    [Fact]
    public void Find_FolderOfNotesInADriveFolder_IsFoundWithItsFullPath()
    {
        using var temp = new TempDirectory();
        MakeNotes(temp, "drives/mnt/usb/Archive", notes: 11);
        var drives = new DriveFolders([new SearchRoot(temp.Resolve("drives/mnt"), MaxDepth: 4)]);

        BrowseFolder folder = Finder(temp, drives).Find(TestContext.Current.CancellationToken).Folders.Single();

        folder.Kind.ShouldBe(FolderKind.Notes);
        folder.Display.ShouldBe(temp.Resolve("drives/mnt/usb/Archive"));
    }

    [Fact]
    public void Find_NotesBelowTheThirdLevelOfAFolder_AreNotCountedForIt()
    {
        using var temp = new TempDirectory();
        temp.Write("home/Counted/Home.md", Linked);
        WriteNotes(temp, "home/Counted/a/b/c", "n", count: 9, linked: 9);
        temp.Write("home/TooDeep/Home.md", Linked);
        WriteNotes(temp, "home/TooDeep/a/b/c/d", "n", count: 9, linked: 9);

        FoundFolders found = Finder(temp).Find(TestContext.Current.CancellationToken);

        // The first has its ten notes within three levels; the second has one there and nine deeper. The count that is shown goes on at any depth.
        found.Folders.Select(folder => (folder.Name, folder.MarkdownCount)).ShouldBe([("Counted", 10)]);
    }

    [Theory]
    [InlineData(".hidden")]
    [InlineData(".git")]
    [InlineData("node_modules")]
    [InlineData("bin")]
    [InlineData("obj")]
    public void Find_FolderThatIsNotEntered_HidesTheFoldersOfNotesInIt(string skipped)
    {
        using var temp = new TempDirectory();
        MakeNotes(temp, $"home/{skipped}/Notes", notes: 12);
        MakeNotes(temp, $"home/project/{skipped}/Notes", notes: 12);
        MakeNotes(temp, "home/project/Visible", notes: 12);

        Paths(Finder(temp).Find(TestContext.Current.CancellationToken)).ShouldBe([temp.Resolve("home/project/Visible")]);
    }

    [Fact]
    public void Find_NotesInTheFoldersThatAreNotEntered_AreNotCountedForTheFolderAbove()
    {
        using var temp = new TempDirectory();
        temp.Write("home/Project/README.md", Linked);
        WriteNotes(temp, "home/Project/node_modules/package", "n", count: 12, linked: 12);
        WriteNotes(temp, "home/Project/.git", "n", count: 12, linked: 12);

        Finder(temp).Find(TestContext.Current.CancellationToken).Folders.ShouldBeEmpty();
    }

    [Fact]
    public void Find_FolderOfNotesInsideAVault_IsNotFound()
    {
        using var temp = new TempDirectory();
        MakeVault(temp, "home/Vault");
        MakeNotes(temp, "home/Vault/Inner", notes: 12);

        Paths(Finder(temp).Find(TestContext.Current.CancellationToken)).ShouldBe([temp.Resolve("home/Vault")]);
    }

    [Fact]
    public void Find_FolderOfNotesBehindASymbolicLink_IsFoundByTheLink()
    {
        using var temp = new TempDirectory();
        MakeNotes(temp, "elsewhere/Notes", notes: 12);
        temp.CreateDirectory("home");
        CreateDirectoryLink(temp.Resolve("home/linked"), temp.Resolve("elsewhere"));

        Paths(Finder(temp).Find(TestContext.Current.CancellationToken)).ShouldBe([temp.Resolve("home/linked/Notes")]);
    }

    [Fact]
    public void Find_FolderOfNotesThatTwoPathsLeadTo_IsFoundOnce()
    {
        using var temp = new TempDirectory();
        MakeNotes(temp, "home/Notes", notes: 12);
        CreateDirectoryLink(temp.Resolve("home/alias"), temp.Resolve("home/Notes"));
        CreateDirectoryLink(temp.Resolve("home/up"), temp.Resolve("home"));

        FoundFolders found = Finder(temp).Find(TestContext.Current.CancellationToken);

        found.Folders.Count.ShouldBe(1);
        found.Complete.ShouldBeTrue();
    }

    [Fact]
    public void Find_FolderOfNotesThatASourceWouldTakeForAClaudeFolder_KeepsTheKindClaude()
    {
        using var temp = new TempDirectory();
        MakeNotes(temp, "home/Config", notes: 12);
        temp.Write("home/Config/CLAUDE.md", Plain);
        temp.CreateDirectory("home/Config/rules");

        FoundFolders found = Finder(temp).Find(TestContext.Current.CancellationToken);

        // Adding it would make a Claude source of it, and that is what the row says.
        found.Folders.Single().Kind.ShouldBe(FolderKind.Claude);
    }

    [Fact]
    public void Find_FolderOfNotes_IsCountedLikeAnyOtherFoundFolder()
    {
        using var temp = new TempDirectory();
        MakeNotes(temp, "home/Big", notes: 12);
        WriteNotes(temp, "home/Big/deep/er/and/deeper", "d", count: 5, linked: 0);

        BrowseFolder folder = Finder(temp).Find(TestContext.Current.CancellationToken).Folders.Single();

        // The count is the one of any found folder: at any depth, the way a source over it would find them.
        folder.MarkdownCount.ShouldBe(17);
        folder.More.ShouldBeNull();
    }

    [Fact]
    public void Find_ContentOfTheNotes_IsInNoFieldOfTheResult()
    {
        using var temp = new TempDirectory();
        temp.Write("home/Ideas/Home.md", "# Home\nTOP-SECRET-TEXT [[secret-plan]]\n");
        WriteNotes(temp, "home/Ideas", "n", count: 11, linked: 11);
        temp.Write("home/Ideas/n02.md", "# Two\nAlso TOP-SECRET-TEXT with [[secret-plan]]\n");

        FoundFolders found = Finder(temp).Find(TestContext.Current.CancellationToken);

        found.Folders.Count.ShouldBe(1);
        string everything = JsonSerializer.Serialize(found) + string.Join('|', found.Folders.SelectMany(folder => new[] { folder.Name, folder.Path, folder.Display }));
        everything.ShouldNotContain("secret", Case.Insensitive);
        everything.ShouldNotContain("TOP-SECRET", Case.Insensitive);
    }

    // ---- What the walk keeps for the last level ------------------------------------------------------------------

    private static void MakeFoldersForAVisit(TempDirectory temp)
    {
        temp.CreateDirectory("plain");
        temp.CreateDirectory("other");
        temp.CreateDirectory("vault/.obsidian");
        temp.CreateDirectory(".hidden");
        temp.CreateDirectory("node_modules");
        temp.CreateDirectory("bin");
        temp.Write("README.md", Linked);
    }

    [Fact]
    public void Visit_FoldersAtTheLastLevel_AreKeptApartForAskingNotEnteredAndAVaultOrASkippedOneIsNotAmongThem()
    {
        using var temp = new TempDirectory();
        MakeFoldersForAVisit(temp);
        var queue = new Queue<FolderFinder.Level>();
        var walked = new FolderFinder.WalkFindings();

        // The folder is at the third level of a root that goes four levels down and asks its last level: the folders in it are at the fourth, the last.
        FolderFinder.Visit(new FolderFinder.Level(temp.Path, temp.Path, Depth: 3, MaxDepth: 4, ProbesLastLevel: true), new ScanBudget(maxEntries: 100, TimeSpan.FromMinutes(1)), new HashSet<string>(), queue, walked);

        queue.ShouldBeEmpty();
        walked.Vaults.ShouldBe([temp.Resolve("vault")]);
        walked.Deepest.Select(folder => (folder.Path, folder.Depth, folder.MaxDepth)).ShouldBe(
            [(temp.Resolve("plain"), 4, 4), (temp.Resolve("other"), 4, 4)],
            ignoreOrder: true);
        walked.Candidates.Select(folder => folder.Path).ShouldBe([temp.Path]);
    }

    [Fact]
    public void Visit_FoldersAtTheLastLevelOfARootThatDoesNotAskIt_AreLetGoAndOnlyAVaultIsKept()
    {
        using var temp = new TempDirectory();
        MakeFoldersForAVisit(temp);
        var queue = new Queue<FolderFinder.Level>();
        var walked = new FolderFinder.WalkFindings();

        FolderFinder.Visit(new FolderFinder.Level(temp.Path, temp.Path, Depth: 3, MaxDepth: 4), new ScanBudget(maxEntries: 100, TimeSpan.FromMinutes(1)), new HashSet<string>(), queue, walked);

        // Nothing is kept apart to be asked, and nothing is entered; the vault is found by its .obsidian directory, as everywhere.
        queue.ShouldBeEmpty();
        walked.Deepest.ShouldBeEmpty();
        walked.Vaults.ShouldBe([temp.Resolve("vault")]);
        walked.Candidates.Select(folder => folder.Path).ShouldBe([temp.Path]);
    }

    [Fact]
    public void Visit_FoldersAboveTheLastLevel_AreQueuedToBeReadAndNotKeptApart()
    {
        using var temp = new TempDirectory();
        MakeFoldersForAVisit(temp);
        var queue = new Queue<FolderFinder.Level>();
        var walked = new FolderFinder.WalkFindings();

        FolderFinder.Visit(new FolderFinder.Level(temp.Path, temp.Path, Depth: 2, MaxDepth: 4), new ScanBudget(maxEntries: 100, TimeSpan.FromMinutes(1)), new HashSet<string>(), queue, walked);

        queue.Select(folder => (folder.Path, folder.Depth)).ShouldBe([(temp.Resolve("plain"), 3), (temp.Resolve("other"), 3)], ignoreOrder: true);
        walked.Deepest.ShouldBeEmpty();
        walked.Vaults.ShouldBe([temp.Resolve("vault")]);
    }

    [Fact]
    public void Visit_FolderAtTheLastLevelThatIsReachedTwice_IsKeptApartOnce()
    {
        using var temp = new TempDirectory();
        temp.CreateDirectory("real");
        CreateDirectoryLink(temp.Resolve("alias"), temp.Resolve("real"));
        var walked = new FolderFinder.WalkFindings();

        FolderFinder.Visit(new FolderFinder.Level(temp.Path, temp.Path, Depth: 3, MaxDepth: 4, ProbesLastLevel: true), new ScanBudget(maxEntries: 100, TimeSpan.FromMinutes(1)), new HashSet<string>(), new Queue<FolderFinder.Level>(), walked);

        walked.Deepest.Count.ShouldBe(1);
    }

    [Fact]
    public void Visit_BudgetThatRunsOutInTheListing_KeepsApartWhatWasReadAndNothingMore()
    {
        using var temp = new TempDirectory();
        for (int i = 1; i <= 5; i++)
        {
            temp.CreateDirectory($"d{i}");
        }

        var walked = new FolderFinder.WalkFindings();

        FolderFinder.Visit(new FolderFinder.Level(temp.Path, temp.Path, Depth: 3, MaxDepth: 4, ProbesLastLevel: true), new ScanBudget(maxEntries: 2, TimeSpan.FromMinutes(1)), new HashSet<string>(), new Queue<FolderFinder.Level>(), walked);

        // Two entries were read, so two folders are known: the others are not kept apart, and nothing is asked of anything the walk did not read.
        walked.Deepest.Count.ShouldBe(2);
    }

    // ---- Folders inside one another -----------------------------------------------------------------------------

    [Fact]
    public void Find_ProjectFolderWithAReadmeAndAVaultInside_IsTheVaultAndNotTheProject()
    {
        using var temp = new TempDirectory();
        temp.Write("home/Project/README.md", Plain);
        temp.CreateDirectory("home/Project/vault/.obsidian");
        WriteNotes(temp, "home/Project/vault", "n", count: 12, linked: 12);

        FoundFolders found = Finder(temp).Find(TestContext.Current.CancellationToken);

        found.Folders.Select(folder => (folder.Path, folder.Kind)).ShouldBe([(temp.Resolve("home/Project/vault"), (FolderKind?)FolderKind.Vault)]);
        found.Complete.ShouldBeTrue();
    }

    [Fact]
    public void Find_DocumentationFolderWhoseSmallVaultHoldsNearlyAllTheWikilinks_IsTheVaultAndNotTheDocumentation()
    {
        using var temp = new TempDirectory();
        temp.Write("home/docs/README.md", Plain);
        WriteNotes(temp, "home/docs", "p", count: 15, linked: 0);
        temp.CreateDirectory("home/docs/guide/.obsidian");
        WriteNotes(temp, "home/docs/guide", "g", count: 12, linked: 12);

        // The sample of the docs folder has wikilinks only because of the vault in it, which has them all: it is the one that is listed.
        Paths(Finder(temp).Find(TestContext.Current.CancellationToken)).ShouldBe([temp.Resolve("home/docs/guide")]);
    }

    [Fact]
    public void Find_VaultInsideAFolderOfNotesThatHoldsFewOfItsWikilinks_IsListedNextToIt()
    {
        using var temp = new TempDirectory();
        temp.Write("home/Notes/Home.md", Linked);
        WriteNotes(temp, "home/Notes", "n", count: 15, linked: 15);
        temp.CreateDirectory("home/Notes/small/.obsidian");
        WriteNotes(temp, "home/Notes/small", "s", count: 4, linked: 0);

        FoundFolders found = Finder(temp).Find(TestContext.Current.CancellationToken);

        // The vault is never left out, and the folder around it keeps what the vault does not hold.
        found.Folders.Select(folder => (folder.Name, folder.Kind)).ShouldBe([("Notes", FolderKind.Notes), ("small", FolderKind.Vault)]);
    }

    // ---- A folder of notes inside a folder of notes that is listed is part of it ----------------------------------------------

    // A folder of documentation written as linked notes, whose subfolders have an index note and wikilinks of their own: Docs has 36 notes in all and each
    // of the two subfolders 12, a third of the wikilinked notes, not the 60% that would put the subfolder in the place of Docs.
    private static void MakeDocsWithTwoSubfolders(TempDirectory temp, string docs)
    {
        MakeNotes(temp, docs, notes: 12);
        MakeNotes(temp, $"{docs}/Guide", notes: 12);
        MakeNotes(temp, $"{docs}/Reference", notes: 12);
    }

    [Fact]
    public void Find_FolderOfNotesWhoseSubfoldersAreFoldersOfNotesTooAndHoldLittleOfIt_IsOneRowAndNotOneForEachSubfolder()
    {
        using var temp = new TempDirectory();
        MakeDocsWithTwoSubfolders(temp, "home/Docs");

        FoundFolders found = Finder(temp).Find(TestContext.Current.CancellationToken);

        // Guide and Reference are folders of notes by the same rules, and neither holds 60% of the wikilinked notes of Docs: they are part of Docs.
        found.Folders.Select(folder => (folder.Name, folder.Kind)).ShouldBe([("Docs", FolderKind.Notes)]);
        found.Complete.ShouldBeTrue();
    }

    [Fact]
    public void Find_FolderOfNotesInsideAFolderOfNotesInsideAFolderOfNotes_IsPartOfTheOutermostThatIsListed()
    {
        using var temp = new TempDirectory();
        MakeNotes(temp, "home/A", notes: 24);
        MakeNotes(temp, "home/A/B", notes: 12);
        MakeNotes(temp, "home/A/B/C", notes: 12);

        FoundFolders found = Finder(temp).Find(TestContext.Current.CancellationToken);

        // A has 48 notes in all, B 24 (half of A's), C 12 (half of B's): none of them holds 60% of the one around it, and only the outermost is a row.
        Paths(found).ShouldBe([temp.Resolve("home/A")]);
    }

    [Fact]
    public void Find_OuterFolderThatTheFolderInsideItTakesTheirPlaceOf_ListsTheInnerOneAndNotTheFoldersOfNotesInsideThat()
    {
        using var temp = new TempDirectory();
        temp.Write("home/A/README.md", Linked);
        WriteNotes(temp, "home/A", "p", count: 3, linked: 3);
        MakeNotes(temp, "home/A/B", notes: 12);
        MakeNotes(temp, "home/A/B/C", notes: 10);

        FoundFolders found = Finder(temp).Find(TestContext.Current.CancellationToken);

        // A has 26 notes, B 22 and C 10. B holds more than 60% of A's wikilinked notes and is listed in its place; C holds 10 of B's 22, not enough to take B's
        // place, and lies inside B, which is listed: it is part of B.
        Paths(found).ShouldBe([temp.Resolve("home/A/B")]);
    }

    [Fact]
    public void Find_VaultInsideAFolderOfNotesThatIsListed_IsListedNextToItAndSoIsNoFolderOfNotesInsideThatDoes()
    {
        using var temp = new TempDirectory();
        MakeDocsWithTwoSubfolders(temp, "home/Docs");
        temp.CreateDirectory("home/Docs/Archive/.obsidian");
        WriteNotes(temp, "home/Docs/Archive", "a", count: 4, linked: 0);

        FoundFolders found = Finder(temp).Find(TestContext.Current.CancellationToken);

        // The vault is never left out: it is listed next to Docs, which keeps what the vault does not hold; Guide and Reference are part of Docs.
        found.Folders.Select(folder => (folder.Name, folder.Kind)).ShouldBe([("Archive", FolderKind.Vault), ("Docs", FolderKind.Notes)]);
    }

    [Fact]
    public void Find_FoldersOfNotesBesideEachOther_AreAllListedAndTheOneThatHasSubfoldersOfNotesIsOneRow()
    {
        using var temp = new TempDirectory();
        MakeDocsWithTwoSubfolders(temp, "home/Docs");
        MakeNotes(temp, "home/Docs2", notes: 12);

        FoundFolders found = Finder(temp).Find(TestContext.Current.CancellationToken);

        // A folder whose name starts like another is not inside it: Docs2 lies beside Docs.
        found.Folders.Select(folder => folder.Name).ShouldBe(["Docs", "Docs2"]);
    }

    // The outer folder has 20 notes, all in its sample: 4 wikilinked ones of its own and the ones of the inner folder (12 notes). The inner
    // folder holds 60% of the wikilinked notes when it has 6 of 10, not when it has 5 of 9. When it does not, the outer folder is listed, and the inner
    // folder is too if it is a vault: a folder of notes that lies inside a folder of notes that is listed is part of it.
    [Theory]
    [InlineData(false, 6, false)]
    [InlineData(false, 5, true)]
    [InlineData(true, 6, false)]
    [InlineData(true, 5, true)]
    public void Find_InnerFolder_ReplacesTheOuterOneFromSixtyPercentOfItsWikilinkedNotes(bool innerIsAVault, int innerLinked, bool outerIsListed)
    {
        using var temp = new TempDirectory();
        temp.Write("home/Outer/README.md", Plain);
        WriteNotes(temp, "home/Outer", "p", count: 7, linked: 4);
        if (innerIsAVault)
        {
            temp.CreateDirectory("home/Outer/Inner/.obsidian");
            WriteNotes(temp, "home/Outer/Inner", "n", count: 12, linked: innerLinked);
        }
        else
        {
            MakeNotes(temp, "home/Outer/Inner", notes: 12, linked: innerLinked);
        }

        FoundFolders found = Finder(temp).Find(TestContext.Current.CancellationToken);

        string[] expected = outerIsListed
            ? (innerIsAVault ? [temp.Resolve("home/Outer"), temp.Resolve("home/Outer/Inner")] : [temp.Resolve("home/Outer")])
            : [temp.Resolve("home/Outer/Inner")];
        Paths(found).ShouldBe(expected, ignoreOrder: true);
        if (found.Folders.SingleOrDefault(folder => folder.Name == "Inner") is { } inner)
        {
            inner.Kind.ShouldBe(innerIsAVault ? FolderKind.Vault : FolderKind.Notes);
        }
    }

    // A folder of notes inside a folder of notes inside a folder of notes, with a vault at the bottom that lies inside both: each of the two is judged
    // by what lies inside it, the vault being among the things inside each.
    [Fact]
    public void Find_VaultThatHoldsMostOfTheWikilinksOfTwoFoldersOfNotesAroundIt_IsTheOnlyOneListed()
    {
        using var temp = new TempDirectory();
        temp.Write("home/A/README.md", Plain);
        WriteNotes(temp, "home/A", "p", count: 3, linked: 0);
        MakeNotes(temp, "home/A/B", notes: 4);
        temp.CreateDirectory("home/A/B/V/.obsidian");
        WriteNotes(temp, "home/A/B/V", "n", count: 12, linked: 12);

        FoundFolders found = Finder(temp).Find(TestContext.Current.CancellationToken);

        // A has 20 notes and 16 of them with a wikilink, B has 16 and 16, the vault 12: it holds more than 60% of the wikilinked notes of both.
        found.Folders.Select(folder => (folder.Path, folder.Kind)).ShouldBe([(temp.Resolve("home/A/B/V"), (FolderKind?)FolderKind.Vault)]);
    }

    [Fact]
    public void Find_VaultThatHoldsLittleOfTheWikilinksAndAFolderOfNotesThatHoldsMostOfThoseOfTheOneAroundIt_ListsTheMiddleOneAndTheVault()
    {
        using var temp = new TempDirectory();
        temp.Write("home/A/README.md", Plain);
        WriteNotes(temp, "home/A", "p", count: 3, linked: 0);
        MakeNotes(temp, "home/A/B", notes: 8);
        temp.CreateDirectory("home/A/B/V/.obsidian");
        WriteNotes(temp, "home/A/B/V", "n", count: 4, linked: 4);

        FoundFolders found = Finder(temp).Find(TestContext.Current.CancellationToken);

        // A has 16 notes and 12 with a wikilink, B has 12 and 12, the vault 4: B holds all of A's and is listed instead of it; the vault holds
        // 4 of B's 12, less than 60%, so B stays, and the vault is never left out.
        found.Folders.Select(folder => (folder.Name, folder.Kind)).ShouldBe([("B", FolderKind.Notes), ("V", FolderKind.Vault)]);
    }

    [Fact]
    public void Find_InnerCandidateThatIsNoFolderOfNotes_DoesNotTakeTheOuterOneAway()
    {
        using var temp = new TempDirectory();
        temp.Write("home/Outer/README.md", Linked);
        WriteNotes(temp, "home/Outer", "p", count: 12, linked: 12);
        temp.Write("home/Outer/docs/README.md", Plain);
        WriteNotes(temp, "home/Outer/docs", "d", count: 3, linked: 3);

        Paths(Finder(temp).Find(TestContext.Current.CancellationToken)).ShouldBe([temp.Resolve("home/Outer")]);
    }

    [Fact]
    public void Find_FoldersOfNotesThatDoNotLieInsideOneAnother_AreAllListedEvenWhenTheirNamesStartAlike()
    {
        using var temp = new TempDirectory();
        MakeNotes(temp, "home/Notes", notes: 12);
        MakeNotes(temp, "home/Notes-old", notes: 12);
        MakeNotes(temp, "home/Notes2", notes: 12);

        Finder(temp).Find(TestContext.Current.CancellationToken).Folders.Count.ShouldBe(3);
    }

    // ---- The budget ---------------------------------------------------------------------------------------------

    [Fact]
    public void Find_BudgetThatSufficesForTheWalkButNotForLookingIntoTheCandidates_ListsNoFolderOfNotesAndIsNotComplete()
    {
        using var temp = new TempDirectory();
        MakeNotes(temp, "home/Notes", notes: 12);

        // The walk reads 13 entries (one in the home directory, twelve in Notes); looking into Notes reads those twelve again and opens twelve notes: 37.
        FoundFolders shortOfBudget = Finder(temp, limits: WithSearchEntries(36)).Find(TestContext.Current.CancellationToken);
        FoundFolders enough = Finder(temp, limits: WithSearchEntries(37)).Find(TestContext.Current.CancellationToken);

        shortOfBudget.Folders.ShouldBeEmpty();
        shortOfBudget.Complete.ShouldBeFalse();
        Paths(enough).ShouldBe([temp.Resolve("home/Notes")]);
        enough.Complete.ShouldBeTrue();
    }

    [Fact]
    public void Find_BudgetThatTheWalkUsesUp_ListsTheVaultsItFoundAndNoCandidate()
    {
        using var temp = new TempDirectory();
        MakeVault(temp, "home/Vault");
        MakeNotes(temp, "home/Notes", notes: 12);

        // Two entries in the home directory and nothing more: the vault is found, Notes is not read, so its entry note is not seen.
        FoundFolders found = Finder(temp, limits: WithSearchEntries(2)).Find(TestContext.Current.CancellationToken);

        Paths(found).ShouldBe([temp.Resolve("home/Vault")]);
        found.Complete.ShouldBeFalse();
    }

    [Fact]
    public void Find_CandidatesThatTheBudgetDoesNotReach_AreLookedIntoTheShallowestFirst()
    {
        using var temp = new TempDirectory();
        MakeNotes(temp, "home/a/b/Deep", notes: 12);
        MakeNotes(temp, "home/Shallow", notes: 12);

        // The walk reads 28 entries; each of the two candidates takes 24 (twelve entries and twelve notes). 52 is enough for one: the one nearer to the root.
        FoundFolders one = Finder(temp, limits: WithSearchEntries(52)).Find(TestContext.Current.CancellationToken);
        FoundFolders both = Finder(temp, limits: WithSearchEntries(76)).Find(TestContext.Current.CancellationToken);

        Paths(one).ShouldBe([temp.Resolve("home/Shallow")]);
        one.Complete.ShouldBeFalse();
        Paths(both).ShouldBe([temp.Resolve("home/a/b/Deep"), temp.Resolve("home/Shallow")], ignoreOrder: true);
        both.Complete.ShouldBeTrue();
    }

    [Fact]
    public void Find_BudgetThatRunsOutBeforeTheVaultInsideAFolderOfNotesIsLookedInto_ListsTheVaultAndNotTheFolder()
    {
        using var temp = new TempDirectory();
        temp.Write("home/Project/README.md", Plain);
        WriteNotes(temp, "home/Project", "p", count: 15, linked: 15);
        temp.CreateDirectory("home/Project/vault/.obsidian");
        WriteNotes(temp, "home/Project/vault", "v", count: 4, linked: 0);

        // The walk reads 18 entries; looking into Project reads 22 and opens 20 notes (the vault's four among them): 60. Looking into the vault, for what it holds
        // of the wikilinked notes (which is none), takes 5 entries and 4 notes more: 69. The vault holds too little for the folder to give way to it.
        FoundFolders enough = Finder(temp, limits: WithSearchEntries(69)).Find(TestContext.Current.CancellationToken);
        FoundFolders shortOfBudget = Finder(temp, limits: WithSearchEntries(68)).Find(TestContext.Current.CancellationToken);
        FoundFolders none = Finder(temp, limits: WithSearchEntries(60)).Find(TestContext.Current.CancellationToken);

        Paths(enough).ShouldBe([temp.Resolve("home/Project"), temp.Resolve("home/Project/vault")], ignoreOrder: true);
        enough.Complete.ShouldBeTrue();

        // What the vault holds is not known, and a folder whose vault is not known could be the parent of it: it is not listed. The vault is.
        foreach (FoundFolders unknown in new[] { shortOfBudget, none })
        {
            Paths(unknown).ShouldBe([temp.Resolve("home/Project/vault")]);
            unknown.Complete.ShouldBeFalse();
        }
    }

    [Fact]
    public void Find_BudgetThatRunsOutBeforeACandidateInsideAFolderOfNotesIsLookedInto_ListsNeitherOfThem()
    {
        using var temp = new TempDirectory();
        temp.Write("home/Outer/README.md", Linked);
        WriteNotes(temp, "home/Outer", "p", count: 12, linked: 12);
        temp.Write("home/Outer/docs/Home.md", Plain);
        WriteNotes(temp, "home/Outer/docs", "d", count: 3, linked: 0);

        // The walk reads 19 entries; looking into Outer reads 18 and opens 17 notes: 54. docs is the next candidate: 4 entries more.
        FoundFolders enough = Finder(temp, limits: WithSearchEntries(58)).Find(TestContext.Current.CancellationToken);
        FoundFolders shortOfBudget = Finder(temp, limits: WithSearchEntries(54)).Find(TestContext.Current.CancellationToken);

        Paths(enough).ShouldBe([temp.Resolve("home/Outer")]);
        enough.Complete.ShouldBeTrue();

        // docs was not looked into: it might be the folder that holds the notes, so Outer is not listed either.
        shortOfBudget.Folders.ShouldBeEmpty();
        shortOfBudget.Complete.ShouldBeFalse();
    }

    [Fact]
    public void Find_BudgetThatTheWalkUsesUp_StillFindsTheVaultOfTheLastLevelAndAsksNoOtherFolderOfItForAnEntryNote()
    {
        using var temp = new TempDirectory();
        MakeVault(temp, "home/a/b/c/Vault");
        for (int i = 1; i <= 5; i++)
        {
            temp.CreateDirectory($"home/a/b/c/d{i}");
        }

        // The walk reads 9 entries: a, b, c, and the six folders in c (the vault, which is found by its .obsidian directory at no cost, and five more).
        // Asking the five for an entry note comes after the walk, with what is left; there is nothing Markdown in them, so each costs one entry.
        FoundFolders walkOnly = Finder(temp, limits: WithSearchEntries(9)).Find(TestContext.Current.CancellationToken);
        FoundFolders enough = Finder(temp, limits: WithSearchEntries(14)).Find(TestContext.Current.CancellationToken);
        FoundFolders oneShort = Finder(temp, limits: WithSearchEntries(13)).Find(TestContext.Current.CancellationToken);

        Paths(walkOnly).ShouldBe([temp.Resolve("home/a/b/c/Vault")]);
        walkOnly.Complete.ShouldBeFalse();
        Paths(enough).ShouldBe([temp.Resolve("home/a/b/c/Vault")]);
        enough.Complete.ShouldBeTrue();
        oneShort.Complete.ShouldBeFalse();
    }

    [Fact]
    public void Find_AskingFoldersOfTheLastLevelForAnEntryNote_CostsOneEntryForTheFolderAndOneForEachEntryRead()
    {
        using var temp = new TempDirectory();
        for (int i = 1; i <= 3; i++)
        {
            WriteNotes(temp, $"home/a/b/c/d{i}", "m", count: 3, linked: 0);
            for (int f = 1; f <= 10; f++)
            {
                temp.Write($"home/a/b/c/d{i}/image{f:D2}.png", "x");
            }
        }

        // The walk reads 6 entries (a, b, c and the three folders in c). Each of the three has 13 entries (three Markdown files and ten that are not) and no
        // entry note: every entry read is taken from the budget, whatever it is, so 1 + 13 each.
        FoundFolders enough = Finder(temp, limits: WithSearchEntries(6 + 3 * 14)).Find(TestContext.Current.CancellationToken);
        FoundFolders oneShort = Finder(temp, limits: WithSearchEntries(6 + 3 * 14 - 1)).Find(TestContext.Current.CancellationToken);

        enough.Complete.ShouldBeTrue();
        oneShort.Complete.ShouldBeFalse();
    }

    [Fact]
    public void Find_FolderOfTheLastLevelWithAThousandFilesAndNoEntryNote_CostsTheSearchAboutTwoHundredEntriesAndIsNoCandidate()
    {
        using var temp = new TempDirectory();
        for (int i = 1; i <= 1000; i++)
        {
            temp.Write($"home/a/b/c/Media/photo{i:D4}.jpg", "x");
        }

        // The walk reads 4 entries (a, b, c, Media). Media is asked: the folder and its first 200 entries, 201; the 800 others are not read.
        FoundFolders enough = Finder(temp, limits: WithSearchEntries(4 + 1 + NoteFolderCheck.ProbeEntries)).Find(TestContext.Current.CancellationToken);
        FoundFolders oneShort = Finder(temp, limits: WithSearchEntries(4 + NoteFolderCheck.ProbeEntries)).Find(TestContext.Current.CancellationToken);

        enough.Folders.ShouldBeEmpty();
        enough.Complete.ShouldBeTrue();
        oneShort.Complete.ShouldBeFalse();
    }

    [Fact]
    public void Find_FolderOfTheLastLevelWhoseEntryNoteIsAmongItsFirstEntries_IsFoundWhateverTheOrderOfTheFileSystem()
    {
        using var temp = new TempDirectory();
        MakeNotes(temp, "home/a/b/c/Notes", notes: 150, entry: "README.md", others: 10);

        // 160 entries: all of them are among the first 200, wherever README.md is listed.
        FoundFolders found = Finder(temp).Find(TestContext.Current.CancellationToken);

        Paths(found).ShouldBe([temp.Resolve("home/a/b/c/Notes")]);
        found.Complete.ShouldBeTrue();
    }

    [Fact]
    public void Find_FolderOfNotesAtTheLastLevel_NeedsTheBudgetForAskingItAndThenForLookingIntoIt()
    {
        using var temp = new TempDirectory();
        MakeNotes(temp, "home/a/b/c/Notes", notes: 12);

        // The walk reads 4 entries (a, b, c, Notes). Asking Notes for an entry note costs one entry and one for each Markdown file looked at until Home.md is
        // met: from 2 to 13, as the file system lists them. Looking into it costs its twelve entries and the twelve notes of the sample: 24.
        FoundFolders notAsked = Finder(temp, limits: WithSearchEntries(4)).Find(TestContext.Current.CancellationToken);
        FoundFolders notLookedInto = Finder(temp, limits: WithSearchEntries(4 + 13)).Find(TestContext.Current.CancellationToken);
        FoundFolders found = Finder(temp, limits: WithSearchEntries(4 + 13 + 24)).Find(TestContext.Current.CancellationToken);

        // A folder that was not asked, or was asked and not looked into, is not listed, and the search says it stopped.
        notAsked.Folders.ShouldBeEmpty();
        notAsked.Complete.ShouldBeFalse();
        notLookedInto.Folders.ShouldBeEmpty();
        notLookedInto.Complete.ShouldBeFalse();
        Paths(found).ShouldBe([temp.Resolve("home/a/b/c/Notes")]);
        found.Complete.ShouldBeTrue();
    }

    [Fact]
    public void Find_FoldersOfTheLastLevelInsideAFolderOfNotes_AreAskedFirstSoThatTheBudgetReachesWhatDecidesTheFolder()
    {
        using var temp = new TempDirectory();
        temp.Write("home/a/b/docs/README.md", Plain);
        WriteNotes(temp, "home/a/b/docs", "p", count: 15, linked: 0);
        MakeNotes(temp, "home/a/b/docs/guide", notes: 12);
        const int decoys = 100;
        for (int i = 1; i <= decoys; i++)
        {
            WriteNotes(temp, $"home/x/y/z/d{i:D3}", "m", count: 10, linked: 0);
        }

        // The walk reads 2 + 1 + 1 + 1 + 1 entries for the folders down to docs and z, the 17 of docs and the 100 folders of z. docs is the one candidate: its 17 entries
        // and the 12 of guide, then 20 notes for the sample. guide is asked (one entry and up to 12 Markdown files) and looked into (12 entries and 12 notes). What is
        // left is a little: it is not enough for the hundred others, which cost 11 each, to be asked before guide.
        int budget = (2 + 1 + 1 + 1 + 1 + 17 + decoys) + (17 + 12 + 20) + 13 + (12 + 12);

        FoundFolders found = Finder(temp, limits: WithSearchEntries(budget)).Find(TestContext.Current.CancellationToken);

        // guide holds all the wikilinks of docs that are not docs' own: only guide is listed, and not docs, though the budget did not reach the other folders.
        Paths(found).ShouldBe([temp.Resolve("home/a/b/docs/guide")]);
        found.Complete.ShouldBeFalse();
    }

    [Fact]
    public void Find_FolderOfTheLastLevelThatWasNotAsked_DoesNotTakeTheFolderAroundItAway()
    {
        using var temp = new TempDirectory();
        temp.Write("home/a/b/Notes/Home.md", Linked);
        WriteNotes(temp, "home/a/b/Notes", "n", count: 11, linked: 11);
        MakeNotes(temp, "home/a/b/Notes/inner", notes: 12);

        // The walk reads 2 (a, b), 1 (Notes) and 13 (the twelve notes and inner) entries; Notes has its 12 notes and inner's 12 in its look, which takes 25
        // entries and 20 notes for the sample. Nothing is left to ask inner: it is not known to be a folder of notes, and Notes is listed, as it was before
        // the last level was asked for at all.
        int budget = (1 + 1 + 1 + 13) + (13 + 12) + 20;

        FoundFolders found = Finder(temp, limits: WithSearchEntries(budget)).Find(TestContext.Current.CancellationToken);

        Paths(found).ShouldBe([temp.Resolve("home/a/b/Notes")]);
        found.Complete.ShouldBeFalse();
    }

    [Fact]
    public void Find_FolderThatHasMoreEntriesThanOneFolderMayTake_IsJudgedByWhatWasRead()
    {
        using var temp = new TempDirectory();
        MakeNotes(temp, "home/Notes", notes: 12);

        FoundFolders cut = Finder(temp, limits: BrowseLimits.Default with { CheckEntriesPerFolder = 9 }).Find(TestContext.Current.CancellationToken);
        FoundFolders whole = Finder(temp, limits: BrowseLimits.Default with { CheckEntriesPerFolder = 12 }).Find(TestContext.Current.CancellationToken);

        // Nine entries are nine notes, one short of ten; the twelve are all of them. A folder that is too big for the limit costs the search no more than that.
        cut.Folders.ShouldBeEmpty();
        cut.Complete.ShouldBeTrue();
        Paths(whole).ShouldBe([temp.Resolve("home/Notes")]);
    }

    [Fact]
    public void Find_NoTime_ListsNoFolderOfNotes()
    {
        using var temp = new TempDirectory();
        MakeNotes(temp, "home/Notes", notes: 12);

        FoundFolders found = Finder(temp, limits: BrowseLimits.Default with { SearchTime = TimeSpan.Zero }).Find(TestContext.Current.CancellationToken);

        found.Folders.ShouldBeEmpty();
        found.Complete.ShouldBeFalse();
    }
}
