using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.Versioning;
using Pusula.Browse;
using Pusula.UnitTests.Support;
using Shouldly;
using Xunit;

namespace Pusula.UnitTests.Browse;

public sealed class NoteFolderScannerTests
{
    private static readonly TimeSpan Plenty = TimeSpan.FromMinutes(10);

    private static ScanBudget Unlimited() => new(maxEntries: int.MaxValue, Plenty);

    // Looks into a folder the way the search does, with no limit on the entries unless told otherwise.
    private static NoteFolderCheck? Scan(string folder, bool sampleAlways = false, int maxEntries = int.MaxValue, ScanBudget? budget = null) =>
        NoteFolderScanner.Scan(folder, folder, sampleAlways, maxEntries, budget ?? Unlimited(), TestContext.Current.CancellationToken);

    private static NoteFolderCheck Looked(string folder, bool sampleAlways = false) =>
        Scan(folder, sampleAlways) ?? throw new InvalidOperationException("The budget ran out.");

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

    // Notes named n01.md, n02.md, ... in a folder; the first `linked` of them have a wikilink.
    private static void MakeNotes(TempDirectory temp, string folder, int notes, int linked = 0)
    {
        for (int i = 1; i <= notes; i++)
        {
            temp.Write($"{folder}/n{i:D2}.md", i <= linked ? $"# {i}\nSee [[other]].\n" : $"# {i}\nNo link here.\n");
        }
    }

    // ---- What is counted ----------------------------------------------------------------------------------------

    [Fact]
    public void Scan_NotesAndFiles_AreCountedInTheFolderAndUpToThreeLevelsBelowIt()
    {
        using var temp = new TempDirectory();
        temp.Write("a.md", "x");
        temp.Write("b.txt", "x");
        temp.Write("l1/c.md", "x");
        temp.Write("l1/l2/d.md", "x");
        temp.Write("l1/l2/e.png", "x");
        temp.Write("l1/l2/l3/f.md", "x");
        temp.Write("l1/l2/l3/l4/g.md", "x");
        temp.Write("l1/l2/l3/l4/l5/h.md", "x");

        NoteFolderCheck check = Looked(temp.Path);

        // The folder is level 0; the folders of level 3 are read, the ones below them are not entered.
        check.Notes.ShouldBe(4);
        check.Files.ShouldBe(6);
    }

    [Fact]
    public void Scan_ExtensionOfAMarkdownFile_IsMatchedIgnoringCase()
    {
        using var temp = new TempDirectory();
        temp.Write("a.md", "x");
        temp.Write("b.MD", "x");
        temp.Write("c.Md", "x");
        temp.Write("d.markdown", "x");
        temp.Write("e.md.txt", "x");
        temp.CreateDirectory("folder.md");

        NoteFolderCheck check = Looked(temp.Path);

        check.Notes.ShouldBe(3);
        check.Files.ShouldBe(5);
    }

    [Fact]
    public void Scan_HiddenFilesAndTheFoldersTheSearchLeavesAlone_AreNotCounted()
    {
        using var temp = new TempDirectory();
        temp.Write("visible.md", "x");
        temp.Write("visible.txt", "x");
        temp.Write(".hidden.md", "x");
        temp.Write(".DS_Store", "x");
        temp.Write(".git/note.md", "x");
        temp.Write(".obsidian/app.md", "x");
        temp.Write("node_modules/note.md", "x");
        temp.Write("bin/note.md", "x");
        temp.Write("obj/note.md", "x");
        temp.Write("sub/node_modules/note.md", "x");
        temp.Write("sub/inside.md", "x");
        temp.Write("binaries/inside.md", "x");

        NoteFolderCheck check = Looked(temp.Path);

        check.Notes.ShouldBe(3);
        check.Files.ShouldBe(4);
    }

    [Fact]
    public void Scan_FolderThatIsNotThere_HasNothing()
    {
        using var temp = new TempDirectory();

        Looked(temp.Resolve("gone")).ShouldBe(new NoteFolderCheck(0, 0, 0, 0));
    }

    [Fact]
    public void Scan_SymbolicLinkToAFolderElsewhere_IsFollowed()
    {
        using var temp = new TempDirectory();
        temp.Write("home/a.md", "x");
        temp.Write("elsewhere/b.md", "x");
        temp.Write("elsewhere/deep/c.md", "x");
        CreateDirectoryLink(temp.Resolve("home/linked"), temp.Resolve("elsewhere"));

        Looked(temp.Resolve("home")).Notes.ShouldBe(3);
    }

    [Fact]
    public void Scan_SymbolicLinksThatLeadBackOrNeverEnd_AreNotEnteredAgain()
    {
        using var temp = new TempDirectory();
        temp.Write("a.md", "x");
        temp.Write("sub/b.md", "x");
        CreateDirectoryLink(temp.Resolve("sub/loop"), temp.Path);
        CreateDirectoryLink(temp.Resolve("self"), temp.Resolve("self"));

        NoteFolderCheck check = Looked(temp.Path);

        check.Notes.ShouldBe(2);
    }

    [Fact]
    public void Scan_FolderThatTwoPathsLeadTo_IsReadOnce()
    {
        using var temp = new TempDirectory();
        temp.Write("real/a.md", "x");
        CreateDirectoryLink(temp.Resolve("first"), temp.Resolve("real"));
        CreateDirectoryLink(temp.Resolve("second"), temp.Resolve("real"));

        // real, first and second are the same folder: one of them is read, the other two are not.
        Looked(temp.Path).Notes.ShouldBe(1);
    }

    [Fact]
    [UnsupportedOSPlatform("windows")]
    public void Scan_FolderThatCannotBeRead_HasNoNotesAndTheRestIsCounted()
    {
        Assert.SkipWhen(OperatingSystem.IsWindows() || Environment.IsPrivilegedProcess, "Needs POSIX permissions and an unprivileged user.");
        using var temp = new TempDirectory();
        temp.Write("open/a.md", "x");
        temp.Write("locked/b.md", "x");
        string locked = temp.Resolve("locked");
        File.SetUnixFileMode(locked, UnixFileMode.None);
        try
        {
            NoteFolderCheck check = Looked(temp.Path);

            check.Notes.ShouldBe(1);
            check.Files.ShouldBe(1);
        }
        finally
        {
            File.SetUnixFileMode(locked, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }
    }

    // ---- The sample ---------------------------------------------------------------------------------------------

    [Fact]
    public void Scan_FolderWithNotesEnough_ReadsEveryNoteOfASmallFolderAndCountsTheWikilinks()
    {
        using var temp = new TempDirectory();
        MakeNotes(temp, ".", notes: 12, linked: 5);

        NoteFolderCheck check = Looked(temp.Path);

        check.ShouldBe(new NoteFolderCheck(Notes: 12, Files: 12, Sampled: 12, Linked: 5));
    }

    [Fact]
    public void Scan_FolderWithTooFewNotes_ReadsNoneOfThemUnlessAsked()
    {
        using var temp = new TempDirectory();
        MakeNotes(temp, ".", notes: 4, linked: 4);

        Looked(temp.Path).ShouldBe(new NoteFolderCheck(Notes: 4, Files: 4, Sampled: 0, Linked: 0));
        Looked(temp.Path, sampleAlways: true).ShouldBe(new NoteFolderCheck(Notes: 4, Files: 4, Sampled: 4, Linked: 4));
    }

    [Fact]
    public void Scan_FolderWhereMarkdownIsLessThanHalf_ReadsNoneOfTheNotes()
    {
        using var temp = new TempDirectory();
        MakeNotes(temp, ".", notes: 12, linked: 12);
        for (int i = 0; i < 13; i++)
        {
            temp.Write($"image{i:D2}.png", "x");
        }

        Looked(temp.Path).ShouldBe(new NoteFolderCheck(Notes: 12, Files: 25, Sampled: 0, Linked: 0));
    }

    [Fact]
    public void Scan_FolderWithNoNotes_IsNotSampledEvenWhenAsked()
    {
        using var temp = new TempDirectory();
        temp.Write("a.txt", "x");

        Looked(temp.Path, sampleAlways: true).ShouldBe(new NoteFolderCheck(Notes: 0, Files: 1, Sampled: 0, Linked: 0));
    }

    [Fact]
    public void Scan_MoreNotesThanTheSample_ReadsTwentySpreadEvenlyOverTheFolders()
    {
        using var temp = new TempDirectory();

        // Four folders of ten notes; only the notes of the last one have a wikilink. Taking every second note (by path) takes five from each folder.
        foreach (string folder in new[] { "a", "b", "c", "d" })
        {
            MakeNotes(temp, folder, notes: 10, linked: folder == "d" ? 10 : 0);
        }

        NoteFolderCheck check = Looked(temp.Path);

        check.ShouldBe(new NoteFolderCheck(Notes: 40, Files: 40, Sampled: 20, Linked: 5));
    }

    [Fact]
    public void Scan_TheSameFolder_GivesTheSameSampleWhateverTheOrderItWasMadeIn()
    {
        using var first = new TempDirectory();
        using var second = new TempDirectory();
        string[] folders = ["a", "b", "c", "d"];
        foreach (string folder in folders)
        {
            MakeNotes(first, folder, notes: 11, linked: folder is "b" or "d" ? 11 : 0);
        }

        foreach (string folder in folders.Reverse())
        {
            MakeNotes(second, folder, notes: 11, linked: folder is "b" or "d" ? 11 : 0);
        }

        NoteFolderCheck one = Looked(first.Path);

        one.Sampled.ShouldBe(20);
        Looked(first.Path).ShouldBe(one);
        Looked(second.Path).ShouldBe(one);
    }

    [Fact]
    public void Scan_OnlyTheFirstFourKilobytesOfANote_AreRead()
    {
        using var temp = new TempDirectory();
        temp.Write("inside.md", new string('x', 4094) + "[[");
        temp.Write("astride.md", new string('x', 4095) + "[[");
        temp.Write("after.md", new string('x', 4096) + "[[");
        temp.Write("far.md", new string('x', 100_000) + "[[");

        NoteFolderCheck check = Looked(temp.Path, sampleAlways: true);

        // Only a note that has both brackets in its first 4,096 bytes has a wikilink.
        check.ShouldBe(new NoteFolderCheck(Notes: 4, Files: 4, Sampled: 4, Linked: 1));
    }

    [Theory]
    [InlineData("[[Note]]", true)]
    [InlineData("see [[Note|name]] and more", true)]
    [InlineData("![[embedded]]", true)]
    [InlineData("[[", true)]
    [InlineData("[ [Note]]", false)]
    [InlineData("[Note](note.md)", false)]
    [InlineData("[", false)]
    [InlineData("plain text", false)]
    [InlineData("", false)]
    public void Scan_WhatMakesAWikilink_IsTwoOpeningBrackets(string text, bool linked)
    {
        using var temp = new TempDirectory();
        temp.Write("a.md", text);

        NoteFolderCheck check = Looked(temp.Path, sampleAlways: true);

        check.Sampled.ShouldBe(1);
        check.Linked.ShouldBe(linked ? 1 : 0);
    }

    [Fact]
    public void Scan_WikilinkInANoteWithUnicodeText_IsFoundAtTheByteLevel()
    {
        using var temp = new TempDirectory();
        temp.Write("a.md", "Çiçek bahçesi: [[Şeker]] ve İzmir");

        Looked(temp.Path, sampleAlways: true).Linked.ShouldBe(1);
    }

    [Fact]
    public void Scan_EmptyNote_IsASampledNoteWithNoWikilink()
    {
        using var temp = new TempDirectory();
        temp.Write("empty.md", string.Empty);
        temp.Write("linked.md", "[[x]]");

        Looked(temp.Path, sampleAlways: true).ShouldBe(new NoteFolderCheck(Notes: 2, Files: 2, Sampled: 2, Linked: 1));
    }

    [Fact]
    [UnsupportedOSPlatform("windows")]
    public void Scan_NoteThatCannotBeRead_IsNoPartOfTheSample()
    {
        Assert.SkipWhen(OperatingSystem.IsWindows() || Environment.IsPrivilegedProcess, "Needs POSIX permissions and an unprivileged user.");
        using var temp = new TempDirectory();
        temp.Write("open.md", "[[x]]");
        string secret = temp.Write("closed.md", "[[y]]");
        File.SetUnixFileMode(secret, UnixFileMode.None);
        try
        {
            Looked(temp.Path, sampleAlways: true).ShouldBe(new NoteFolderCheck(Notes: 2, Files: 2, Sampled: 1, Linked: 1));
        }
        finally
        {
            File.SetUnixFileMode(secret, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        }
    }

    [Fact]
    public void Scan_NoteThatIsALinkToNothing_IsNoPartOfTheSample()
    {
        using var temp = new TempDirectory();
        temp.Write("open.md", "[[x]]");
        try
        {
            File.CreateSymbolicLink(temp.Resolve("dangling.md"), temp.Resolve("nowhere.md"));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or PlatformNotSupportedException)
        {
            Assert.Skip($"Symbolic links are not available here: {exception.Message}");
        }

        Looked(temp.Path, sampleAlways: true).ShouldBe(new NoteFolderCheck(Notes: 2, Files: 2, Sampled: 1, Linked: 1));
    }

    [Fact]
    public void Scan_NoteThatIsALinkToAFile_IsReadThroughTheLink()
    {
        using var temp = new TempDirectory();
        temp.Write("elsewhere/target.md", "[[x]]");
        temp.CreateDirectory("home");
        try
        {
            File.CreateSymbolicLink(temp.Resolve("home/linked.md"), temp.Resolve("elsewhere/target.md"));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or PlatformNotSupportedException)
        {
            Assert.Skip($"Symbolic links are not available here: {exception.Message}");
        }

        Looked(temp.Resolve("home"), sampleAlways: true).ShouldBe(new NoteFolderCheck(Notes: 1, Files: 1, Sampled: 1, Linked: 1));
    }

    // A link to a file: made where links can be made.
    private static void CreateFileLink(string linkPath, string target)
    {
        try
        {
            File.CreateSymbolicLink(linkPath, target);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or PlatformNotSupportedException)
        {
            Assert.Skip($"Symbolic links are not available here: {exception.Message}");
        }
    }

    // A named pipe in the file system: opening one waits for ever until something writes to it. .NET has no way to make one, so the program that does is run.
    private static void MakePipe(string path)
    {
        Assert.SkipWhen(OperatingSystem.IsWindows(), "Needs a named pipe in the file system.");
        try
        {
            using Process? mkfifo = Process.Start(new ProcessStartInfo("mkfifo", [path]) { RedirectStandardError = true });
            mkfifo?.WaitForExit();
            if (mkfifo is not { ExitCode: 0 })
            {
                Assert.Skip("A named pipe cannot be made here.");
            }
        }
        catch (Exception exception) when (exception is Win32Exception or InvalidOperationException)
        {
            Assert.Skip($"A named pipe cannot be made here: {exception.Message}");
        }
    }

    // Looks into a folder on a thread of its own and waits a while for it: a scan that opened a pipe never comes back, and that has to fail the test and not hang it.
    private static async Task<NoteFolderCheck> LookedWithinAWhile(string folder)
    {
        Task<NoteFolderCheck> look = Task.Run(() => Looked(folder, sampleAlways: true), TestContext.Current.CancellationToken);

        Task first = await Task.WhenAny(look, Task.Delay(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken));

        first.ShouldBeSameAs(look, "The scan did not come back: it is waiting on a pipe that it should not have opened.");
        return await look;
    }

    [Fact]
    public void Scan_NoteThatIsALinkToAnEmptyFile_IsASampledNoteWithNoWikilink()
    {
        using var temp = new TempDirectory();
        temp.Write("elsewhere/empty.md", string.Empty);
        temp.Write("home/linked.md", "[[x]]");
        CreateFileLink(temp.Resolve("home/to-empty.md"), temp.Resolve("elsewhere/empty.md"));

        // What a link leads to is empty, as the file itself would be.
        Looked(temp.Resolve("home"), sampleAlways: true).ShouldBe(new NoteFolderCheck(Notes: 2, Files: 2, Sampled: 2, Linked: 1));
    }

    [Fact]
    public void Scan_NoteThatIsALinkToALinkToAFile_IsReadThroughBothLinks()
    {
        using var temp = new TempDirectory();
        temp.Write("elsewhere/target.md", "[[x]]");
        temp.CreateDirectory("home");
        CreateFileLink(temp.Resolve("elsewhere/first.md"), temp.Resolve("elsewhere/target.md"));
        CreateFileLink(temp.Resolve("home/second.md"), temp.Resolve("elsewhere/first.md"));

        Looked(temp.Resolve("home"), sampleAlways: true).ShouldBe(new NoteFolderCheck(Notes: 1, Files: 1, Sampled: 1, Linked: 1));
    }

    [Fact]
    public async Task Scan_NoteThatIsAPipe_IsNotOpenedAndIsTakenForAnEmptyNote()
    {
        using var temp = new TempDirectory();
        temp.Write("real.md", "[[x]]");
        MakePipe(temp.Resolve("pipe.md"));

        (await LookedWithinAWhile(temp.Path)).ShouldBe(new NoteFolderCheck(Notes: 2, Files: 2, Sampled: 2, Linked: 1));
    }

    [Fact]
    public async Task Scan_NoteThatIsALinkToAPipe_IsNotOpenedEither()
    {
        using var temp = new TempDirectory();
        temp.Write("home/real.md", "[[x]]");
        temp.CreateDirectory("elsewhere");
        MakePipe(temp.Resolve("elsewhere/pipe"));
        CreateFileLink(temp.Resolve("home/to-pipe.md"), temp.Resolve("elsewhere/pipe"));

        // The length of a link is its own, not the length of what it leads to: the guard has to look at the target.
        (await LookedWithinAWhile(temp.Resolve("home"))).ShouldBe(new NoteFolderCheck(Notes: 2, Files: 2, Sampled: 2, Linked: 1));
    }

    // ---- Asking a folder for an entry note ------------------------------------------------------------------------

    private static bool? Asked(string folder, ScanBudget? budget = null) =>
        NoteFolderScanner.HasEntryNote(folder, budget ?? Unlimited(), TestContext.Current.CancellationToken);

    [Theory]
    [InlineData("Home.md")]
    [InlineData("home.md")]
    [InlineData("HOME.MD")]
    [InlineData("index.md")]
    [InlineData("Index.Md")]
    [InlineData("README.md")]
    [InlineData("readme.md")]
    [InlineData("Readme.MD")]
    [InlineData("MOC.md")]
    [InlineData("moc.md")]
    [InlineData("_index.md")]
    [InlineData("_INDEX.MD")]
    [InlineData("start.md")]
    [InlineData("Start.md")]
    public void HasEntryNote_FolderWithAnEntryNote_HasOneWhateverTheCaseOfItsName(string name)
    {
        using var temp = new TempDirectory();
        temp.Write("other.md", "x");
        temp.Write(name, "x");

        Asked(temp.Path).ShouldBe(true);
    }

    [Theory]
    [InlineData("readme.txt")]
    [InlineData("README")]
    [InlineData("README.markdown")]
    [InlineData("README.md.bak")]
    [InlineData("notes.md")]
    [InlineData("homepage.md")]
    [InlineData("index2.md")]
    [InlineData("my-index.md")]
    [InlineData("ındex.md")]
    public void HasEntryNote_FileThatIsNoEntryNote_IsNone(string name)
    {
        using var temp = new TempDirectory();
        temp.Write(name, "x");

        Asked(temp.Path).ShouldBe(false);
    }

    [Fact]
    public void HasEntryNote_EntryNoteInAFolderBelowOrAFolderCalledLikeOne_IsNone()
    {
        using var temp = new TempDirectory();
        temp.Write("sub/README.md", "x");
        temp.CreateDirectory("index.md");
        temp.Write("notes.md", "x");

        Asked(temp.Path).ShouldBe(false);
    }

    [Fact]
    public void HasEntryNote_EntryNoteThatIsALinkToAFile_IsOne()
    {
        using var temp = new TempDirectory();
        temp.Write("elsewhere/target.md", "x");
        temp.CreateDirectory("home");
        try
        {
            File.CreateSymbolicLink(temp.Resolve("home/README.md"), temp.Resolve("elsewhere/target.md"));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or PlatformNotSupportedException)
        {
            Assert.Skip($"Symbolic links are not available here: {exception.Message}");
        }

        // A file the walk would take for the entry note of a folder it reads, one that leads to a file included.
        Asked(temp.Resolve("home")).ShouldBe(true);
    }

    [Fact]
    public void HasEntryNote_FolderThatIsNotThere_HasNone()
    {
        using var temp = new TempDirectory();

        Asked(temp.Resolve("gone")).ShouldBe(false);
    }

    [Fact]
    [UnsupportedOSPlatform("windows")]
    public void HasEntryNote_FolderThatCannotBeRead_HasNone()
    {
        Assert.SkipWhen(OperatingSystem.IsWindows() || Environment.IsPrivilegedProcess, "Needs POSIX permissions and an unprivileged user.");
        using var temp = new TempDirectory();
        temp.Write("locked/README.md", "x");
        string locked = temp.Resolve("locked");
        File.SetUnixFileMode(locked, UnixFileMode.None);
        try
        {
            Asked(locked).ShouldBe(false);
        }
        finally
        {
            File.SetUnixFileMode(locked, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }
    }

    // A folder of files that are no entry note: every entry that is read is taken from the budget, and nothing but the entries read.
    private static void MakeFiles(TempDirectory temp, string folder, int count, string extension = "png")
    {
        for (int i = 1; i <= count; i++)
        {
            temp.Write($"{folder}/file{i:D4}.{extension}", "x");
        }
    }

    [Fact]
    public void HasEntryNote_EmptyFolder_CostsOneEntryForTheFolder()
    {
        using var temp = new TempDirectory();
        var exact = new ScanBudget(maxEntries: 1, Plenty);

        Asked(temp.Path, exact).ShouldBe(false);

        // The one entry was enough, so that the time is asked for even where there is nothing to read; with none the folder is not asked at all.
        exact.IsSpent.ShouldBeFalse();
        Asked(temp.Path, new ScanBudget(maxEntries: 0, Plenty)).ShouldBeNull();
    }

    [Fact]
    public void HasEntryNote_FolderWithNoEntryNote_CostsOneEntryForTheFolderAndOneForEachEntryItReadsOfAnyKind()
    {
        using var temp = new TempDirectory();
        temp.Write("a.md", "x");
        temp.Write("b.MD", "x");
        temp.Write("c.Md", "x");
        MakeFiles(temp, ".", 10);
        temp.Write("sub/README.md", "x");
        var exact = new ScanBudget(maxEntries: 1 + 14, Plenty);

        // The folder, and its 14 entries (three Markdown files, ten that are not, and a folder, which is read as an entry and not entered): 15.
        Asked(temp.Path, exact).ShouldBe(false);
        exact.IsSpent.ShouldBeFalse();
        Asked(temp.Path, new ScanBudget(maxEntries: 1 + 13, Plenty)).ShouldBeNull();
    }

    [Fact]
    public void HasEntryNote_FolderWithAThousandFilesAndNoEntryNote_ReadsTwoHundredEntriesAndHasNone()
    {
        using var temp = new TempDirectory();
        MakeFiles(temp, ".", 1000);
        var exact = new ScanBudget(maxEntries: 1 + NoteFolderCheck.ProbeEntries, Plenty);

        // The folder and the first 200 of its 1,000 entries: 201. The others are not read, and cost nothing: a folder that has no entry note
        // among its first entries is taken to have none.
        Asked(temp.Path, exact).ShouldBe(false);

        NoteFolderCheck.ProbeEntries.ShouldBe(200);
        exact.IsSpent.ShouldBeFalse();
        Asked(temp.Path, new ScanBudget(maxEntries: NoteFolderCheck.ProbeEntries, Plenty)).ShouldBeNull();
    }

    [Fact]
    public void HasEntryNote_FolderWithAThousandMarkdownFilesAndNoEntryNote_ReadsTwoHundredEntriesToo()
    {
        using var temp = new TempDirectory();
        MakeFiles(temp, ".", 1000, "md");
        var exact = new ScanBudget(maxEntries: 1 + NoteFolderCheck.ProbeEntries, Plenty);

        Asked(temp.Path, exact).ShouldBe(false);

        exact.IsSpent.ShouldBeFalse();
    }

    [Fact]
    public void HasEntryNote_EntryNoteAmongTheFirstEntries_IsFoundWhereverTheFileSystemPutsIt()
    {
        using var temp = new TempDirectory();
        MakeFiles(temp, ".", NoteFolderCheck.ProbeEntries - 1);
        temp.Write("README.md", "x");

        // 200 entries in all, so the entry note is among the first 200 in any order; the most it costs is the folder and all of them.
        var exact = new ScanBudget(maxEntries: 1 + NoteFolderCheck.ProbeEntries, Plenty);

        Asked(temp.Path, exact).ShouldBe(true);
        exact.IsSpent.ShouldBeFalse();
    }

    [Fact]
    public void HasEntryNote_FolderWithOnlyTheEntryNoteAndAnotherFile_CostsTwoOrThreeEntriesAsTheFileSystemListsThem()
    {
        using var temp = new TempDirectory();
        temp.Write("README.md", "x");
        temp.Write("image.png", "x");

        // The entry note stops the asking: with the budget of the folder and both entries, it is found whichever comes first; with the folder alone nothing is read.
        Asked(temp.Path, new ScanBudget(maxEntries: 1 + 2, Plenty)).ShouldBe(true);
        Asked(temp.Path, new ScanBudget(maxEntries: 1, Plenty)).ShouldBeNull();
    }

    [Fact]
    public void HasEntryNote_TimeThatRunsOutInTheMiddleOfABigFolder_StopsTheAsking()
    {
        using var temp = new TempDirectory();
        MakeFiles(temp, ".", 1000);
        var clock = new SteppingTimeProvider(TimeSpan.FromMilliseconds(1));
        var budget = new ScanBudget(maxEntries: 100_000, TimeSpan.FromMilliseconds(50), clock);

        // Time moves on 1 ms with every entry that is taken from the budget (the folder is the first): at the 50th it is out, long before the
        // 200 entries of the cap or the 1,000 of the folder, and the answer is that it is not known.
        Asked(temp.Path, budget).ShouldBeNull();

        budget.IsSpent.ShouldBeTrue();
        clock.Reads.ShouldBeLessThan(60);
    }

    [Fact]
    public void HasEntryNote_NumberOfEntriesThatRunsOutInTheMiddleOfABigFolder_StopsTheAsking()
    {
        using var temp = new TempDirectory();
        MakeFiles(temp, ".", 1000);
        var budget = new ScanBudget(maxEntries: 50, Plenty);

        Asked(temp.Path, budget).ShouldBeNull();

        budget.IsSpent.ShouldBeTrue();
    }

    [Fact]
    public void HasEntryNote_BudgetThatRanOutBefore_IsNoAnswerAndTakesNothingMore()
    {
        using var temp = new TempDirectory();
        temp.Write("README.md", "x");
        var spent = new ScanBudget(maxEntries: 0, Plenty);

        Asked(temp.Path, spent).ShouldBeNull();
        spent.IsSpent.ShouldBeTrue();
        Asked(temp.Path, spent).ShouldBeNull();
    }

    [Fact]
    public void HasEntryNote_NoTime_IsNoAnswer()
    {
        using var temp = new TempDirectory();
        temp.Write("README.md", "x");

        Asked(temp.Path, new ScanBudget(maxEntries: 100, TimeSpan.Zero)).ShouldBeNull();
    }

    [Fact]
    public void HasEntryNote_RequestThatIsGone_StopsTheAsking()
    {
        using var temp = new TempDirectory();
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();

        Should.Throw<OperationCanceledException>(() => NoteFolderScanner.HasEntryNote(temp.Path, Unlimited(), cancelled.Token));
    }

    // ---- What a look and an asking note of their cost, for the log -----------------------------------------------

    [Fact]
    public void Scan_WithAPart_NotesTheFolderTheEntriesTheNotesOpenedAndTheTime()
    {
        using var temp = new TempDirectory();
        MakeNotes(temp, ".", notes: 12, linked: 12);
        var part = new SearchStats.ScanPart();

        NoteFolderScanner.Scan(temp.Path, temp.Path, sampleAlways: false, int.MaxValue, Unlimited(), TestContext.Current.CancellationToken, part).ShouldNotBeNull();

        // Twelve entries read, and twelve notes opened for the sample, which are not counted as entries.
        part.Count.ShouldBe(1);
        part.Entries.ShouldBe(12);
        part.NotesOpened.ShouldBe(12);
        part.Capped.ShouldBe(0);
        part.Slowest.ShouldBe(temp.Path);
        part.SlowestNoteFolder.ShouldBe(Path.GetDirectoryName(temp.Resolve("n01.md")));
        part.Milliseconds.ShouldBeGreaterThanOrEqualTo(part.NoteMilliseconds);
    }

    [Fact]
    public void Scan_WithAPart_FolderWithMoreEntriesThanTheLimit_IsNotedAsCut()
    {
        using var temp = new TempDirectory();
        MakeNotes(temp, ".", notes: 8, linked: 0);
        var part = new SearchStats.ScanPart();

        NoteFolderScanner.Scan(temp.Path, temp.Path, sampleAlways: false, maxEntries: 5, Unlimited(), TestContext.Current.CancellationToken, part);

        part.Capped.ShouldBe(1);
        part.Entries.ShouldBe(5);
    }

    [Fact]
    public void Scan_WithAPart_BudgetThatRunsOut_StillNotesTheWorkThatWasDone()
    {
        using var temp = new TempDirectory();
        MakeNotes(temp, ".", notes: 12, linked: 12);
        var part = new SearchStats.ScanPart();

        NoteFolderScanner.Scan(temp.Path, temp.Path, sampleAlways: false, int.MaxValue, new ScanBudget(maxEntries: 5, Plenty), TestContext.Current.CancellationToken, part).ShouldBeNull();

        part.Count.ShouldBe(1);
        part.Entries.ShouldBe(5);
        part.NotesOpened.ShouldBe(0);
    }

    [Fact]
    public void Scan_WithAPart_TheSameNotesAreCountedForEveryLook()
    {
        using var temp = new TempDirectory();
        MakeNotes(temp, ".", notes: 12, linked: 12);
        var part = new SearchStats.ScanPart();

        NoteFolderScanner.Scan(temp.Path, temp.Path, sampleAlways: false, int.MaxValue, Unlimited(), TestContext.Current.CancellationToken, part);
        NoteFolderScanner.Scan(temp.Path, temp.Path, sampleAlways: false, int.MaxValue, Unlimited(), TestContext.Current.CancellationToken, part);

        part.Count.ShouldBe(2);
        part.Entries.ShouldBe(24);
        part.NotesOpened.ShouldBe(24);
    }

    [Fact]
    public void HasEntryNote_WithAPart_NotesTheEntriesItTookAndTheFolder()
    {
        using var temp = new TempDirectory();
        MakeFiles(temp, ".", 1000);
        var part = new SearchStats.Part();

        NoteFolderScanner.HasEntryNote(temp.Path, Unlimited(), TestContext.Current.CancellationToken, part).ShouldBe(false);

        part.Count.ShouldBe(1);
        part.Entries.ShouldBe(1 + NoteFolderCheck.ProbeEntries);
        part.Slowest.ShouldBe(temp.Path);
    }

    [Fact]
    public void HasEntryNote_WithAPart_BudgetThatRunsOutBeforeIsStillNoted()
    {
        using var temp = new TempDirectory();
        var part = new SearchStats.Part();

        NoteFolderScanner.HasEntryNote(temp.Path, new ScanBudget(maxEntries: 0, Plenty), TestContext.Current.CancellationToken, part).ShouldBeNull();

        part.Count.ShouldBe(1);
        part.Entries.ShouldBe(0);
    }

    // ---- Which notes are chosen -----------------------------------------------------------------------------------

    [Fact]
    public void Choose_NoMoreNotesThanTheSample_TakesAllOfThemInOrder()
    {
        List<string> notes = [.. Enumerable.Range(1, 20).Select(i => $"/n/{i:D2}.md").Reverse()];

        NoteFolderScanner.Choose(notes).ShouldBe([.. Enumerable.Range(1, 20).Select(i => $"/n/{i:D2}.md")]);
    }

    [Fact]
    public void Choose_MoreNotesThanTheSample_TakesTwentySpreadEvenlyFromTheOrderedPaths()
    {
        // 40 notes in any order: every second one of the ordered paths, from the middle of each pair (the 2nd, 4th, ...).
        List<string> notes = [.. Enumerable.Range(1, 40).Select(i => $"/n/{i:D2}.md").OrderByDescending(path => path)];

        List<string> chosen = NoteFolderScanner.Choose(notes);

        chosen.ShouldBe([.. Enumerable.Range(1, 20).Select(i => $"/n/{2 * i:D2}.md")]);
    }

    [Theory]
    [InlineData(21)]
    [InlineData(27)]
    [InlineData(100)]
    [InlineData(999)]
    public void Choose_AnyNumberOfNotes_TakesTwentyDifferentOnesInOrderThatReachBothEnds(int count)
    {
        List<string> notes = [.. Enumerable.Range(1, count).Select(i => $"/n/{i:D4}.md")];

        List<string> chosen = NoteFolderScanner.Choose(notes);

        chosen.Count.ShouldBe(20);
        chosen.Distinct().Count().ShouldBe(20);
        chosen.ShouldBe([.. chosen.Order(StringComparer.Ordinal)]);
        // The first is in the first stretch of notes and the last in the last one.
        notes.IndexOf(chosen[0]).ShouldBeLessThan(count / 20 + 1);
        notes.IndexOf(chosen[^1]).ShouldBeGreaterThanOrEqualTo(count - count / 20 - 1);
    }

    [Fact]
    public void Choose_NoNotes_TakesNone() =>
        NoteFolderScanner.Choose([]).ShouldBeEmpty();

    [Fact]
    public void Choose_PathsAreOrderedByCharacterAndNotByTheCultureOfTheMachine()
    {
        List<string> notes = ["/b/Z.md", "/b/a.md", "/a/z.md", "/B/a.md"];

        // Uppercase letters come before lowercase ones, whatever the language of the machine.
        NoteFolderScanner.Choose(notes).ShouldBe(["/B/a.md", "/a/z.md", "/b/Z.md", "/b/a.md"]);
    }

    // ---- The budget and the limit of entries ---------------------------------------------------------------------

    [Fact]
    public void Scan_BudgetThatRunsOutWhileTheFolderIsRead_GivesNothing()
    {
        using var temp = new TempDirectory();
        MakeNotes(temp, ".", notes: 12, linked: 12);

        Scan(temp.Path, budget: new ScanBudget(maxEntries: 11, Plenty)).ShouldBeNull();
    }

    [Fact]
    public void Scan_BudgetThatRunsOutWhileTheSampleIsRead_GivesNothing()
    {
        using var temp = new TempDirectory();
        MakeNotes(temp, ".", notes: 12, linked: 12);

        // 12 entries to read the folder and 12 notes to read for the sample.
        Scan(temp.Path, budget: new ScanBudget(maxEntries: 23, Plenty)).ShouldBeNull();
        Scan(temp.Path, budget: new ScanBudget(maxEntries: 24, Plenty)).ShouldBe(new NoteFolderCheck(12, 12, 12, 12));
    }

    [Fact]
    public void Scan_EveryEntryReadAndEveryNoteOpened_IsTakenFromTheBudget()
    {
        using var temp = new TempDirectory();
        MakeNotes(temp, "a", notes: 6, linked: 6);
        MakeNotes(temp, "a/b", notes: 7, linked: 7);

        // The folder (1 entry, a), then a (6 notes and b), then b (7 notes): 15 entries; 13 notes opened for the sample: 28 in all.
        var exact = new ScanBudget(maxEntries: 28, Plenty);
        Scan(temp.Path, budget: exact).ShouldNotBeNull();

        exact.IsSpent.ShouldBeFalse();
        exact.TrySpend().ShouldBeFalse();
    }

    [Fact]
    public void Scan_TimeThatRunsOutInTheMiddleOfABigFolder_StopsTheLook()
    {
        using var temp = new TempDirectory();
        MakeFiles(temp, ".", 1000, "md");
        var clock = new SteppingTimeProvider(TimeSpan.FromMilliseconds(1));
        var budget = new ScanBudget(maxEntries: 100_000, TimeSpan.FromMilliseconds(50), clock);

        // Every entry read is asked of the budget before it is looked at, so the time that passes with them stops the look in the folder: not after the 1,000.
        Scan(temp.Path, budget: budget).ShouldBeNull();

        budget.IsSpent.ShouldBeTrue();
        clock.Reads.ShouldBeLessThan(60);
    }

    [Fact]
    public void Scan_TimeThatRunsOutWhileTheSampleIsRead_GivesNothing()
    {
        using var temp = new TempDirectory();
        MakeNotes(temp, ".", notes: 12, linked: 12);
        var clock = new SteppingTimeProvider(TimeSpan.FromMilliseconds(1));
        var budget = new ScanBudget(maxEntries: 100_000, TimeSpan.FromMilliseconds(20), clock);

        // The twelve entries take twelve of the nineteen asks that fit in 20 ms; the twelve notes of the sample take the other seven, and the eighth note is not opened.
        Scan(temp.Path, budget: budget).ShouldBeNull();

        budget.IsSpent.ShouldBeTrue();
    }

    [Fact]
    public void Scan_NoTime_GivesNothing()
    {
        using var temp = new TempDirectory();
        temp.Write("a.md", "x");

        Scan(temp.Path, budget: new ScanBudget(maxEntries: 1000, TimeSpan.Zero)).ShouldBeNull();
    }

    [Fact]
    public void Scan_FolderWithMoreEntriesThanTheLimit_IsJudgedByWhatWasReadAndTheRestIsNotTakenFromTheBudget()
    {
        using var temp = new TempDirectory();
        MakeNotes(temp, ".", notes: 8, linked: 8);
        var budget = new ScanBudget(maxEntries: 5, Plenty);

        NoteFolderCheck? check = Scan(temp.Path, maxEntries: 5, budget: budget);

        // Five of the eight entries (which five is the file system's choice); the sixth is not read, so it costs nothing.
        check.ShouldNotBeNull();
        check.Value.Notes.ShouldBe(5);
        check.Value.Files.ShouldBe(5);
        budget.IsSpent.ShouldBeFalse();
    }

    [Fact]
    public void Scan_LimitOfEntries_CutsWhatIsDeepestFirst()
    {
        using var temp = new TempDirectory();
        MakeNotes(temp, ".", notes: 3);
        MakeNotes(temp, "a", notes: 3);
        MakeNotes(temp, "a/b", notes: 3);

        // The folder itself (3 notes and a), then a (3 notes and b) are read before b.
        Scan(temp.Path, maxEntries: 8)!.Value.Notes.ShouldBe(6);
    }

    [Fact]
    public void Scan_RequestThatIsGone_StopsTheLook()
    {
        using var temp = new TempDirectory();
        temp.Write("a.md", "x");
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();

        Should.Throw<OperationCanceledException>(() => NoteFolderScanner.Scan(temp.Path, temp.Path, sampleAlways: false, int.MaxValue, Unlimited(), cancelled.Token));
    }
}
