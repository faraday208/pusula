using System.Runtime.Versioning;
using System.Text.Json;
using Pusula.History;
using Pusula.Indexing;
using Pusula.Links;
using Pusula.UnitTests.Support;
using Shouldly;
using Xunit;

namespace Pusula.UnitTests.History;

public sealed class HistoryFileTests
{
    private static readonly string Root = Path.GetFullPath("/home/someone/.claude");

    private static DailySummary Summary(string day, DateTimeOffset takenAt, (string Path, int Total, int EverySession)[] files, params BrokenLink[] broken) =>
        new(
            DateOnly.ParseExact(day, "yyyy-MM-dd"),
            takenAt,
            files.ToDictionary(file => file.Path, file => new FileTokens(file.Total, file.EverySession), DailySummary.Keys),
            broken);

    private static string Folder(TempDirectory temp) => temp.Resolve("state/pusula/history");

    // ---- The name of the file -----------------------------------------------------------------------------------

    [Fact]
    public void NameOf_Source_IsSixteenLowercaseHexCharactersAndJson() =>
        HistoryFile.NameOf(SourceProfile.Claude, Root).ShouldMatch("^[0-9a-f]{16}\\.json$");

    // The first 16 hexadecimal digits of the SHA-256 of "Claude\n/home/someone/.claude" (sha256sum), a name that does not change from one
    // version to the next: the file a version wrote must be the file the next one reads.
    [Fact]
    public void NameOf_ClaudeFolderOnAUnixMachine_IsTheKnownName()
    {
        Assert.SkipWhen(OperatingSystem.IsWindows(), "The folder is spelled the way of Unix.");

        HistoryFile.NameOf(SourceProfile.Claude, "/home/someone/.claude").ShouldBe("ba8e32ccd6583eae.json");
    }

    [Fact]
    public void NameOf_SameSource_IsTheSameEveryTime() =>
        HistoryFile.NameOf(SourceProfile.Vault, Root).ShouldBe(HistoryFile.NameOf(SourceProfile.Vault, Root));

    [Fact]
    public void NameOf_OtherProfile_IsAnotherName() =>
        new[] { SourceProfile.Claude, SourceProfile.Vault, SourceProfile.Markdown }
            .Select(profile => HistoryFile.NameOf(profile, Root)).Distinct().Count().ShouldBe(3);

    [Fact]
    public void NameOf_OtherFolder_IsAnotherName() =>
        HistoryFile.NameOf(SourceProfile.Claude, Root).ShouldNotBe(HistoryFile.NameOf(SourceProfile.Claude, Root + "-other"));

    // Where paths ignore case the same folder written in two ways is one source; elsewhere they are two folders.
    [Fact]
    public void NameOf_FolderInAnotherCase_IsTheSameNameOnlyWherePathsIgnoreCase()
    {
        string upper = HistoryFile.NameOf(SourceProfile.Claude, Root.ToUpperInvariant());
        string lower = HistoryFile.NameOf(SourceProfile.Claude, Root.ToLowerInvariant());

        (upper == lower).ShouldBe(OperatingSystem.IsWindows());
    }

    [Fact]
    public void RootOf_Index_IsTheFullPathWithoutASeparatorAtTheEnd()
    {
        string folder = Path.GetFullPath("/home/someone/notes");

        HistoryFile.RootOf(IndexFactory.Index([], root: folder + Path.DirectorySeparatorChar)).ShouldBe(folder);
        HistoryFile.RootOf(IndexFactory.Index([], root: folder)).ShouldBe(folder);
    }

    // ---- The format ---------------------------------------------------------------------------------------------

    [Fact]
    public void Write_Summaries_AreCompactJsonOfTheDocumentedShape()
    {
        using var temp = new TempDirectory();
        DailySummary summary = Summary(
            "2026-10-12",
            new DateTimeOffset(2026, 10, 12, 9, 14, 3, TimeSpan.FromHours(3)),
            [("CLAUDE.md", 3495, 3495), ("kurallar ve şablon.md", 80, 0)],
            new BrokenLink("guides/setup.md", LinkKind.ClaudePath, "~/.claude/X.md"),
            new BrokenLink("rules/a.md", LinkKind.WikiLink, "bir <not> & 'öbür'"));

        HistoryFile.Write(Folder(temp), "abc.json", SourceProfile.Claude, "/home/me/.claude", [summary]);

        File.ReadAllText(Path.Join(Folder(temp), "abc.json")).ShouldBe(
            """{"version":1,"root":"/home/me/.claude","profile":"Claude","days":[{"day":"2026-10-12","takenAt":"2026-10-12T09:14:03+03:00","files":{"CLAUDE.md":[3495,3495],"kurallar ve şablon.md":[80,0]},"broken":[["guides/setup.md","ClaudePath","~/.claude/X.md"],["rules/a.md","WikiLink","bir <not> & 'öbür'"]]}]}""");
    }

    [Fact]
    public void Write_Days_AreWrittenOldestFirstWhateverTheOrderTheyAreGivenIn()
    {
        using var temp = new TempDirectory();
        DateTimeOffset taken = new(2026, 10, 12, 9, 0, 0, TimeSpan.Zero);

        HistoryFile.Write(Folder(temp), "abc.json", SourceProfile.Claude, Root, [Summary("2026-10-12", taken, []), Summary("2026-10-05", taken, []), Summary("2026-10-09", taken, [])]);

        using JsonDocument json = JsonDocument.Parse(File.ReadAllText(Path.Join(Folder(temp), "abc.json")));
        json.RootElement.GetProperty("days").EnumerateArray().Select(day => day.GetProperty("day").GetString()).ShouldBe(["2026-10-05", "2026-10-09", "2026-10-12"]);
    }

    [Fact]
    public void Write_Then_Read_GivesTheSameSummaries()
    {
        using var temp = new TempDirectory();
        DateTimeOffset taken = new(2026, 10, 12, 9, 14, 3, 123, TimeSpan.FromHours(3));
        DailySummary first = Summary("2026-10-11", taken.AddDays(-1), [("CLAUDE.md", 3400, 3400)], new BrokenLink("a.md", LinkKind.Embed, "pic.png"));
        DailySummary second = Summary("2026-10-12", taken, [("CLAUDE.md", 3495, 3495), ("skills/s/SKILL.md", 1200, 64), ("kurallar ve şablon.md", 80, 0)], new BrokenLink("a.md", LinkKind.RelativePath, "rules/x.md"));
        HistoryFile.Write(Folder(temp), "abc.json", SourceProfile.Claude, Root, [first, second]);

        HistoryRead read = HistoryFile.Read(Path.Join(Folder(temp), "abc.json"), SourceProfile.Claude, Root);

        read.Problem.ShouldBeNull();
        read.Days.Count.ShouldBe(2);
        foreach ((DailySummary written, DailySummary got) in new[] { first, second }.Zip(read.Days))
        {
            got.Day.ShouldBe(written.Day);
            got.TakenAt.ShouldBe(written.TakenAt);
            got.TakenAt.Offset.ShouldBe(written.TakenAt.Offset);
            got.Files.OrderBy(file => file.Key, StringComparer.Ordinal).ShouldBe(written.Files.OrderBy(file => file.Key, StringComparer.Ordinal));
            got.Broken.ShouldBe(written.Broken);
        }
    }

    [Fact]
    public void Write_FolderThatDoesNotExist_MakesItAndLeavesOnlyTheFile()
    {
        using var temp = new TempDirectory();

        HistoryFile.Write(Folder(temp), "abc.json", SourceProfile.Claude, Root, [Summary("2026-10-12", DateTimeOffset.UnixEpoch, [])]);

        Directory.GetFileSystemEntries(Folder(temp)).ShouldBe([Path.Join(Folder(temp), "abc.json")]);
    }

    [Fact]
    public void Write_ExistingFile_ReplacesItWholeAndLeavesNothingElseInTheFolder()
    {
        using var temp = new TempDirectory();
        temp.Write("state/pusula/history/abc.json", new string('x', 10_000));

        HistoryFile.Write(Folder(temp), "abc.json", SourceProfile.Claude, Root, [Summary("2026-10-12", DateTimeOffset.UnixEpoch, [])]);

        File.ReadAllText(Path.Join(Folder(temp), "abc.json")).ShouldStartWith("{\"version\":1,");
        Directory.GetFileSystemEntries(Folder(temp)).ShouldBe([Path.Join(Folder(temp), "abc.json")]);
    }

    [Fact]
    public void Write_FileThatCannotBeReplaced_ThrowsAndLeavesNoTemporaryFileBehind()
    {
        using var temp = new TempDirectory();

        // A folder where the file should be: the new file cannot take its place.
        temp.Write("state/pusula/history/abc.json/keep.txt", "x");

        // A folder in the place of the file is refused with access denied (an UnauthorizedAccessException) on Windows, with an IOException elsewhere.
        Exception thrown = Should.Throw<Exception>(() =>
            HistoryFile.Write(Folder(temp), "abc.json", SourceProfile.Claude, Root, [Summary("2026-10-12", DateTimeOffset.UnixEpoch, [])]));

        thrown.ShouldBeAssignableTo(OperatingSystem.IsWindows() ? typeof(UnauthorizedAccessException) : typeof(IOException));
        Directory.GetFileSystemEntries(Folder(temp)).ShouldBe([Path.Join(Folder(temp), "abc.json")]);
    }

    [Fact]
    public void TemporaryNameOf_File_StartsWithADotAndEndsWithTmpAndIsNeverTheSameTwice()
    {
        string first = HistoryFile.TemporaryNameOf("abc.json");
        string second = HistoryFile.TemporaryNameOf("abc.json");

        first.ShouldStartWith(".abc.json.");
        first.ShouldEndWith(".tmp");
        first.ShouldNotBe(second);
    }

    [Fact]
    [UnsupportedOSPlatform("windows")]
    public void Write_OnUnix_MakesTheFoldersWithMode0700AndTheFileWithMode0600()
    {
        Assert.SkipWhen(OperatingSystem.IsWindows(), "Unix permissions.");
        using var temp = new TempDirectory();

        HistoryFile.Write(Folder(temp), "abc.json", SourceProfile.Claude, Root, [Summary("2026-10-12", DateTimeOffset.UnixEpoch, [])]);

        const UnixFileMode Folder0700 = UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute;
        File.GetUnixFileMode(temp.Resolve("state/pusula")).ShouldBe(Folder0700);
        File.GetUnixFileMode(Folder(temp)).ShouldBe(Folder0700);
        File.GetUnixFileMode(Path.Join(Folder(temp), "abc.json")).ShouldBe(UnixFileMode.UserRead | UnixFileMode.UserWrite);
    }

    [Fact]
    [UnsupportedOSPlatform("windows")]
    public void Write_OnUnix_LeavesTheModeOfFoldersThatExistAlone()
    {
        Assert.SkipWhen(OperatingSystem.IsWindows(), "Unix permissions.");
        using var temp = new TempDirectory();
        string pusula = temp.CreateDirectory("state/pusula");
        File.SetUnixFileMode(pusula, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute | UnixFileMode.GroupRead | UnixFileMode.GroupExecute);

        HistoryFile.Write(Folder(temp), "abc.json", SourceProfile.Claude, Root, [Summary("2026-10-12", DateTimeOffset.UnixEpoch, [])]);

        File.GetUnixFileMode(pusula).ShouldBe(UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute | UnixFileMode.GroupRead | UnixFileMode.GroupExecute);
    }

    // ---- Reading ------------------------------------------------------------------------------------------------

    private static HistoryRead ReadText(TempDirectory temp, string text, SourceProfile profile = SourceProfile.Claude, string? root = null)
    {
        string file = temp.Write("abc.json", text);
        return HistoryFile.Read(file, profile, root ?? Root);
    }

    private static string FileOf(string root, string profile, string days, int version = 1) =>
        JsonSerializer.Serialize(new { version, root, profile }).TrimEnd('}') + ",\"days\":" + days + "}";

    [Fact]
    public void Read_FileThatIsNotThere_HoldsNothingAndIsNoProblem()
    {
        using var temp = new TempDirectory();

        HistoryRead read = HistoryFile.Read(temp.Resolve("abc.json"), SourceProfile.Claude, Root);

        read.Days.ShouldBeEmpty();
        read.Problem.ShouldBeNull();
    }

    [Fact]
    public void Read_FolderThatIsNotThere_HoldsNothingAndIsNoProblem()
    {
        using var temp = new TempDirectory();

        HistoryRead read = HistoryFile.Read(temp.Resolve("no/such/folder/abc.json"), SourceProfile.Claude, Root);

        read.Days.ShouldBeEmpty();
        read.Problem.ShouldBeNull();
    }

    [Fact]
    public void Read_FileWithNoDays_HoldsNothingAndIsNoProblem()
    {
        using var temp = new TempDirectory();

        HistoryRead read = ReadText(temp, FileOf(Root, "Claude", "[]"));

        read.Days.ShouldBeEmpty();
        read.Problem.ShouldBeNull();
    }

    [Fact]
    public void Read_FileThatIsAFolder_CannotBeUsed()
    {
        using var temp = new TempDirectory();
        temp.Write("abc.json/keep.txt", "x");

        HistoryRead read = HistoryFile.Read(temp.Resolve("abc.json"), SourceProfile.Claude, Root);

        read.Days.ShouldBeEmpty();
        read.Problem.ShouldNotBeNull();
    }

    [Theory]
    [InlineData("")]
    [InlineData("not json")]
    [InlineData("{\"version\":1,")]
    [InlineData("null")]
    [InlineData("[]")]
    [InlineData("42")]
    [InlineData("\"text\"")]
    public void Read_FileThatIsNotAHistoryFileInJson_CannotBeUsed(string text)
    {
        using var temp = new TempDirectory();

        HistoryRead read = ReadText(temp, text);

        read.Days.ShouldBeEmpty();
        read.Problem.ShouldNotBeNull();
        read.Problem.ShouldContain("not a history file");
    }

    [Fact]
    public void Read_FileOfAnotherVersion_CannotBeUsed()
    {
        using var temp = new TempDirectory();

        HistoryRead read = ReadText(temp, FileOf(Root, "Claude", "[]", version: 2));

        read.Days.ShouldBeEmpty();
        read.Problem.ShouldBe("it is of version 2, and this pusula reads version 1");
    }

    [Fact]
    public void Read_FileWithoutAVersion_CannotBeUsed()
    {
        using var temp = new TempDirectory();

        HistoryRead read = ReadText(temp, "{\"root\":\"" + JsonEncodedText.Encode(Root) + "\",\"profile\":\"Claude\",\"days\":[]}");

        read.Problem.ShouldNotBeNull();
        read.Problem.ShouldContain("version 0");
    }

    [Fact]
    public void Read_FileOfAnotherFolder_CannotBeUsed()
    {
        using var temp = new TempDirectory();

        HistoryRead read = ReadText(temp, FileOf(Root + "-other", "Claude", "[]"));

        read.Days.ShouldBeEmpty();
        read.Problem.ShouldNotBeNull();
        read.Problem.ShouldContain("another source");
    }

    [Fact]
    public void Read_FileOfAnotherProfile_CannotBeUsed()
    {
        using var temp = new TempDirectory();

        HistoryRead read = ReadText(temp, FileOf(Root, "Vault", "[]"));

        read.Days.ShouldBeEmpty();
        read.Problem.ShouldNotBeNull();
        read.Problem.ShouldContain("another source");
    }

    // The folder is compared the way paths are compared: the same folder in another case is the same source where paths ignore case.
    [Fact]
    public void Read_FileOfTheSameFolderInAnotherCase_CanBeUsedOnlyWherePathsIgnoreCase()
    {
        using var temp = new TempDirectory();

        HistoryRead read = ReadText(temp, FileOf(Root.ToUpperInvariant(), "Claude", "[]"));

        (read.Problem is null).ShouldBe(OperatingSystem.IsWindows());
    }

    [Theory]
    [InlineData("[null]")]
    [InlineData("[{}]")]
    [InlineData("[{\"day\":\"2026-10-12\"}]")]
    [InlineData("[{\"day\":\"yesterday\",\"takenAt\":\"2026-10-12T09:00:00+00:00\",\"files\":{},\"broken\":[]}]")]
    [InlineData("[{\"day\":\"2026-10-12\",\"takenAt\":\"soon\",\"files\":{},\"broken\":[]}]")]
    [InlineData("[{\"day\":\"2026-10-12\",\"takenAt\":\"2026-10-12T09:00:00+00:00\",\"files\":[],\"broken\":[]}]")]
    [InlineData("[{\"day\":\"2026-10-12\",\"takenAt\":\"2026-10-12T09:00:00+00:00\",\"files\":{\"a.md\":null},\"broken\":[]}]")]
    [InlineData("[{\"day\":\"2026-10-12\",\"takenAt\":\"2026-10-12T09:00:00+00:00\",\"files\":{\"a.md\":[1]},\"broken\":[]}]")]
    [InlineData("[{\"day\":\"2026-10-12\",\"takenAt\":\"2026-10-12T09:00:00+00:00\",\"files\":{\"a.md\":[1,2,3]},\"broken\":[]}]")]
    [InlineData("[{\"day\":\"2026-10-12\",\"takenAt\":\"2026-10-12T09:00:00+00:00\",\"files\":{\"a.md\":[-1,0]},\"broken\":[]}]")]
    [InlineData("[{\"day\":\"2026-10-12\",\"takenAt\":\"2026-10-12T09:00:00+00:00\",\"files\":{\"a.md\":[\"1\",\"2\"]},\"broken\":[]}]")]
    [InlineData("[{\"day\":\"2026-10-12\",\"takenAt\":\"2026-10-12T09:00:00+00:00\",\"files\":{\"a.md\":[1.5,2]},\"broken\":[]}]")]
    [InlineData("[{\"day\":\"2026-10-12\",\"takenAt\":\"2026-10-12T09:00:00+00:00\",\"files\":{},\"broken\":{}}]")]
    [InlineData("[{\"day\":\"2026-10-12\",\"takenAt\":\"2026-10-12T09:00:00+00:00\",\"files\":{},\"broken\":[null]}]")]
    [InlineData("[{\"day\":\"2026-10-12\",\"takenAt\":\"2026-10-12T09:00:00+00:00\",\"files\":{},\"broken\":[[\"a.md\",\"WikiLink\"]]}]")]
    [InlineData("[{\"day\":\"2026-10-12\",\"takenAt\":\"2026-10-12T09:00:00+00:00\",\"files\":{},\"broken\":[[\"a.md\",\"WikiLink\",\"x\",\"y\"]]}]")]
    [InlineData("[{\"day\":\"2026-10-12\",\"takenAt\":\"2026-10-12T09:00:00+00:00\",\"files\":{},\"broken\":[[\"a.md\",\"NoSuchKind\",\"x\"]]}]")]
    [InlineData("[{\"day\":\"2026-10-12\",\"takenAt\":\"2026-10-12T09:00:00+00:00\",\"files\":{},\"broken\":[[\"a.md\",\"42\",\"x\"]]}]")]
    [InlineData("[{\"day\":\"2026-10-12\",\"takenAt\":\"2026-10-12T09:00:00+00:00\",\"files\":{},\"broken\":[[\"a.md\",\"wikilink\",\"x\"]]}]")]
    [InlineData("[{\"day\":\"2026-10-12\",\"takenAt\":\"2026-10-12T09:00:00+00:00\",\"files\":{},\"broken\":[[null,\"WikiLink\",\"x\"]]}]")]
    [InlineData("[{\"day\":\"2026-10-12\",\"takenAt\":\"2026-10-12T09:00:00+00:00\",\"files\":{},\"broken\":[[\"a.md\",\"WikiLink\",null]]}]")]
    public void Read_FileWithADayThatIsNotValid_CannotBeUsed(string days)
    {
        using var temp = new TempDirectory();

        HistoryRead read = ReadText(temp, FileOf(Root, "Claude", days));

        read.Days.ShouldBeEmpty();
        read.Problem.ShouldNotBeNull();
    }

    [Fact]
    public void Read_FileWithADayThatLacksAPart_SaysSo()
    {
        using var temp = new TempDirectory();

        HistoryRead read = ReadText(temp, FileOf(Root, "Claude", "[{\"day\":\"2026-10-12\"}]"));

        read.Problem.ShouldBe("a day in it is not valid");
    }

    [Fact]
    public void Read_FileWithAPropertyTwice_CannotBeUsed()
    {
        using var temp = new TempDirectory();

        HistoryRead read = ReadText(temp, "{\"version\":1,\"version\":1,\"root\":\"" + JsonEncodedText.Encode(Root) + "\",\"profile\":\"Claude\",\"days\":[]}");

        read.Days.ShouldBeEmpty();
        read.Problem.ShouldNotBeNull();
    }

    [Fact]
    public void Read_FileWithPropertiesThatThisPusulaDoesNotKnow_IsReadAllTheSame()
    {
        using var temp = new TempDirectory();
        string day = "{\"day\":\"2026-10-12\",\"takenAt\":\"2026-10-12T09:00:00+00:00\",\"files\":{\"a.md\":[3,1]},\"broken\":[],\"note\":\"hello\"}";

        HistoryRead read = ReadText(temp, FileOf(Root, "Claude", "[" + day + "]").Replace("\"version\":1", "\"version\":1,\"extra\":true", StringComparison.Ordinal));

        read.Problem.ShouldBeNull();
        read.Days.Single().Files["a.md"].ShouldBe(new FileTokens(3, 1));
    }

    [Fact]
    public void Read_FileWithABrokenLink_GivesItsFileKindAndText()
    {
        using var temp = new TempDirectory();
        string day = "{\"day\":\"2026-10-12\",\"takenAt\":\"2026-10-12T09:00:00+00:00\",\"files\":{},\"broken\":[[\"guides/setup.md\",\"ClaudePath\",\"~/.claude/X.md\"]]}";

        HistoryRead read = ReadText(temp, FileOf(Root, "Claude", "[" + day + "]"));

        read.Days.Single().Broken.ShouldBe([new BrokenLink("guides/setup.md", LinkKind.ClaudePath, "~/.claude/X.md")]);
    }

    [Fact]
    public void Read_FileWithAByteOrderMark_IsReadAllTheSame()
    {
        using var temp = new TempDirectory();
        string file = temp.Resolve("abc.json");
        File.WriteAllText(file, FileOf(Root, "Claude", "[]"), new System.Text.UTF8Encoding(encoderShouldEmitUTF8Identifier: true));

        HistoryRead read = HistoryFile.Read(file, SourceProfile.Claude, Root);

        read.Problem.ShouldBeNull();
    }
}
