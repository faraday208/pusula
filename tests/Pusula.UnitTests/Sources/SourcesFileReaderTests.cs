using System.Globalization;
using System.Text.Json;
using Pusula.Indexing;
using Pusula.Sources;
using Pusula.UnitTests.Support;
using Shouldly;
using Xunit;

namespace Pusula.UnitTests.Sources;

public sealed class SourcesFileReaderTests
{
    // The folder of the file, as a full path of this platform: "/base" has no drive in front of it on Windows and is not one.
    private static readonly string Base = Path.GetFullPath("/base");

    // A path as JSON text, with the quotes: the backslashes of a Windows path are escaped.
    private static string Json(string path) => JsonSerializer.Serialize(path);

    private static IReadOnlyList<SourceDefinition> Parse(string json, string baseDirectory, string home = "/home/test")
    {
        SourcesFileReader.TryParse(json, baseDirectory, home, out IReadOnlyList<SourceDefinition>? sources, out string? reason)
            .ShouldBeTrue(reason);
        return sources.ShouldNotBeNull();
    }

    private static string Fail(string json, string home = "/home/test")
    {
        SourcesFileReader.TryParse(json, Base, home, out IReadOnlyList<SourceDefinition>? sources, out string? reason).ShouldBeFalse();
        sources.ShouldBeNull();
        return reason.ShouldNotBeNull();
    }

    [Fact]
    public void TryParse_ItemWithEveryField_IsTakenAsWritten()
    {
        using var temp = new TempDirectory();
        string folder = temp.CreateDirectory("anywhere");

        SourceDefinition source = Parse($$"""{ "sources": [ { "id": "my-claude", "name": "Benim Claude", "path": {{Json(folder)}}, "profile": "claude" } ] }""", temp.Path).Single();

        source.ShouldBe(new SourceDefinition("my-claude", "Benim Claude", folder, SourceProfile.Claude, IsRequired: false));
    }

    [Fact]
    public void TryParse_OnlyAPath_GetsTheNameOfTheFolderADerivedIdAndTheProfileThatTheContentSuggests()
    {
        using var temp = new TempDirectory();
        string vault = temp.CreateDirectory("Not Defteri");
        temp.CreateDirectory("Not Defteri/.obsidian");

        SourceDefinition source = Parse($$"""{ "sources": [ { "path": {{Json(vault)}} } ] }""", temp.Path).Single();

        source.Name.ShouldBe("Not Defteri");
        source.Id.ShouldBe("not-defteri");
        source.Profile.ShouldBe(SourceProfile.Vault);
        source.IsRequired.ShouldBeFalse();
    }

    [Fact]
    public void TryParse_TildeInThePath_IsTheHomeDirectory()
    {
        using var home = new TempDirectory();

        SourceDefinition source = Parse("""{ "sources": [ { "path": "~/Documents/vault" } ] }""", Base, home.Path).Single();

        source.Path.ShouldBe(Path.Join(home.Path, "Documents", "vault"));
    }

    [Fact]
    public void TryParse_RelativePath_IsRelativeToTheFolderOfTheFile()
    {
        SourceDefinition source = Parse("""{ "sources": [ { "path": "notes/work" } ] }""", Path.GetFullPath("/etc/pusula")).Single();

        source.Path.ShouldBe(Path.GetFullPath("/etc/pusula/notes/work"));
    }

    [Fact]
    public void TryParse_CommentsAndATrailingComma_AreAllowed()
    {
        const string json = """
            {
              // the folders to show
              "sources": [
                { "path": "/a/one" /* inline */ },
                { "path": "/a/two", },
              ],
            }
            """;

        Parse(json, Base).Select(source => source.Id).ShouldBe(["one", "two"]);
    }

    [Fact]
    public void TryParse_FoldersWithTheSameName_GetNumberedIdsAndAWrittenIdIsNeverTakenAway()
    {
        const string json = """
            { "sources": [
              { "path": "/a/notes" },
              { "path": "/b/notes" },
              { "path": "/c/other", "id": "notes-2" },
              { "path": "/d/notes" }
            ] }
            """;

        Parse(json, Base).Select(source => source.Id).ShouldBe(["notes", "notes-3", "notes-2", "notes-4"]);
    }

    [Theory]
    [InlineData("VAULT", SourceProfile.Vault)]
    [InlineData("Markdown", SourceProfile.Markdown)]
    [InlineData("claude", SourceProfile.Claude)]
    public void TryParse_Profile_IsReadIgnoringCase(string text, SourceProfile expected) =>
        Parse($$"""{ "sources": [ { "path": "/a/x", "profile": "{{text}}" } ] }""", Base).Single().Profile.ShouldBe(expected);

    [Fact]
    public void TryParse_ProfileAuto_IsDecidedByTheFolder()
    {
        using var temp = new TempDirectory();
        string claude = temp.CreateDirectory("a/config");
        temp.Write("a/config/CLAUDE.md", "x");
        temp.CreateDirectory("a/config/skills");

        Parse($$"""{ "sources": [ { "path": {{Json(claude)}}, "profile": "auto" }, { "path": {{Json(temp.Path)}} } ] }""", Base)
            .Select(source => source.Profile).ShouldBe([SourceProfile.Claude, SourceProfile.Markdown]);
    }

    [Fact]
    public void TryParse_FolderThatDoesNotExist_IsNotAnError()
    {
        SourceDefinition source = Parse("""{ "sources": [ { "path": "/does/not/exist/anywhere", "name": "Kayıp" } ] }""", Base).Single();

        source.Path.ShouldBe(Path.GetFullPath("/does/not/exist/anywhere"));
        source.Id.ShouldBe("kayip");
    }

    [Theory]
    [InlineData("""{ "sources": [ """, "invalid JSON: ")]
    [InlineData("not json", "invalid JSON: ")]
    [InlineData("[]", "expected an object with a \"sources\" array")]
    [InlineData("""{ "sources": {} }""", "expected an object with a \"sources\" array")]
    [InlineData("""{ "sources": [ 1 ] }""", "source 1: expected an object")]
    [InlineData("""{ "sources": [ { "name": "x" } ] }""", "source 1: \"path\" is required")]
    [InlineData("""{ "sources": [ { "path": "  " } ] }""", "source 1: \"path\" is required")]
    [InlineData("""{ "sources": [ { "path": 5 } ] }""", "source 1: \"path\" must be a string")]
    [InlineData("""{ "sources": [ { "path": "/a/x" }, { "path": "/a/y", "profile": "obsidian" } ] }""", "source 2: unknown profile \"obsidian\"")]
    [InlineData("""{ "sources": [ { "path": "/a/x", "id": "Not Valid" } ] }""", "source 1: the id \"Not Valid\" must be lowercase letters and digits")]
    [InlineData("""{ "sources": [ { "path": "/a/x", "id": "same" }, { "path": "/a/y", "id": "same" } ] }""", "source 2: the id \"same\" is used more than once")]
    public void TryParse_UnusableFile_GivesOneLineThatSaysWhat(string json, string expectedStart)
    {
        string reason = Fail(json);

        reason.ShouldStartWith(expectedStart);
        reason.ShouldNotContain('\n');
    }

    // What the file says after its last source was removed: a list with nothing in it is a list.
    [Theory]
    [InlineData("""{ "sources": [] }""")]
    [InlineData("{ // nothing to show\n  \"sources\": [ ],\n}")]
    [InlineData("""{ "sources": [], "other": 1 }""")]
    public void TryParse_EmptyList_IsAListWithNoSources(string json) =>
        Parse(json, Base).ShouldBeEmpty();

    // A character that no path has is a file that cannot be used (one line, never an exception): the null character
    // everywhere, and on Windows the others that Path.GetInvalidPathChars lists. The JSON escapes them.
    [Fact]
    public void TryParse_PathWithACharacterNoPathHas_IsAnUnusableFileAndNotAnException()
    {
        foreach (char invalid in Path.GetInvalidPathChars())
        {
            string escaped = "\\u" + ((int)invalid).ToString("x4", CultureInfo.InvariantCulture);
            foreach (string path in new[] { "/a/b" + escaped + "c", escaped + "/a", "/a" + escaped, "relative" + escaped, "~/x" + escaped, escaped })
            {
                // Nothing but white space (a tab or a line break, which are characters that no path has on Windows) is no path at all: "path" is required.
                if (path == escaped && char.IsWhiteSpace(invalid))
                {
                    continue;
                }

                string reason = Fail("{ \"sources\": [ { \"path\": \"" + path + "\" } ] }");

                reason.ShouldBe("source 1: \"path\" has a character that no path has");
            }
        }
    }

    // Whatever a hand-edited file holds, reading it is `true`, or `false` with one line: never an exception, because the
    // file is read again while the server runs, by the file watcher, and for every edit.
    [Fact]
    public void TryParse_AnyTextInAnyProperty_IsUsableOrOneLineAndNeverAnException()
    {
        string[] nasty =
        [
            "\\u0000", "a\\u0000b", "\\ud800", "x\\udc00y", "\\ud83d\\ude00", "\\u202e", "\\u0001\\u001f\\u007f", "\\ufeff", "\\u2028\\u2029",
            "..", ".", "~", "~/", "~\\\\x", "~root/x", "//", "\\\\\\\\server\\\\share", "C:", "C:\\\\x", "a:b", "*?<>|", "/a/../../..", "   ", "\\t", new string('a', 100_000),
            "/" + string.Join('/', Enumerable.Repeat("seg", 5_000)),
        ];

        foreach (string value in nasty)
        {
            foreach (string property in new[] { "path", "name", "id", "profile" })
            {
                string path = property == "path" ? value : "/a/x";
                string json = "{ \"sources\": [ { \"path\": \"" + path + "\"" + (property == "path" ? string.Empty : ", \"" + property + "\": \"" + value + "\"") + " } ] }";

                bool usable = SourcesFileReader.TryParse(json, Base, "/home/test", out IReadOnlyList<SourceDefinition>? sources, out string? reason);

                if (usable)
                {
                    sources.ShouldNotBeNull().ShouldNotBeEmpty();
                }
                else
                {
                    reason.ShouldNotBeNullOrWhiteSpace();
                    reason.ShouldNotContain('\n');
                }
            }
        }
    }

    [Fact]
    public void TryParse_PathWithANullCharacterInTheSecondItem_NamesThatItem() =>
        Fail("""{ "sources": [ { "path": "/a/one" }, { "path": "/a/two\u0000" } ] }""").ShouldBe("source 2: \"path\" has a character that no path has");

    [Theory]
    [InlineData("""{ "sources": [ { "path": "/a/x", "name": "\ud800" } ] }""", "source 1: \"name\" is not valid text")]
    [InlineData("""{ "sources": [ { "path": "\udc00" } ] }""", "source 1: \"path\" is not valid text")]
    [InlineData("""{ "sources": [ { "path": "/a/x", "id": "a\ud800" } ] }""", "source 1: \"id\" is not valid text")]
    [InlineData("""{ "sources": [ { "path": "/a/x", "profile": "\ud800" } ] }""", "source 1: \"profile\" is not valid text")]
    public void TryParse_TextThatIsNoText_IsAnUnusableFileAndNotAnException(string json, string expected) =>
        Fail(json).ShouldBe(expected);

    [Fact]
    public void TryParse_TildeWithoutAHomeDirectory_NamesTheSource() =>
        Fail("""{ "sources": [ { "path": "~/x" } ] }""", home: string.Empty).ShouldStartWith("source 1: Cannot expand '~'");

    [Fact]
    public void TryRead_ExistingFile_IsParsedRelativeToItsFolder()
    {
        using var temp = new TempDirectory();
        string file = temp.Write("config/sources.json", """{ "sources": [ { "path": "../vault" } ] }""");

        SourcesFileReader.TryRead(file, "/home/test", out IReadOnlyList<SourceDefinition>? sources, out string? reason).ShouldBeTrue(reason);

        sources.ShouldNotBeNull().Single().Path.ShouldBe(Path.Join(temp.Path, "vault"));
    }

    [Fact]
    public void TryReadText_ExistingFile_IsItsTextWithoutAnyInterpretation()
    {
        using var temp = new TempDirectory();
        string file = temp.Write("sources.json", "not even json // x\n");

        SourcesFileReader.TryReadText(file, out string? text, out string? reason).ShouldBeTrue(reason);

        text.ShouldBe("not even json // x\n");
    }

    [Fact]
    public void TryReadText_FileThatDoesNotExist_GivesAReasonOnOneLine()
    {
        using var temp = new TempDirectory();

        SourcesFileReader.TryReadText(temp.Resolve("missing.json"), out string? text, out string? reason).ShouldBeFalse();

        text.ShouldBeNull();
        reason.ShouldNotBeNullOrWhiteSpace();
        reason.ShouldNotContain('\n');
    }

    [Fact]
    public void TryRead_FileWithAPathThatCannotBeAPath_IsAnUnusableFileAndNotAnException()
    {
        using var temp = new TempDirectory();
        string file = temp.Write("sources.json", "{ \"sources\": [ { \"path\": \"/a/x\\u0000y\" } ] }");

        SourcesFileReader.TryRead(file, "/home/test", out IReadOnlyList<SourceDefinition>? sources, out string? reason).ShouldBeFalse();

        sources.ShouldBeNull();
        reason.ShouldBe("source 1: \"path\" has a character that no path has");
    }

    [Fact]
    public void TryRead_FileThatCannotBeRead_GivesAReason()
    {
        using var temp = new TempDirectory();

        // A directory where the file should be: reading it fails the way a missing permission does.
        SourcesFileReader.TryRead(temp.Path, "/home/test", out IReadOnlyList<SourceDefinition>? sources, out string? reason).ShouldBeFalse();

        sources.ShouldBeNull();
        reason.ShouldNotBeNullOrWhiteSpace();
        reason.ShouldNotContain('\n');
    }
}
