using System.Text.Json;
using Microsoft.Extensions.Logging;
using Pusula.Indexing;
using Pusula.IntegrationTests.Support;
using Pusula.Links;
using Pusula.Sources;
using Shouldly;
using Xunit;

namespace Pusula.IntegrationTests.Sources;

/// <summary>
/// The registry on its own: adding and removing sources, and following the sources file when it changes. Everything
/// happens in scratch directories (a made-up home directory, a made-up sources file); the registry has real file
/// watchers, and the tests that depend on them wait with a time limit.
/// </summary>
public sealed class SourceRegistryTests
{
    private static string[] Fields(JsonElement item) =>
        [.. item.EnumerateObject().Select(property => $"{property.Name}={property.Value.GetString()}")];

    private static string Json(string path) => JsonSerializer.Serialize(path);

    private static SourceEntry Entry(RegistryHarness harness, string id) =>
        harness.Registry.Sources.Single(source => source.Definition.Id == id);

    private static int Warnings(RegistryHarness harness, string text) =>
        harness.Log.Entries.Count(entry => entry.Level == LogLevel.Warning && entry.Message.Contains(text, StringComparison.Ordinal));

    // ---- Adding -------------------------------------------------------------------------------------------------

    [Fact]
    public async Task AddAsync_NoSourcesFileYet_WritesTheListThatWasShownAndTheNewSourceAndKeepsTheRunningOneRunning()
    {
        await using var harness = new RegistryHarness();
        await harness.StartAsync();
        SourceEntry claude = Entry(harness, "claude");
        string notes = harness.Folder("notes", "A.md", "B.md");
        File.Exists(harness.SourcesFile).ShouldBeFalse();
        Directory.Exists(Path.GetDirectoryName(harness.SourcesFile)).ShouldBeFalse();

        SourceEditResult result = await harness.AddAsync(notes);

        result.Error.ShouldBeNull();
        SourceEntry added = result.Entry.ShouldNotBeNull();
        added.Definition.ShouldBe(new SourceDefinition("notes", "notes", notes, SourceProfile.Markdown));
        added.IsAvailable.ShouldBeTrue();
        added.Index!.Current.Files.Count.ShouldBe(2);
        harness.Ids.ShouldBe(["claude", "notes"]);
        Entry(harness, "claude").Index.ShouldBeSameAs(claude.Index);
        harness.Registry.SourcesFile.ShouldBe(harness.SourcesFile);

        JsonElement[] items = harness.ReadItems();
        items.Length.ShouldBe(2);
        Fields(items[0]).ShouldBe(["id=claude", "name=.claude", "path=~/.claude", "profile=auto"]);
        Fields(items[1]).ShouldBe(["id=notes", "name=notes", $"path={notes}", "profile=auto"]);
    }

    [Fact]
    public async Task AddAsync_ExistingFile_KeepsTheOtherItemsAsTheyWereWritten()
    {
        await using var harness = new RegistryHarness();
        harness.HomeFolder("Documents/old", "O.md");
        string gone = harness.Missing("gone");
        harness.WriteSourcesFile(
            $$"""
            {
              // lost when the file is written again
              "sources": [
                { "path": "~/Documents/old", "profile": "auto" },
                { "id": "gone", "path": {{Json(gone)}} },
              ],
              "other": [1, 2],
            }
            """);
        await harness.StartAsync();
        harness.Ids.ShouldBe(["old", "gone"]);
        string notes = harness.Folder("notes", "N.md");

        SourceEditResult result = await harness.AddAsync(notes, name: "Notes", profile: "Markdown");

        result.Error.ShouldBeNull();
        harness.Ids.ShouldBe(["old", "gone", "notes"]);
        harness.Registry.Sources[1].IsAvailable.ShouldBeFalse();
        JsonElement[] items = harness.ReadItems();
        Fields(items[0]).ShouldBe(["path=~/Documents/old", "profile=auto"]);
        Fields(items[1]).ShouldBe(["id=gone", $"path={gone}"]);
        Fields(items[2]).ShouldBe(["id=notes", "name=Notes", $"path={notes}", "profile=markdown"]);
        harness.ReadSourcesFile().ShouldNotContain("lost when");
        using JsonDocument document = JsonDocument.Parse(harness.ReadSourcesFile());
        document.RootElement.GetProperty("other").EnumerateArray().Select(number => number.GetInt32()).ShouldBe([1, 2]);
    }

    [Fact]
    public async Task AddAsync_PathWithATilde_IsWrittenAsTypedAndTheSourceHasTheFullPath()
    {
        await using var harness = new RegistryHarness();
        await harness.StartAsync();
        string folder = harness.HomeFolder("Documents/new", "N.md");

        SourceEditResult result = await harness.AddAsync("~/Documents/new/");

        result.Error.ShouldBeNull();
        result.Entry!.Definition.Path.ShouldBe(folder);
        Fields(harness.ReadItems()[1]).ShouldBe(["id=new", "name=new", "path=~/Documents/new", "profile=auto"]);
    }

    [Fact]
    public async Task AddAsync_NameAndProfile_AreWrittenAndUsed()
    {
        await using var harness = new RegistryHarness();
        await harness.StartAsync();
        string folder = harness.Folder("x", "N.md");

        SourceEditResult result = await harness.AddAsync(folder, name: "  Notlarım  ", profile: "VAULT");

        result.Error.ShouldBeNull();
        result.Entry!.Definition.ShouldBe(new SourceDefinition("notlarim", "Notlarım", folder, SourceProfile.Vault));
        Fields(harness.ReadItems()[1]).ShouldBe(["id=notlarim", "name=Notlarım", $"path={folder}", "profile=vault"]);
    }

    [Fact]
    public async Task AddAsync_FolderAlreadyListed_FailsAndLeavesTheFileAndTheSourcesAlone()
    {
        await using var harness = new RegistryHarness();
        string notes = harness.Folder("notes", "A.md");
        string missing = harness.Missing("later");
        harness.WriteSourcesFile($$"""{ "sources": [ { "path": {{Json(notes + Path.DirectorySeparatorChar)}} }, { "path": {{Json(missing)}} } ] }""");
        await harness.StartAsync();
        byte[] before = File.ReadAllBytes(harness.SourcesFile);
        harness.Folder("later", "L.md");

        SourceEditResult written = await harness.AddAsync(notes);
        SourceEditResult another = await harness.AddAsync(Path.Join(notes, "..", "notes"));
        SourceEditResult unavailableUntilNow = await harness.AddAsync(missing);

        foreach (SourceEditResult result in new[] { written, another, unavailableUntilNow })
        {
            result.Error.ShouldBe(EditError.AlreadyListed);
            result.Entry.ShouldBeNull();
        }

        File.ReadAllBytes(harness.SourcesFile).ShouldBe(before);
        harness.Ids.ShouldBe(["notes", "later"]);
    }

    [Fact]
    public async Task AddAsync_FoldersWithTheSameName_GetDifferentIds()
    {
        await using var harness = new RegistryHarness();
        await harness.StartAsync();

        SourceEditResult first = await harness.AddAsync(harness.Folder("a/notes", "A.md"));
        SourceEditResult second = await harness.AddAsync(harness.Folder("b/notes", "B.md"));
        SourceEditResult third = await harness.AddAsync(harness.Folder("c/other", "C.md"), name: "Notes");
        SourceEditResult fourth = await harness.AddAsync(harness.Folder("d/claude", "D.md"));

        new[] { first, second, third, fourth }.Select(result => result.Entry!.Definition.Id).ShouldBe(["notes", "notes-2", "notes-3", "claude-2"]);
        harness.Ids.ShouldBe(["claude", "notes", "notes-2", "notes-3", "claude-2"]);
        harness.ReadItems().Select(item => item.GetProperty("id").GetString()).ShouldBe(harness.Ids);
    }

    [Fact]
    public async Task AddAsync_WrittenAndDerivedIdsOfTheFile_AreNeverTakenAway()
    {
        await using var harness = new RegistryHarness();
        harness.WriteSourcesFile(
            $$"""{ "sources": [ { "path": {{Json(harness.Folder("one/notes", "A.md"))}} }, { "id": "notes-2", "path": {{Json(harness.Folder("two/x", "B.md"))}} } ] }""");
        await harness.StartAsync();
        harness.Ids.ShouldBe(["notes", "notes-2"]);

        SourceEditResult result = await harness.AddAsync(harness.Folder("three/notes", "C.md"));

        result.Entry!.Definition.Id.ShouldBe("notes-3");
        harness.Ids.ShouldBe(["notes", "notes-2", "notes-3"]);
        Entry(harness, "notes").Definition.Path.ShouldEndWith("one" + Path.DirectorySeparatorChar + "notes");
    }

    [Fact]
    public async Task AddAsync_FileThatCannotBeUsed_FailsLeavesItAloneAndRemembersWhy()
    {
        await using var harness = new RegistryHarness();
        harness.WriteSourcesFile($$"""{ "sources": [ { "path": {{Json(harness.Folder("a", "A.md"))}} } ] }""");
        await harness.StartAsync();
        SourceEntry before = Entry(harness, "a");
        harness.WriteSourcesFile("""{ "sources": [ { "path": """);
        byte[] broken = File.ReadAllBytes(harness.SourcesFile);

        SourceEditResult result = await harness.AddAsync(harness.Folder("notes", "N.md"));

        result.Error.ShouldBe(EditError.FileInvalid);
        result.Entry.ShouldBeNull();
        result.Reason.ShouldNotBeNull().ShouldStartWith("invalid JSON: ");
        File.ReadAllBytes(harness.SourcesFile).ShouldBe(broken);
        harness.Ids.ShouldBe(["a"]);
        Entry(harness, "a").ShouldBeSameAs(before);
        harness.Registry.SourcesFileError.ShouldBe(result.Reason);
        Warnings(harness, "cannot be used").ShouldBe(1);
    }

    [Theory]
    [InlineData("""{ "sources": [ { "name": "x" } ] }""", "source 1: \"path\" is required")]
    [InlineData("""{ "sources": [ { "path": "/a/x", "profile": "obsidian" } ] }""", "source 1: unknown profile")]
    [InlineData("""{ "sources": {} }""", "expected an object with a \"sources\" array")]
    [InlineData("[]", "expected an object with a \"sources\" array")]
    public async Task AddAsync_FileThatIsNotAList_SaysWhatIsWrongWithIt(string text, string expectedStart)
    {
        await using var harness = new RegistryHarness();
        await harness.StartAsync();
        harness.WriteSourcesFile(text);

        SourceEditResult result = await harness.AddAsync(harness.Folder("notes", "N.md"));

        result.Error.ShouldBe(EditError.FileInvalid);
        result.Reason.ShouldNotBeNull().ShouldStartWith(expectedStart);
        harness.ReadSourcesFile().ShouldBe(text);
    }

    [Fact]
    public async Task AddAsync_AfterTheFileWasFixed_WorksAndTheErrorIsGone()
    {
        await using var harness = new RegistryHarness();
        harness.WriteSourcesFile($$"""{ "sources": [ { "path": {{Json(harness.Folder("a", "A.md"))}} } ] }""");
        await harness.StartAsync();
        string valid = harness.ReadSourcesFile();
        harness.WriteSourcesFile("{ nope");
        (await harness.AddAsync(harness.Folder("notes", "N.md"))).Error.ShouldBe(EditError.FileInvalid);
        harness.Registry.SourcesFileError.ShouldNotBeNull();

        harness.WriteSourcesFile(valid);
        SourceEditResult result = await harness.AddAsync(harness.Folder("notes", "N.md"));

        result.Error.ShouldBeNull();
        harness.Registry.SourcesFileError.ShouldBeNull();
        harness.Ids.ShouldBe(["a", "notes"]);
    }

    [Fact]
    public async Task AddAsync_ListFromTheCommandLine_FailsAndWritesNothing()
    {
        await using var harness = new RegistryHarness();
        harness.Roots = [harness.Folder("given", "G.md")];
        await harness.StartAsync();

        SourceEditResult added = await harness.AddAsync(harness.Folder("notes", "N.md"));
        SourceEditResult removed = await harness.RemoveAsync("given");

        added.Error.ShouldBe(EditError.CommandLine);
        removed.Error.ShouldBe(EditError.CommandLine);
        harness.Registry.SourcesFile.ShouldBeNull();
        harness.Ids.ShouldBe(["given"]);
        Directory.Exists(Path.GetDirectoryName(harness.SourcesFile)).ShouldBeFalse();
    }

    [Fact]
    public async Task AddAsync_FileThatCannotBeWritten_FailsLeavesNothingBehindAndChangesNothing()
    {
        await using var harness = new RegistryHarness();

        // A folder where the sources file should be: nothing can take its place.
        Directory.CreateDirectory(harness.SourcesFile);
        await harness.StartAsync();
        SourceEntry claude = Entry(harness, "claude");

        SourceEditResult result = await harness.AddAsync(harness.Folder("notes", "N.md"));

        result.Error.ShouldBe(EditError.WriteFailed);
        harness.Ids.ShouldBe(["claude"]);
        Entry(harness, "claude").ShouldBeSameAs(claude);
        Directory.GetFileSystemEntries(Path.GetDirectoryName(harness.SourcesFile)!).ShouldBe([harness.SourcesFile]);
        harness.Log.Entries.ShouldContain(entry => entry.Level == LogLevel.Error && entry.Message.Contains("could not be written", StringComparison.Ordinal));
    }

    [Fact]
    public async Task AddAsync_SeveralAtOnce_AreAllAddedAndTheFileHasAllOfThem()
    {
        await using var harness = new RegistryHarness();
        await harness.StartAsync();
        string[] folders = [.. Enumerable.Range(1, 6).Select(number => harness.Folder($"f{number}", "A.md"))];

        SourceEditResult[] results = await Task.WhenAll(folders.Select(folder => harness.AddAsync(folder)));

        results.ShouldAllBe(result => result.Error == null);
        harness.Ids.Length.ShouldBe(7);
        harness.Ids.Distinct().Count().ShouldBe(7);
        harness.ReadItems().Select(item => item.GetProperty("id").GetString()).ShouldBe(harness.Ids);
        folders.ShouldAllBe(folder => harness.Registry.Sources.Any(source => source.Definition.Path == folder && source.IsAvailable));
    }

    // ---- A folder that is too large to show ----------------------------------------------------------------------

    private static readonly ScanLimits ThreeFiles = new(MaxFiles: 3, MaxDirectories: 100, MaxBytes: 100_000_000);

    private const string TooManyFiles = "The folder is too large to show: more than 3 Markdown files.";

    [Fact]
    public async Task StartAsync_FolderOfASourcesFileThatIsTooLarge_IsNotAvailableWithTheReasonAndTheOthersWork()
    {
        await using var harness = new RegistryHarness { Limits = ThreeFiles };
        string big = harness.Folder("big", "1.md", "2.md", "3.md", "4.md");
        string small = harness.Folder("small", "A.md");
        harness.WriteSourcesFile($$"""{ "sources": [ { "path": {{Json(big)}} }, { "path": {{Json(small)}} } ] }""");

        await harness.StartAsync();

        SourceEntry tooLarge = Entry(harness, "big");
        tooLarge.IsAvailable.ShouldBeFalse();
        tooLarge.Index.ShouldBeNull();
        tooLarge.Error.ShouldBe(TooManyFiles);
        tooLarge.ErrorCode.ShouldBe(SourceErrorCode.TooLarge);
        Entry(harness, "small").IsAvailable.ShouldBeTrue();
        Entry(harness, "small").ErrorCode.ShouldBeNull();
        Warnings(harness, "is not available").ShouldBe(1);

        // Not a failure of the server: no stack trace in the log.
        harness.Log.Entries.ShouldNotContain(entry => entry.Message.Contains("could not be started", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ReloadAsync_SourceThatIsTooLarge_IsNotScannedAgainAndStaysNotAvailableUntilTheNextStart()
    {
        await using var harness = new RegistryHarness { Limits = ThreeFiles };
        string big = harness.Folder("big", "1.md", "2.md", "3.md", "4.md");
        harness.WriteSourcesFile($$"""{ "sources": [ { "path": {{Json(big)}} } ] }""");
        await harness.StartAsync();
        SourceEntry before = Entry(harness, "big");

        await harness.Registry.ReloadAsync();
        await harness.Registry.ReloadAsync();

        Entry(harness, "big").ShouldBeSameAs(before);
        Warnings(harness, "is not available").ShouldBe(1);

        // Making the folder small does not start the source on its own: a scan up to the limit at every change of the list is what this saves.
        File.Delete(Path.Join(big, "4.md"));
        await harness.Registry.ReloadAsync();

        Entry(harness, "big").ShouldBeSameAs(before);

        await harness.RestartAsync();

        Entry(harness, "big").IsAvailable.ShouldBeTrue();
        Entry(harness, "big").Index!.Current.Files.Count.ShouldBe(3);
    }

    [Fact]
    public async Task ReloadAsync_SourceWhoseMissingFolderAppearsTooLarge_IsNotAvailableWithTheReasonAndNotTriedAgain()
    {
        await using var harness = new RegistryHarness { Limits = ThreeFiles };
        harness.WriteSourcesFile($$"""{ "sources": [ { "id": "later", "path": {{Json(harness.Missing("later"))}} } ] }""");
        await harness.StartAsync();
        Entry(harness, "later").Error.ShouldBe("The folder does not exist or is not a directory.");
        Entry(harness, "later").ErrorCode.ShouldBe(SourceErrorCode.FolderMissing);

        harness.Folder("later", "1.md", "2.md", "3.md", "4.md");
        await harness.Registry.ReloadAsync();
        SourceEntry tooLarge = Entry(harness, "later");
        await harness.Registry.ReloadAsync();
        await harness.Registry.ReloadAsync();

        tooLarge.IsAvailable.ShouldBeFalse();
        tooLarge.Error.ShouldBe(TooManyFiles);
        tooLarge.ErrorCode.ShouldBe(SourceErrorCode.TooLarge);
        Entry(harness, "later").ShouldBeSameAs(tooLarge);
        Warnings(harness, "is not available").ShouldBe(2);
    }

    [Fact]
    public async Task AddAsync_FolderThatIsTooLarge_IsAddedNotAvailableWithTheReasonAndCanBeRemoved()
    {
        await using var harness = new RegistryHarness { Limits = ThreeFiles };
        await harness.StartAsync();
        string big = harness.Folder("big", "1.md", "2.md", "3.md", "4.md");

        SourceEditResult added = await harness.AddAsync(big);

        added.Error.ShouldBeNull();
        SourceEntry entry = added.Entry.ShouldNotBeNull();
        entry.Definition.Id.ShouldBe("big");
        entry.IsAvailable.ShouldBeFalse();
        entry.Error.ShouldBe(TooManyFiles);
        entry.ErrorCode.ShouldBe(SourceErrorCode.TooLarge);
        harness.Ids.ShouldBe(["claude", "big"]);
        Fields(harness.ReadItems()[1]).ShouldBe(["id=big", "name=big", $"path={big}", "profile=auto"]);

        SourceEditResult removed = await harness.RemoveAsync("big");

        removed.Error.ShouldBeNull();
        harness.Ids.ShouldBe(["claude"]);
    }

    [Fact]
    public async Task StartAsync_FolderNamedOutsideASourcesFileThatIsTooLarge_Fails()
    {
        await using var harness = new RegistryHarness { Limits = ThreeFiles };
        string big = harness.Folder("big", "1.md", "2.md", "3.md", "4.md");
        harness.Roots = [big];

        FolderTooLargeException exception = await Should.ThrowAsync<FolderTooLargeException>(harness.StartAsync);

        exception.Folder.ShouldBe(big);
        exception.Message.ShouldBe(TooManyFiles);
    }

    // A path that cannot be a path (a null character, written as a JSON escape) makes the file one that cannot be used:
    // the reason on one line, never an exception, whether it is read when the server starts, when it changes, or for an edit.
    private const string UnusablePath = """{ "sources": [ { "path": "/a/x\u0000y" } ] }""";
    private const string UnusablePathReason = "source 1: \"path\" has a character that no path has";

    [Fact]
    public async Task StartAsync_SourcesFileWithAPathThatCannotBeAPath_FailsWithOneLine()
    {
        await using var harness = new RegistryHarness();
        harness.WriteSourcesFile(UnusablePath);

        InvalidOperationException exception = await Should.ThrowAsync<InvalidOperationException>(harness.StartAsync);

        exception.Message.ShouldBe($"pusula: sources file {harness.SourcesFile}: {UnusablePathReason}");
    }

    [Fact]
    public async Task ReloadAsync_FileChangedToAPathThatCannotBeAPath_KeepsTheLastListAndSaysWhy()
    {
        await using var harness = new RegistryHarness();
        string a = harness.Folder("a", "A.md");
        string good = $$"""{ "sources": [ { "id": "a", "path": {{Json(a)}} } ] }""";
        harness.WriteSourcesFile(good);
        await harness.StartAsync();
        SourceEntry before = Entry(harness, "a");

        harness.WriteSourcesFile(UnusablePath);
        await harness.Registry.ReloadSafelyAsync();

        harness.Registry.SourcesFileError.ShouldBe(UnusablePathReason);
        Entry(harness, "a").ShouldBeSameAs(before);
        harness.Log.Entries.ShouldNotContain(entry => entry.Level >= LogLevel.Error);

        harness.WriteSourcesFile(good);
        await harness.Registry.ReloadSafelyAsync();

        harness.Registry.SourcesFileError.ShouldBeNull();
    }

    [Fact]
    public async Task EditingAFileWithAPathThatCannotBeAPath_FailsWithFileInvalidAndLeavesItAlone()
    {
        await using var harness = new RegistryHarness();
        harness.WriteSourcesFile($$"""{ "sources": [ { "id": "a", "path": {{Json(harness.Folder("a", "A.md"))}} } ] }""");
        await harness.StartAsync();
        harness.WriteSourcesFile(UnusablePath);
        byte[] before = File.ReadAllBytes(harness.SourcesFile);

        SourceEditResult added = await harness.AddAsync(harness.Folder("notes", "N.md"));
        SourceEditResult removed = await harness.RemoveAsync("a");

        added.Error.ShouldBe(EditError.FileInvalid);
        added.Reason.ShouldBe(UnusablePathReason);
        removed.Error.ShouldBe(EditError.FileInvalid);
        removed.Reason.ShouldBe(UnusablePathReason);
        File.ReadAllBytes(harness.SourcesFile).ShouldBe(before);
        harness.Ids.ShouldBe(["a"]);
    }

    // The reader takes the last of two properties with the same name; writing the file out again would drop the other
    // one without anyone having said so, so such a file is left alone.
    [Theory]
    [InlineData("""{ "sources": [ { "path": "/a/one" } ], "other": 1, "other": 2 }""")]
    [InlineData("""{ "sources": [ { "path": "/a/one" } ], "sources": [ { "path": "/a/two" } ] }""")]
    public async Task EditingAFileWithAPropertyThatIsThereTwice_FailsAndLeavesItAlone(string text)
    {
        await using var harness = new RegistryHarness();
        harness.WriteSourcesFile(text);
        await harness.StartAsync();
        harness.Ids.Length.ShouldBe(1);
        byte[] before = File.ReadAllBytes(harness.SourcesFile);

        SourceEditResult added = await harness.AddAsync(harness.Folder("notes", "N.md"));
        SourceEditResult removed = await harness.RemoveAsync(harness.Ids[0]);

        added.Error.ShouldBe(EditError.FileInvalid);
        added.Reason.ShouldBe("a property of the file is there more than once");
        removed.Error.ShouldBe(EditError.FileInvalid);
        File.ReadAllBytes(harness.SourcesFile).ShouldBe(before);
    }

    // ---- Removing -----------------------------------------------------------------------------------------------

    [Fact]
    public async Task RemoveAsync_Source_TakesItOutOfTheFileAndTheListStopsItAndEndsItsEventStream()
    {
        await using var harness = new RegistryHarness();
        string first = harness.Folder("first", "F.md");
        string notes = harness.Folder("notes", "N.md");
        string last = harness.Folder("last", "L.md");

        // The source that goes is in the middle, and the items around it are written in different ways.
        harness.WriteSourcesFile(
            $$"""
            {
              "sources": [
                { "path": {{Json(first)}}, "profile": "vault" },
                { "id": "notes", "path": {{Json(notes)}} },
                { "path": {{Json(last)}} },
              ],
            }
            """);
        await harness.StartAsync();
        harness.Ids.ShouldBe(["first", "notes", "last"]);
        SourceEntry firstBefore = Entry(harness, "first");
        SourceEntry lastBefore = Entry(harness, "last");
        Task streamEnded = RegistryHarness.StreamEndsAsync(Entry(harness, "notes"));
        Task keptStreamEnded = RegistryHarness.StreamEndsAsync(firstBefore);

        SourceEditResult result = await harness.RemoveAsync("notes");

        result.Error.ShouldBeNull();
        result.Entry.ShouldBeNull();
        harness.Ids.ShouldBe(["first", "last"]);
        harness.Registry.TryGet("notes", out _).ShouldBeFalse();
        Entry(harness, "first").Index.ShouldBeSameAs(firstBefore.Index);
        Entry(harness, "last").Index.ShouldBeSameAs(lastBefore.Index);
        JsonElement[] items = harness.ReadItems();
        items.Length.ShouldBe(2);
        Fields(items[0]).ShouldBe([$"path={first}", "profile=vault"]);
        Fields(items[1]).ShouldBe([$"path={last}"]);
        await streamEnded.WaitAsync(RegistryHarness.Timeout, TestContext.Current.CancellationToken);
        keptStreamEnded.IsCompleted.ShouldBeFalse();
        Directory.Exists(notes).ShouldBeTrue();
        File.Exists(Path.Join(notes, "N.md")).ShouldBeTrue();
    }

    [Fact]
    public async Task RemoveAsync_UnknownId_IsNotFoundAndChangesNothing()
    {
        await using var harness = new RegistryHarness();
        harness.WriteSourcesFile($$"""{ "sources": [ { "path": {{Json(harness.Folder("a", "A.md"))}} } ] }""");
        await harness.StartAsync();
        byte[] before = File.ReadAllBytes(harness.SourcesFile);

        foreach (string id in new[] { "nope", "A", "", "../a", "a/../a" })
        {
            SourceEditResult result = await harness.RemoveAsync(id);

            result.Error.ShouldBe(EditError.NotFound, id);
        }

        File.ReadAllBytes(harness.SourcesFile).ShouldBe(before);
        harness.Ids.ShouldBe(["a"]);
    }

    [Fact]
    public async Task RemoveAsync_SourceThatIsNotAvailable_IsRemovedLikeAnyOther()
    {
        await using var harness = new RegistryHarness();
        harness.WriteSourcesFile(
            $$"""{ "sources": [ { "id": "gone", "path": {{Json(harness.Missing("gone"))}} }, { "path": {{Json(harness.Folder("a", "A.md"))}} } ] }""");
        await harness.StartAsync();
        Entry(harness, "gone").IsAvailable.ShouldBeFalse();

        SourceEditResult result = await harness.RemoveAsync("gone");

        result.Error.ShouldBeNull();
        harness.Ids.ShouldBe(["a"]);
        harness.ReadItems().Length.ShouldBe(1);
    }

    [Fact]
    public async Task RemoveAsync_FileThatCannotBeUsed_FailsAndLeavesItAlone()
    {
        await using var harness = new RegistryHarness();
        harness.WriteSourcesFile($$"""{ "sources": [ { "path": {{Json(harness.Folder("a", "A.md"))}} } ] }""");
        await harness.StartAsync();
        harness.WriteSourcesFile("{ nope");

        SourceEditResult result = await harness.RemoveAsync("a");

        result.Error.ShouldBe(EditError.FileInvalid);
        harness.ReadSourcesFile().ShouldBe("{ nope");
        harness.Ids.ShouldBe(["a"]);
    }

    [Fact]
    public async Task RemoveAsync_FileThatCannotBeWritten_FailsLeavesNothingBehindAndChangesNothing()
    {
        await using var harness = new RegistryHarness();

        // A folder where the sources file should be: nothing can take its place.
        Directory.CreateDirectory(harness.SourcesFile);
        await harness.StartAsync();
        SourceEntry claude = Entry(harness, "claude");

        SourceEditResult result = await harness.RemoveAsync("claude");

        result.Error.ShouldBe(EditError.WriteFailed);
        Entry(harness, "claude").ShouldBeSameAs(claude);
        Directory.GetFileSystemEntries(Path.GetDirectoryName(harness.SourcesFile)!).ShouldBe([harness.SourcesFile]);
    }

    [Fact]
    public async Task RemoveAsync_NoSourcesFileYet_WritesTheListWithoutItAndTheNextStartHasNoSources()
    {
        await using var harness = new RegistryHarness();
        await harness.StartAsync();

        SourceEditResult result = await harness.RemoveAsync("claude");

        result.Error.ShouldBeNull();
        harness.Ids.ShouldBeEmpty();
        harness.ReadItems().ShouldBeEmpty();
        await harness.RestartAsync();
        harness.Ids.ShouldBeEmpty();
    }

    [Fact]
    public async Task RemoveAsync_EverySource_LeavesAnEmptyListThatTheNextStartTakes()
    {
        await using var harness = new RegistryHarness();
        harness.WriteSourcesFile(
            $$"""{ "sources": [ { "path": {{Json(harness.Folder("a", "A.md"))}} }, { "path": {{Json(harness.Folder("b", "B.md"))}} } ] }""");
        await harness.StartAsync();

        (await harness.RemoveAsync("a")).Error.ShouldBeNull();
        (await harness.RemoveAsync("b")).Error.ShouldBeNull();

        harness.Ids.ShouldBeEmpty();
        harness.Registry.Sources.ShouldBeEmpty();
        harness.ReadItems().ShouldBeEmpty();
        await harness.RestartAsync();
        harness.Ids.ShouldBeEmpty();
        harness.Registry.SourcesFile.ShouldBe(harness.SourcesFile);

        // And a source can be added to the empty list again.
        (await harness.AddAsync(harness.Folder("c", "C.md"))).Error.ShouldBeNull();
        harness.Ids.ShouldBe(["c"]);
    }

    // ---- The file changes ---------------------------------------------------------------------------------------

    [Fact]
    public async Task ReloadAsync_FileChangedByHand_AddsRemovesAndReordersAndTheSourcesThatStayKeepTheirHost()
    {
        await using var harness = new RegistryHarness();
        string a = harness.Folder("a", "A.md");
        string b = harness.Folder("b", "B.md", "B2.md");
        string c = harness.Folder("c", "C.md");
        harness.WriteSourcesFile($$"""{ "sources": [ { "id": "a", "path": {{Json(a)}} }, { "id": "b", "path": {{Json(b)}} } ] }""");
        await harness.StartAsync();
        SourceEntry aBefore = Entry(harness, "a");
        SourceEntry bBefore = Entry(harness, "b");

        harness.WriteSourcesFile(
            $$"""
            {
              "sources": [
                { "id": "b", "path": {{Json(b)}} },
                { "id": "c", "name": "The C", "path": {{Json(c)}} },
                { "id": "a", "name": "A renamed", "path": {{Json(a)}} },
              ]
            }
            """);
        await harness.Registry.ReloadAsync();

        harness.Ids.ShouldBe(["b", "c", "a"]);
        Entry(harness, "b").Index.ShouldBeSameAs(bBefore.Index);
        Entry(harness, "a").Index.ShouldBeSameAs(aBefore.Index);
        Entry(harness, "a").Definition.Name.ShouldBe("A renamed");
        Entry(harness, "c").IsAvailable.ShouldBeTrue();
        Entry(harness, "c").Definition.Name.ShouldBe("The C");
        Entry(harness, "c").Index!.Current.Files.Count.ShouldBe(1);

        Task bEnded = RegistryHarness.StreamEndsAsync(Entry(harness, "b"));
        Task aEnded = RegistryHarness.StreamEndsAsync(Entry(harness, "a"));
        harness.WriteSourcesFile($$"""{ "sources": [ { "id": "c", "path": {{Json(c)}} } ] }""");
        await harness.Registry.ReloadAsync();

        harness.Ids.ShouldBe(["c"]);
        await Task.WhenAll(aEnded, bEnded).WaitAsync(RegistryHarness.Timeout, TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task ReloadAsync_FolderOrProfileOfASourceChanged_StartsThatSourceAgainAndStopsTheOldHost()
    {
        await using var harness = new RegistryHarness();
        string a = harness.Folder("a", "A.md");
        string moved = harness.Folder("moved", "M1.md", "M2.md");
        string untouched = harness.Folder("untouched", "U.md");
        harness.WriteSourcesFile(
            $$"""{ "sources": [ { "id": "a", "path": {{Json(a)}} }, { "id": "u", "path": {{Json(untouched)}}, "profile": "vault" } ] }""");
        await harness.StartAsync();
        SourceEntry aBefore = Entry(harness, "a");
        SourceEntry uBefore = Entry(harness, "u");
        Task aEnded = RegistryHarness.StreamEndsAsync(aBefore);
        Task uEnded = RegistryHarness.StreamEndsAsync(uBefore);

        harness.WriteSourcesFile(
            $$"""{ "sources": [ { "id": "a", "path": {{Json(moved)}} }, { "id": "u", "path": {{Json(untouched)}}, "profile": "markdown" } ] }""");
        await harness.Registry.ReloadAsync();

        Entry(harness, "a").Index.ShouldNotBeSameAs(aBefore.Index);
        Entry(harness, "a").Definition.Path.ShouldBe(moved);
        Entry(harness, "a").Index!.Current.Files.Count.ShouldBe(2);
        Entry(harness, "u").Index.ShouldNotBeSameAs(uBefore.Index);
        Entry(harness, "u").Definition.Profile.ShouldBe(SourceProfile.Markdown);
        await Task.WhenAll(aEnded, uEnded).WaitAsync(RegistryHarness.Timeout, TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task ReloadAsync_NothingChanged_ChangesNothing()
    {
        await using var harness = new RegistryHarness();
        harness.WriteSourcesFile($$"""{ "sources": [ { "path": {{Json(harness.Folder("a", "A.md"))}} }, { "path": {{Json(harness.Missing("m"))}} } ] }""");
        await harness.StartAsync();
        SourceEntry[] before = [.. harness.Registry.Sources];
        int logged = harness.Log.Entries.Count;

        await harness.Registry.ReloadAsync();
        await harness.Registry.ReloadAsync();

        harness.Registry.Sources.ShouldBe(before);
        harness.Registry.Sources.Select((entry, index) => ReferenceEquals(entry, before[index])).ShouldAllBe(same => same);
        harness.Log.Entries.Count.ShouldBe(logged);
    }

    [Fact]
    public async Task ReloadAsync_FileThatCannotBeUsed_KeepsTheLastListAndSaysWhyUntilItIsFixed()
    {
        await using var harness = new RegistryHarness();
        string a = harness.Folder("a", "A.md");
        string good = $$"""{ "sources": [ { "id": "a", "path": {{Json(a)}} } ] }""";
        harness.WriteSourcesFile(good);
        await harness.StartAsync();
        SourceEntry before = Entry(harness, "a");
        harness.Registry.SourcesFileError.ShouldBeNull();

        harness.WriteSourcesFile("""{ "sources": [ { "path": """);
        await harness.Registry.ReloadAsync();
        await harness.Registry.ReloadAsync();

        harness.Registry.SourcesFileError.ShouldNotBeNull().ShouldStartWith("invalid JSON: ");
        harness.Registry.SourcesFileError.ShouldNotContain('\n');
        Entry(harness, "a").ShouldBeSameAs(before);
        harness.Ids.ShouldBe(["a"]);
        Warnings(harness, "cannot be used").ShouldBe(1);

        // Another way of being wrong is another reason, and is told again.
        harness.WriteSourcesFile("""{ "sources": [ { "name": "x" } ] }""");
        await harness.Registry.ReloadAsync();

        harness.Registry.SourcesFileError.ShouldBe("source 1: \"path\" is required");
        Warnings(harness, "cannot be used").ShouldBe(2);

        harness.WriteSourcesFile(good);
        await harness.Registry.ReloadAsync();

        harness.Registry.SourcesFileError.ShouldBeNull();
        Entry(harness, "a").ShouldBeSameAs(before);
        harness.Log.Entries.Count(entry => entry.Level == LogLevel.Information && entry.Message.Contains("can be used again", StringComparison.Ordinal)).ShouldBe(1);
    }

    [Fact]
    public async Task ReloadAsync_FileDeleted_KeepsTheListAsItIsAndHasNoError()
    {
        await using var harness = new RegistryHarness();
        harness.WriteSourcesFile($$"""{ "sources": [ { "path": {{Json(harness.Folder("a", "A.md"))}} } ] }""");
        await harness.StartAsync();
        SourceEntry before = Entry(harness, "a");

        File.Delete(harness.SourcesFile);
        await harness.Registry.ReloadAsync();

        Entry(harness, "a").ShouldBeSameAs(before);
        harness.Registry.SourcesFileError.ShouldBeNull();
        harness.Registry.SourcesFile.ShouldBe(harness.SourcesFile);

        // A source can still be added: the file is written again, with the list as it is shown.
        (await harness.AddAsync(harness.Folder("b", "B.md"))).Error.ShouldBeNull();
        harness.Ids.ShouldBe(["a", "b"]);
        harness.ReadItems().Length.ShouldBe(2);
    }

    [Fact]
    public async Task ReloadAsync_BrokenFileThenDeleted_HasNoErrorAnyMore()
    {
        await using var harness = new RegistryHarness();
        harness.WriteSourcesFile($$"""{ "sources": [ { "path": {{Json(harness.Folder("a", "A.md"))}} } ] }""");
        await harness.StartAsync();
        harness.WriteSourcesFile("{ nope");
        await harness.Registry.ReloadAsync();
        harness.Registry.SourcesFileError.ShouldNotBeNull();

        File.Delete(harness.SourcesFile);
        await harness.Registry.ReloadAsync();

        harness.Registry.SourcesFileError.ShouldBeNull();
        harness.Ids.ShouldBe(["a"]);
    }

    [Fact]
    public async Task ReloadAsync_SourceWhoseFolderAppears_IsStartedAndOneWhoseFolderIsStillMissingIsNotLoggedAgain()
    {
        await using var harness = new RegistryHarness();
        harness.WriteSourcesFile(
            $$"""{ "sources": [ { "id": "later", "path": {{Json(harness.Missing("later"))}} }, { "id": "never", "path": {{Json(harness.Missing("never"))}} } ] }""");
        await harness.StartAsync();
        harness.Registry.Sources.ShouldAllBe(entry => !entry.IsAvailable);
        Warnings(harness, "is not available").ShouldBe(2);

        await harness.Registry.ReloadAsync();
        await harness.Registry.ReloadAsync();

        Warnings(harness, "is not available").ShouldBe(2);

        harness.Folder("later", "L.md");
        await harness.Registry.ReloadAsync();

        Entry(harness, "later").IsAvailable.ShouldBeTrue();
        Entry(harness, "later").ErrorCode.ShouldBeNull();
        Entry(harness, "later").Index!.Current.Files.Count.ShouldBe(1);
        Entry(harness, "never").IsAvailable.ShouldBeFalse();
        Entry(harness, "never").ErrorCode.ShouldBe(SourceErrorCode.FolderMissing);
        Warnings(harness, "is not available").ShouldBe(2);
    }

    // What a folder holds cannot make its scan fail (a file that cannot be read is skipped), so the scan is made to fail
    // here with an expression that does not work: the sentence says nothing about why, and the exception goes to the log.
    [Fact]
    public async Task StartAsync_FolderWhoseScanFails_IsNotAvailableAsNotReadableAndTheOthersWork()
    {
        await using var harness = new RegistryHarness { LinkPatterns = LinkExtractor.PatternSet.Default with { InlineCode = null! } };
        string broken = harness.Folder("broken", "A.md");
        string empty = harness.Folder("empty");
        harness.WriteSourcesFile($$"""{ "sources": [ { "path": {{Json(broken)}} }, { "path": {{Json(empty)}} } ] }""");

        await harness.StartAsync();

        SourceEntry entry = Entry(harness, "broken");
        entry.IsAvailable.ShouldBeFalse();
        entry.Index.ShouldBeNull();
        entry.Error.ShouldBe("The folder could not be read.");
        entry.ErrorCode.ShouldBe(SourceErrorCode.NotReadable);
        Entry(harness, "empty").IsAvailable.ShouldBeTrue();
        Entry(harness, "empty").ErrorCode.ShouldBeNull();
        Warnings(harness, "could not be started").ShouldBe(1);
        Warnings(harness, "is not available").ShouldBe(1);

        // It is not scanned again at every change of the list: only a folder that was missing is tried again.
        await harness.Registry.ReloadAsync();

        Entry(harness, "broken").ShouldBeSameAs(entry);
        Warnings(harness, "could not be started").ShouldBe(1);
    }

    // Both of these are "the folder is not there": the path does not exist, or it is a file and not a folder.
    [Fact]
    public async Task StartAsync_FolderThatDoesNotExistOrIsAFile_IsNotAvailableWithTheCodeFolderMissing()
    {
        await using var harness = new RegistryHarness();
        string file = harness.Folder("holder", "File.md") + Path.DirectorySeparatorChar + "File.md";
        harness.WriteSourcesFile(
            $$"""{ "sources": [ { "id": "gone", "path": {{Json(harness.Missing("gone"))}} }, { "id": "file", "path": {{Json(file)}} } ] }""");

        await harness.StartAsync();

        foreach (string id in new[] { "gone", "file" })
        {
            SourceEntry entry = Entry(harness, id);
            entry.IsAvailable.ShouldBeFalse(id);
            entry.Error.ShouldBe("The folder does not exist or is not a directory.", id);
            entry.ErrorCode.ShouldBe(SourceErrorCode.FolderMissing, id);
        }
    }

    // An application whose start failed is disposed, and the host may stop it after that.
    [Fact]
    public async Task StopAsync_AfterTheRegistryWasDisposed_IsHarmless()
    {
        await using var harness = new RegistryHarness();
        harness.WriteSourcesFile($$"""{ "sources": [ { "id": "a", "path": {{Json(harness.Folder("a", "A.md"))}} } ] }""");
        await harness.StartAsync();
        SourceRegistry registry = harness.Registry;

        harness.DisposeRegistry();

        await registry.StopAsync(TestContext.Current.CancellationToken);
        await registry.StopAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task ReloadSafelyAsync_AfterTheRegistryWasDisposed_IsSilent()
    {
        await using var harness = new RegistryHarness();
        harness.WriteSourcesFile($$"""{ "sources": [ { "id": "a", "path": {{Json(harness.Folder("a", "A.md"))}} } ] }""");
        await harness.StartAsync();
        SourceRegistry registry = harness.Registry;

        harness.DisposeRegistry();
        await registry.ReloadSafelyAsync();

        harness.Log.Entries.ShouldNotContain(entry => entry.Level >= LogLevel.Error);
    }

    [Fact]
    public async Task ReloadSafelyAsync_WhenSomethingUnexpectedFails_LogsItKeepsTheSourcesAndTheNextChangeTriesAgain()
    {
        await using var harness = new RegistryHarness();
        var loggers = new FailingLoggerFactory();
        harness.LoggerFactory = loggers;
        string a = harness.Folder("a", "A.md");
        string b = harness.Folder("b", "B.md");
        harness.WriteSourcesFile($$"""{ "sources": [ { "id": "a", "path": {{Json(a)}} } ] }""");
        await harness.StartAsync();
        SourceEntry before = Entry(harness, "a");

        // A source that is new needs a logger, which is what fails here.
        loggers.Armed = true;
        harness.WriteSourcesFile($$"""{ "sources": [ { "id": "a", "path": {{Json(a)}} }, { "id": "b", "path": {{Json(b)}} } ] }""");
        await harness.Registry.ReloadSafelyAsync();

        harness.Ids.ShouldBe(["a"]);
        Entry(harness, "a").ShouldBeSameAs(before);
        harness.Log.Entries.ShouldContain(entry => entry.Level == LogLevel.Error && entry.Message.Contains("Reading the sources file", StringComparison.Ordinal));

        loggers.Armed = false;
        await harness.Registry.ReloadSafelyAsync();

        harness.Ids.ShouldBe(["a", "b"]);
    }

    // ---- The file watcher ---------------------------------------------------------------------------------------

    [Fact]
    public async Task Watcher_FileWrittenByHand_BringsTheSourcesInLineWithoutAnyCall()
    {
        await using var harness = new RegistryHarness();
        string a = harness.Folder("a", "A.md");
        string b = harness.Folder("b", "B.md");
        harness.WriteSourcesFile($$"""{ "sources": [ { "id": "a", "path": {{Json(a)}} } ] }""");
        await harness.StartAsync();
        SourceEntry aBefore = Entry(harness, "a");

        harness.WriteSourcesFile($$"""{ "sources": [ { "id": "a", "path": {{Json(a)}} }, { "id": "b", "path": {{Json(b)}} } ] }""");
        await RegistryHarness.WaitUntilAsync(() => harness.Registry.Sources.Count == 2, "the new source to be listed");

        harness.Ids.ShouldBe(["a", "b"]);
        Entry(harness, "a").Index.ShouldBeSameAs(aBefore.Index);
        Entry(harness, "b").IsAvailable.ShouldBeTrue();

        Task bEnded = RegistryHarness.StreamEndsAsync(Entry(harness, "b"));
        harness.WriteSourcesFile($$"""{ "sources": [ { "id": "a", "path": {{Json(a)}} } ] }""");
        await RegistryHarness.WaitUntilAsync(() => harness.Registry.Sources.Count == 1, "the source to be gone");
        await bEnded.WaitAsync(RegistryHarness.Timeout, TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task Watcher_FileReplacedTheWayEditorsSaveIt_IsNoticed()
    {
        await using var harness = new RegistryHarness();
        string a = harness.Folder("a", "A.md");
        string b = harness.Folder("b", "B.md");
        harness.WriteSourcesFile($$"""{ "sources": [ { "id": "a", "path": {{Json(a)}} } ] }""");
        await harness.StartAsync();

        harness.ReplaceSourcesFile($$"""{ "sources": [ { "id": "b", "path": {{Json(b)}} }, { "id": "a", "path": {{Json(a)}} } ] }""");

        await RegistryHarness.WaitUntilAsync(() => harness.Ids.SequenceEqual(["b", "a"]), "the replaced file to be read");
    }

    [Fact]
    public async Task Watcher_FileCreatedWhereThereWasNone_IsTheListFromThenOn()
    {
        await using var harness = new RegistryHarness();
        string notes = harness.Folder("notes", "N.md");
        Directory.CreateDirectory(Path.GetDirectoryName(harness.SourcesFile)!);
        await harness.StartAsync();
        harness.Ids.ShouldBe(["claude"]);
        Task claudeEnded = RegistryHarness.StreamEndsAsync(Entry(harness, "claude"));

        harness.WriteSourcesFile($$"""{ "sources": [ { "path": {{Json(notes)}} } ] }""");

        await RegistryHarness.WaitUntilAsync(() => harness.Ids.SequenceEqual(["notes"]), "the new file to be the list");
        await claudeEnded.WaitAsync(RegistryHarness.Timeout, TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task Watcher_FolderOfTheFileCreatedByTheFirstWrite_IsWatchedFromThen()
    {
        await using var harness = new RegistryHarness();
        await harness.StartAsync();
        (await harness.AddAsync(harness.Folder("notes", "N.md"))).Error.ShouldBeNull();
        string other = harness.Folder("other", "O.md");

        harness.WriteSourcesFile(harness.ReadSourcesFile().Replace("\"sources\": [", $"\"sources\": [ {{ \"id\": \"other\", \"path\": {Json(other)} }},", StringComparison.Ordinal));

        await RegistryHarness.WaitUntilAsync(() => harness.Ids.Contains("other"), "the hand edit to be noticed");
        harness.Ids.ShouldBe(["other", "claude", "notes"]);
    }

    [Fact]
    public async Task Watcher_FileBrokenAndThenFixed_SaysSoAndTakesItBack()
    {
        await using var harness = new RegistryHarness();
        string a = harness.Folder("a", "A.md");
        string good = $$"""{ "sources": [ { "id": "a", "path": {{Json(a)}} } ] }""";
        harness.WriteSourcesFile(good);
        await harness.StartAsync();

        harness.WriteSourcesFile("""{ "sources": [ { "id": "a", "path": """);
        await RegistryHarness.WaitUntilAsync(() => harness.Registry.SourcesFileError is not null, "the broken file to be noticed");
        harness.Ids.ShouldBe(["a"]);

        harness.WriteSourcesFile(good);
        await RegistryHarness.WaitUntilAsync(() => harness.Registry.SourcesFileError is null, "the fixed file to be noticed");
        harness.Ids.ShouldBe(["a"]);
    }

    [Fact]
    public async Task Watcher_AfterTheRegistryStopped_NoticesNothingAndThrowsNothing()
    {
        await using var harness = new RegistryHarness();
        string a = harness.Folder("a", "A.md");
        harness.WriteSourcesFile($$"""{ "sources": [ { "id": "a", "path": {{Json(a)}} } ] }""");
        await harness.StartAsync();

        await harness.Registry.StopAsync(TestContext.Current.CancellationToken);
        harness.WriteSourcesFile("""{ "sources": [] }""");
        await harness.Registry.ReloadSafelyAsync();
        await Task.Delay(SourcesFileWatcher.Delay * 2, TestContext.Current.CancellationToken);

        harness.Ids.ShouldBe(["a"]);
    }
}
