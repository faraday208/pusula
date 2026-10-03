using System.Text.Json;
using Pusula.Indexing;
using Pusula.Sources;
using Shouldly;
using Xunit;

namespace Pusula.UnitTests.Sources;

public sealed class SourcesFileEditorTests
{
    private static readonly string Home = Path.GetFullPath("/home/test");
    private static readonly string BaseDirectory = Path.GetFullPath("/base");

    private const string HandWritten = """
        {
          // my sources
          "sources": [
            { "path": "~/.claude", "profile": "auto", "future": { "a": [1, 2] } },
            { "id": "notes", "name": "Notes", "path": "/a/notes", },
            { "path": "/b/vault", "profile": "VAULT" },
          ],
          "other": 5,
        }
        """;

    private static IReadOnlyList<SourceDefinition> Read(string json)
    {
        SourcesFileReader.TryParse(json, BaseDirectory, Home, out IReadOnlyList<SourceDefinition>? sources, out string? reason).ShouldBeTrue(reason);
        return sources.ShouldNotBeNull();
    }

    private static string Add(string? text, IReadOnlyList<SourceDefinition> current, NewSource source, string id)
    {
        SourcesFileEditor.TryAdd(text, current, Home, source, id, out string? json, out string? reason).ShouldBeTrue(reason);
        return json.ShouldNotBeNull();
    }

    private static string Remove(string? text, IReadOnlyList<SourceDefinition> current, int index)
    {
        SourcesFileEditor.TryRemove(text, current, Home, index, out string? json, out string? reason).ShouldBeTrue(reason);
        return json.ShouldNotBeNull();
    }

    private static JsonElement[] Items(JsonDocument document) => [.. document.RootElement.GetProperty("sources").EnumerateArray()];

    private static NewSource Source(string path = "/c/new", string name = "New", string profile = "auto") => new(path, path, name, profile);

    // ---- Adding -------------------------------------------------------------------------------------------------

    [Fact]
    public void TryAdd_ExistingFile_KeepsTheOtherItemsAsTheyWereWrittenAndAppendsTheNewOne()
    {
        string json = Add(HandWritten, Read(HandWritten), Source("~/Documents/new", "New", "vault"), "new");
        using JsonDocument document = JsonDocument.Parse(json);
        JsonElement[] items = Items(document);

        items.Length.ShouldBe(4);
        items[0].EnumerateObject().Select(property => property.Name).ShouldBe(["path", "profile", "future"]);
        items[0].GetProperty("path").GetString().ShouldBe("~/.claude");
        items[0].GetProperty("profile").GetString().ShouldBe("auto");
        items[0].GetProperty("future").GetProperty("a").EnumerateArray().Select(number => number.GetInt32()).ShouldBe([1, 2]);
        items[1].EnumerateObject().Select(property => property.Name).ShouldBe(["id", "name", "path"]);
        items[2].EnumerateObject().Select(property => property.Name).ShouldBe(["path", "profile"]);
        items[2].GetProperty("profile").GetString().ShouldBe("VAULT");
        items[3].EnumerateObject().Select(property => $"{property.Name}={property.Value.GetString()}").ShouldBe(
            ["id=new", "name=New", "path=~/Documents/new", "profile=vault"]);
        document.RootElement.GetProperty("other").GetInt32().ShouldBe(5);
    }

    [Fact]
    public void TryAdd_ExistingFile_LosesTheCommentsAndStaysAFileTheReaderTakes()
    {
        string json = Add(HandWritten, Read(HandWritten), Source(), "new");

        json.ShouldNotContain("//");
        json.ShouldNotContain("my sources");
        json.ShouldEndWith("\n");
        Read(json).Select(source => source.Id).ShouldBe(["claude", "notes", "vault", "new"]);
    }

    [Fact]
    public void TryAdd_ExistingFile_ChangesNothingAboutTheSourcesThatWereThere()
    {
        IReadOnlyList<SourceDefinition> before = Read(HandWritten);

        IReadOnlyList<SourceDefinition> after = Read(Add(HandWritten, before, Source(), "new"));

        after.Take(before.Count).ShouldBe(before);
    }

    [Fact]
    public void TryAdd_NameWithLettersOfAnotherAlphabet_IsWrittenAsItIs()
    {
        string json = Add(HandWritten, Read(HandWritten), Source("/c/Notlarım", "Notlarım ğüşiöç <&> 'x'"), "notlarim");

        json.ShouldContain("Notlarım ğüşiöç <&> 'x'");
        json.ShouldNotContain("\\u");
        Read(json)[^1].Name.ShouldBe("Notlarım ğüşiöç <&> 'x'");
    }

    [Fact]
    public void TryAdd_PathIsWrittenAsTypedAndTheReaderResolvesItTheSameWay()
    {
        string json = Add(HandWritten, Read(HandWritten), new NewSource("~/Documents/new", Path.Join(Home, "Documents", "new"), "New", "auto"), "new");

        Read(json)[^1].Path.ShouldBe(Path.Join(Home, "Documents", "new"));
    }

    [Fact]
    public void TryAdd_NoFileYet_StartsWithTheListThatIsShownAndThenTheNewSource()
    {
        SourceDefinition shown = new("claude", ".claude", Path.Join(Home, ".claude"), SourceProfile.Claude, IsRequired: true);

        string json = Add(text: null, [shown], Source(), "new");
        using JsonDocument document = JsonDocument.Parse(json);
        JsonElement[] items = Items(document);

        items.Length.ShouldBe(2);
        items[0].EnumerateObject().Select(property => $"{property.Name}={property.Value.GetString()}").ShouldBe(
            ["id=claude", "name=.claude", "path=~/.claude", "profile=auto"]);
        items[1].GetProperty("id").GetString().ShouldBe("new");
        Read(json).Select(source => (source.Id, source.Name, source.Profile)).ShouldBe([("claude", ".claude", SourceProfile.Claude), ("new", "New", SourceProfile.Markdown)]);
    }

    [Fact]
    public void TryAdd_NoFileYet_WritesAProfileThatAutoWouldNotGiveAndKeepsPathsOutsideTheHomeDirectoryWhole()
    {
        string outside = Path.Join(Path.GetTempPath(), "pusula-no-such-folder", "vault");
        SourceDefinition shown = new("data", "Data", outside, SourceProfile.Vault);

        string json = Add(text: null, [shown], Source(), "new");
        using JsonDocument document = JsonDocument.Parse(json);

        Items(document)[0].EnumerateObject().Select(property => $"{property.Name}={property.Value.GetString()}").ShouldBe(
            ["id=data", "name=Data", $"path={outside}", "profile=vault"]);
    }

    [Fact]
    public void TryAdd_NoFileYetAndNothingShown_IsAListWithTheNewSourceOnly()
    {
        string json = Add(text: null, [], Source(), "new");

        Read(json).Select(source => source.Id).ShouldBe(["new"]);
    }

    [Theory]
    [InlineData("{ \"sources\": [ { \"path\": ", "invalid JSON: ")]
    [InlineData("[]", "expected an object with a \"sources\" array")]
    [InlineData("{}", "expected an object with a \"sources\" array")]
    [InlineData("{ \"sources\": {} }", "expected an object with a \"sources\" array")]
    [InlineData("null", "expected an object with a \"sources\" array")]
    public void TryAdd_TextThatIsNotAList_FailsWithOneLine(string text, string expectedStart)
    {
        SourcesFileEditor.TryAdd(text, [], Home, Source(), "new", out string? json, out string? reason).ShouldBeFalse();

        json.ShouldBeNull();
        reason.ShouldNotBeNull().ShouldStartWith(expectedStart);
        reason.ShouldNotContain('\n');
    }

    [Fact]
    public void TryAdd_ListThatDoesNotMatchWhatWasRead_Fails()
    {
        IReadOnlyList<SourceDefinition> notWhatTheFileSays = Read("""{ "sources": [ { "path": "/a/one" } ] }""");

        SourcesFileEditor.TryAdd(HandWritten, notWhatTheFileSays, Home, Source(), "new", out string? json, out string? reason).ShouldBeFalse();

        json.ShouldBeNull();
        reason.ShouldNotBeNull().ShouldContain("does not hold the sources that were read");
    }

    [Theory]
    [InlineData("""{ "sources": [ { "path": "/a/one" } ], "other": 1, "other": 2 }""")]
    [InlineData("""{ "sources": [ { "path": "/a/one" } ], "sources": [ { "path": "/a/two" } ] }""")]
    public void TryAdd_AndTryRemove_PropertyThatIsInTheFileTwice_FailWithOneLineAndWriteNothing(string text)
    {
        IReadOnlyList<SourceDefinition> current = Read(text);

        SourcesFileEditor.TryAdd(text, current, Home, Source(), "new", out string? added, out string? addReason).ShouldBeFalse();
        SourcesFileEditor.TryRemove(text, current, Home, 0, out string? removed, out string? removeReason).ShouldBeFalse();

        added.ShouldBeNull();
        removed.ShouldBeNull();
        addReason.ShouldBe("a property of the file is there more than once");
        removeReason.ShouldBe(addReason);
    }

    [Fact]
    public void TryAdd_ItemThatHasAPropertyTwice_IsKeptAsItWasWritten()
    {
        const string text = """{ "sources": [ { "path": "/a/one", "path": "/a/two" } ] }""";

        string json = Add(text, Read(text), Source(), "new");

        json.ShouldContain("/a/one");
        json.ShouldContain("/a/two");
        Read(json).Select(source => source.Id).ShouldBe(["two", "new"]);
    }

    [Fact]
    public void TryAdd_NoFileYetAndNoHomeDirectory_WritesFullPaths()
    {
        string folder = Path.Join(Path.GetTempPath(), "pusula-no-such-folder", "notes");
        SourceDefinition shown = new("notes", "Notes", folder, SourceProfile.Markdown);

        SourcesFileEditor.TryAdd(text: null, [shown], homeDirectory: string.Empty, Source(), "new", out string? json, out string? reason).ShouldBeTrue(reason);
        using JsonDocument document = JsonDocument.Parse(json.ShouldNotBeNull());

        Items(document)[0].GetProperty("path").GetString().ShouldBe(folder);
    }

    // ---- Removing -----------------------------------------------------------------------------------------------

    [Fact]
    public void TryRemove_OneItem_TakesOutThatItemAndKeepsTheOthersAsTheyWereWritten()
    {
        string json = Remove(HandWritten, Read(HandWritten), index: 1);
        using JsonDocument document = JsonDocument.Parse(json);
        JsonElement[] items = Items(document);

        items.Length.ShouldBe(2);
        items[0].EnumerateObject().Select(property => property.Name).ShouldBe(["path", "profile", "future"]);
        items[1].EnumerateObject().Select(property => property.Name).ShouldBe(["path", "profile"]);
        items[1].GetProperty("profile").GetString().ShouldBe("VAULT");
        document.RootElement.GetProperty("other").GetInt32().ShouldBe(5);
        Read(json).Select(source => source.Id).ShouldBe(["claude", "vault"]);
    }

    [Theory]
    [InlineData(0, new[] { "notes", "vault" })]
    [InlineData(2, new[] { "claude", "notes" })]
    public void TryRemove_FirstAndLastItem_AreTakenOutByPosition(int index, string[] remaining)
    {
        string json = Remove(HandWritten, Read(HandWritten), index);

        Read(json).Select(source => source.Id).ShouldBe(remaining);
    }

    [Fact]
    public void TryRemove_EveryItem_LeavesAnEmptyListThatTheReaderTakes()
    {
        IReadOnlyList<SourceDefinition> current = Read(HandWritten);
        string json = HandWritten;
        for (int remaining = current.Count; remaining > 0; remaining--)
        {
            json = Remove(json, Read(json), index: 0);
        }

        using JsonDocument document = JsonDocument.Parse(json);
        Items(document).ShouldBeEmpty();
        document.RootElement.GetProperty("other").GetInt32().ShouldBe(5);
        Read(json).ShouldBeEmpty();
    }

    [Fact]
    public void TryRemove_NoFileYet_StartsWithTheListThatIsShownWithoutTheSource()
    {
        SourceDefinition shown = new("claude", ".claude", Path.Join(Home, ".claude"), SourceProfile.Claude, IsRequired: true);

        string json = Remove(text: null, [shown], index: 0);

        Read(json).ShouldBeEmpty();
    }

    [Fact]
    public void TryRemove_TextThatIsNotAList_FailsWithOneLine()
    {
        SourcesFileEditor.TryRemove("{ nope", [], Home, 0, out string? json, out string? reason).ShouldBeFalse();

        json.ShouldBeNull();
        reason.ShouldNotBeNull().ShouldStartWith("invalid JSON: ");
        reason.ShouldNotContain('\n');
    }
}
