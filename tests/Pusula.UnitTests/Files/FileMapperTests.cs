using System.Text.Json;
using System.Text.Json.Nodes;
using Pusula.Files;
using Pusula.Indexing;
using Pusula.Links;
using Pusula.Startup;
using Pusula.UnitTests.Support;
using Shouldly;
using Xunit;

namespace Pusula.UnitTests.Files;

public sealed class FileMapperTests
{
    [Fact]
    public void ToResponse_MapsEveryField()
    {
        var frontmatter = new JsonObject { ["name"] = "deploy", ["tags"] = new JsonArray("a", "b") };
        ConfigFile file = IndexFactory.File(
            "skills/deploy/SKILL.md",
            Layer.Skill,
            LoadMode.DescriptionEverySession,
            new TokenCounts(120, 12, 0),
            [
                IndexFactory.Link(LinkKind.MarkdownLink, "notes.md#Top", 7, LinkStatus.Resolved, "skills/deploy/notes.md", "Top"),
                IndexFactory.Link(LinkKind.WikiLink, "ghost", 8, LinkStatus.Broken),
            ],
            isOrphan: false,
            frontmatterError: "Line 2: oops",
            frontmatterErrorLine: 2,
            frontmatter: frontmatter,
            body: "# Deploy\n",
            bodyStartLine: 6,
            content: "---\nname: deploy: oops\ntags: [a, b]\nmore: 1\n---\n# Deploy\n");
        ConfigFile notes = IndexFactory.File("skills/deploy/notes.md", Layer.SkillResource, LoadMode.OnDemand);
        ConfigIndex index = IndexFactory.Index([file, notes], version: 9);

        FileResponse response = FileMapper.ToResponse(index, file);

        response.Path.ShouldBe("skills/deploy/SKILL.md");
        response.Name.ShouldBe("SKILL.md");
        response.Layer.ShouldBe(Layer.Skill);
        response.LoadMode.ShouldBe(LoadMode.DescriptionEverySession);
        response.Tokens.ShouldBe(new FileTokens(120, 12, 0));
        response.Frontmatter.ShouldBeSameAs(frontmatter);
        response.FrontmatterError.ShouldBe("Line 2: oops");
        response.FrontmatterErrorLine.ShouldBe(2);
        response.FrontmatterErrorText.ShouldBe("name: deploy: oops");
        response.Body.ShouldBe("# Deploy\n");
        response.BodyStartLine.ShouldBe(6);
        response.Links.ShouldBe(
        [
            new FileLink(LinkKind.MarkdownLink, "notes.md#Top", 7, LinkStatus.Resolved, "skills/deploy/notes.md", "Top"),
            new FileLink(LinkKind.WikiLink, "ghost", 8, LinkStatus.Broken),
        ]);
        response.Backlinks.ShouldBeEmpty();
        response.Orphan.ShouldBeFalse();
        response.Version.ShouldBe(9);
    }

    [Fact]
    public void ToResponse_FileWithoutFrontmatterOrLinks_HasNullsAndEmptyLists()
    {
        ConfigFile file = IndexFactory.File("a.md", isOrphan: true);

        FileResponse response = FileMapper.ToResponse(IndexFactory.Index([file]), file);

        response.Frontmatter.ShouldBeNull();
        response.FrontmatterError.ShouldBeNull();
        response.FrontmatterErrorLine.ShouldBeNull();
        response.FrontmatterErrorText.ShouldBeNull();
        response.Links.ShouldBeEmpty();
        response.Backlinks.ShouldBeEmpty();
        response.Orphan.ShouldBeTrue();
    }

    [Fact]
    public void ToResponse_Backlinks_ComeFromTheIndexInItsOrder()
    {
        ConfigFile target = IndexFactory.File("rules/target.md");
        ConfigFile first = IndexFactory.File(
            "a.md",
            links:
            [
                IndexFactory.Link(LinkKind.WikiLink, "rules/target", 4, LinkStatus.Resolved, "rules/target.md"),
                IndexFactory.Link(LinkKind.RelativePath, "rules/target.md", 9, LinkStatus.Resolved, "rules/target.md"),
            ]);
        ConfigFile second = IndexFactory.File(
            "b/c.md",
            links: [IndexFactory.Link(LinkKind.ClaudePath, "~/.claude/rules/target.md", 2, LinkStatus.Resolved, "rules/target.md")]);
        ConfigIndex index = IndexFactory.Index([second, target, first]);

        FileResponse response = FileMapper.ToResponse(index, target);

        response.Backlinks.ShouldBe(
        [
            new FileBacklink("a.md", LinkKind.WikiLink, 4),
            new FileBacklink("a.md", LinkKind.RelativePath, 9),
            new FileBacklink("b/c.md", LinkKind.ClaudePath, 2),
        ]);
    }

    // ---- Backlink excerpts ------------------------------------------------------------------------------------

    private static Link Resolved(LinkKind kind, string raw, int line, string target) =>
        IndexFactory.Link(kind, raw, line, LinkStatus.Resolved, target);

    private static FileResponse MapTarget(ConfigIndex index, string targetPath = "t.md") =>
        FileMapper.ToResponse(index, index.Files[targetPath]);

    [Fact]
    public void ToResponse_Backlinks_CarryTheTextOfTheLineThatHoldsTheLink()
    {
        ConfigFile target = IndexFactory.File("rules/target.md");

        // Three lines of frontmatter come first: link lines are counted from the top of the file.
        ConfigFile source = IndexFactory.File(
            "a.md",
            links:
            [
                Resolved(LinkKind.WikiLink, "rules/target", 5, "rules/target.md"),
                Resolved(LinkKind.MarkdownLink, "rules/target.md", 7, "rules/target.md"),
            ],
            content: "---\nname: a\n---\n\n  See [[rules/target]] for **details**.  \nunrelated\n- [the target](rules/target.md)\n");

        FileResponse response = FileMapper.ToResponse(IndexFactory.Index([source, target]), target);

        response.Backlinks.ShouldBe(
        [
            new FileBacklink("a.md", LinkKind.WikiLink, 5, "See [[rules/target]] for **details**."),
            new FileBacklink("a.md", LinkKind.MarkdownLink, 7, "- [the target](rules/target.md)"),
        ]);
    }

    [Fact]
    public void ToResponse_Backlinks_TakeTheLineFromTheContentNotFromTheBody()
    {
        ConfigFile target = IndexFactory.File("t.md");
        ConfigFile source = IndexFactory.File(
            "a.md",
            links: [Resolved(LinkKind.WikiLink, "t", 4, "t.md")],
            body: "Body line [[t]]\n",
            bodyStartLine: 4,
            content: "---\nname: a\n---\nBody line [[t]]\n");

        FileMapper.ToResponse(IndexFactory.Index([source, target]), target).Backlinks.ShouldBe(
            [new FileBacklink("a.md", LinkKind.WikiLink, 4, "Body line [[t]]")]);
    }

    [Fact]
    public void ToResponse_Backlinks_FromSeveralSources_EachReadsItsOwnSource()
    {
        ConfigFile target = IndexFactory.File("t.md");
        ConfigFile a = IndexFactory.File(
            "a.md",
            links: [Resolved(LinkKind.WikiLink, "t", 1, "t.md"), Resolved(LinkKind.RelativePath, "t.md", 3, "t.md")],
            content: "a one [[t]]\na two\na three `t.md`");
        ConfigFile b = IndexFactory.File(
            "b/c.md",
            links: [Resolved(LinkKind.ClaudePath, "~/.claude/t.md", 2, "t.md")],
            content: "b one\nb two ~/.claude/t.md\n");

        FileResponse response = FileMapper.ToResponse(IndexFactory.Index([b, target, a]), target);

        response.Backlinks.ShouldBe(
        [
            new FileBacklink("a.md", LinkKind.WikiLink, 1, "a one [[t]]"),
            new FileBacklink("a.md", LinkKind.RelativePath, 3, "a three `t.md`"),
            new FileBacklink("b/c.md", LinkKind.ClaudePath, 2, "b two ~/.claude/t.md"),
        ]);
    }

    [Fact]
    public void ToResponse_Backlinks_AreReadRightWhateverOrderTheirSourcesAndLinesComeIn()
    {
        ConfigFile target = IndexFactory.File("t.md");
        ConfigFile a = IndexFactory.File("a.md", content: "a1\na2\na3");
        ConfigFile b = IndexFactory.File("b.md", content: "b1\nb2\nb3");
        ConfigIndex index = IndexFactory.Index([a, b, target]) with
        {
            Backlinks = new Dictionary<string, IReadOnlyList<Backlink>>
            {
                ["t.md"] =
                [
                    new Backlink("a.md", LinkKind.WikiLink, 3),
                    new Backlink("b.md", LinkKind.WikiLink, 1),
                    new Backlink("a.md", LinkKind.WikiLink, 1),
                    new Backlink("b.md", LinkKind.WikiLink, 3),
                ],
            },
        };

        MapTarget(index).Backlinks.Select(backlink => backlink.Excerpt).ShouldBe(["a3", "b1", "a1", "b3"]);
    }

    [Fact]
    public void ToResponse_Backlink_LineThatTheSourceDoesNotHave_HasNoExcerpt()
    {
        ConfigFile target = IndexFactory.File("t.md");
        ConfigFile source = IndexFactory.File(
            "a.md",
            links: [Resolved(LinkKind.WikiLink, "t", 2, "t.md"), Resolved(LinkKind.WikiLink, "t", 9, "t.md")],
            content: "only [[t]]\n");

        FileResponse response = FileMapper.ToResponse(IndexFactory.Index([source, target]), target);

        response.Backlinks.ShouldBe(
        [
            new FileBacklink("a.md", LinkKind.WikiLink, 2),
            new FileBacklink("a.md", LinkKind.WikiLink, 9),
        ]);
        response.Backlinks.ShouldAllBe(backlink => backlink.Excerpt == null);
    }

    [Fact]
    public void ToResponse_Backlink_FromASourceThatIsNotInTheIndex_HasNoExcerpt()
    {
        ConfigFile target = IndexFactory.File("t.md");
        ConfigIndex index = IndexFactory.Index([target]) with
        {
            Backlinks = new Dictionary<string, IReadOnlyList<Backlink>> { ["t.md"] = [new Backlink("ghost.md", LinkKind.WikiLink, 1)] },
        };

        MapTarget(index).Backlinks.ShouldBe([new FileBacklink("ghost.md", LinkKind.WikiLink, 1)]);
    }

    [Fact]
    public void ToResponse_Backlink_LongLine_IsCutToTwoHundredCharacters()
    {
        ConfigFile target = IndexFactory.File("t.md");
        string line = "Start [[t]] " + new string('y', 500);
        ConfigFile source = IndexFactory.File("a.md", links: [Resolved(LinkKind.WikiLink, "t", 2, "t.md")], content: $"first\n  {line}  \nlast");

        string excerpt = FileMapper.ToResponse(IndexFactory.Index([source, target]), target).Backlinks.Single().Excerpt.ShouldNotBeNull();

        excerpt.Length.ShouldBe(200);
        excerpt.ShouldBe(line[..199] + "…");
    }

    [Fact]
    public void ToResponse_ThousandsOfBacklinksFromOneSource_EachGetsItsOwnLine()
    {
        const int count = 20_000;
        ConfigFile target = IndexFactory.File("t.md");
        ConfigFile source = IndexFactory.File(
            "a.md",
            links: [.. Enumerable.Range(1, count).Select(number => Resolved(LinkKind.WikiLink, "t", number, "t.md"))],
            content: string.Concat(Enumerable.Range(1, count).Select(number => $"line {number} [[t]]\n")));

        FileResponse response = FileMapper.ToResponse(IndexFactory.Index([source, target]), target);

        response.Backlinks.Count.ShouldBe(count);
        for (int index = 0; index < count; index++)
        {
            response.Backlinks[index].Excerpt.ShouldBe($"line {index + 1} [[t]]");
        }
    }

    [Fact]
    public void ToResponse_Serialized_BacklinkHasTheExcerptPropertyOnlyWhenThereIsAnExcerpt()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        ApiJson.Configure(options);
        ConfigFile target = IndexFactory.File("t.md");
        ConfigFile source = IndexFactory.File(
            "a.md",
            links: [Resolved(LinkKind.WikiLink, "t", 1, "t.md"), Resolved(LinkKind.WikiLink, "t", 8, "t.md")],
            content: "Şablon [[t]] — \"quoted\"\n");

        FileResponse response = FileMapper.ToResponse(IndexFactory.Index([source, target]), target);

        using JsonDocument json = JsonDocument.Parse(JsonSerializer.Serialize(response, options));
        JsonElement[] backlinks = [.. json.RootElement.GetProperty("backlinks").EnumerateArray()];
        backlinks[0].EnumerateObject().Select(property => property.Name).ShouldBe(["source", "kind", "line", "excerpt"]);
        backlinks[0].GetProperty("excerpt").GetString().ShouldBe("Şablon [[t]] — \"quoted\"");
        backlinks[1].EnumerateObject().Select(property => property.Name).ShouldBe(["source", "kind", "line"]);
    }

    [Fact]
    public void ToResponse_Serialized_ExcerptCutNextToAnEmoji_IsStillValidJson()
    {
        // A cut inside a surrogate pair would leave a lone surrogate, which the JSON writer refuses to write.
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        ApiJson.Configure(options);
        ConfigFile target = IndexFactory.File("t.md");
        ConfigFile source = IndexFactory.File(
            "a.md",
            links: [Resolved(LinkKind.WikiLink, "t", 1, "t.md")],
            content: new string('a', 198) + "\U0001F600" + " [[t]]");

        FileResponse response = FileMapper.ToResponse(IndexFactory.Index([source, target]), target);

        string text = Should.NotThrow(() => JsonSerializer.Serialize(response, options));
        using JsonDocument json = JsonDocument.Parse(text);
        json.RootElement.GetProperty("backlinks")[0].GetProperty("excerpt").GetString().ShouldBe(new string('a', 198) + "…");
    }

    // ---- The line of a frontmatter error ----------------------------------------------------------------------

    private const string BrokenLine = "description: Does X: then Y";

    private static FileResponse MapFrontmatterError(string content, int? line, string error = "Line 3, column 20: oops")
    {
        ConfigFile file = IndexFactory.File("a.md", frontmatterError: error, frontmatterErrorLine: line, content: content);

        return FileMapper.ToResponse(IndexFactory.Index([file]), file);
    }

    private static JsonDocument Serialize(FileResponse response)
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        ApiJson.Configure(options);
        return JsonDocument.Parse(JsonSerializer.Serialize(response, options));
    }

    [Fact]
    public void ToResponse_FrontmatterErrorLine_CarriesTheLineAndTheTextOfThatLineWithoutTheWhitespaceAroundIt()
    {
        FileResponse response = MapFrontmatterError($"---\nname: a\n   {BrokenLine}   \n---\nbody\n", line: 3);

        response.FrontmatterError.ShouldBe("Line 3, column 20: oops");
        response.FrontmatterErrorLine.ShouldBe(3);
        response.FrontmatterErrorText.ShouldBe(BrokenLine);
    }

    [Fact]
    public void ToResponse_FrontmatterErrorLine_IsCountedFromTheTopOfTheFileAndNotFromTheBody()
    {
        ConfigFile file = IndexFactory.File(
            "a.md",
            frontmatterError: "Line 3, column 20: oops",
            frontmatterErrorLine: 3,
            body: "body\n",
            bodyStartLine: 5,
            content: $"---\nname: a\n{BrokenLine}\n---\nbody\n");

        FileResponse response = FileMapper.ToResponse(IndexFactory.Index([file]), file);

        response.FrontmatterErrorText.ShouldBe(BrokenLine);
        response.Body.ShouldBe("body\n");
    }

    [Fact]
    public void ToResponse_FrontmatterErrorLine_WindowsLineEndings_LeaveNoCarriageReturnInTheText()
    {
        FileResponse response = MapFrontmatterError($"---\r\nname: a\r\n{BrokenLine}\r\n---\r\nbody\r\n", line: 3);

        response.FrontmatterErrorText.ShouldBe(BrokenLine);
    }

    [Fact]
    public void ToResponse_FrontmatterErrorLine_TextIsLeftAsWrittenAndKeepsItsNonAsciiLetters()
    {
        const string written = "açıklama: \"Şablon\": **kalın** [[bağ]] | `kod`";

        FileResponse response = MapFrontmatterError($"---\n{written}\n---\n", line: 2);

        response.FrontmatterErrorText.ShouldBe(written);
    }

    [Fact]
    public void ToResponse_FrontmatterErrorWithoutALine_HasTheMessageButNoLineAndNoText()
    {
        // The content has lines; without a line that the error names, none of them is picked.
        FileResponse response = MapFrontmatterError("---\n- a\n- b\n---\nbody\n", line: null, error: "Frontmatter is not a mapping");

        response.FrontmatterError.ShouldBe("Frontmatter is not a mapping");
        response.FrontmatterErrorLine.ShouldBeNull();
        response.FrontmatterErrorText.ShouldBeNull();
    }

    [Fact]
    public void ToResponse_FrontmatterErrorLine_OfABlankLine_IsKeptButHasNoText()
    {
        FileResponse response = MapFrontmatterError("---\nname: a\n  \t \nbad: x: y\n---\n", line: 3);

        response.FrontmatterErrorLine.ShouldBe(3);
        response.FrontmatterErrorText.ShouldBeNull();
    }

    [Fact]
    public void ToResponse_FrontmatterErrorLine_BeyondTheContent_IsKeptButHasNoText()
    {
        FileResponse response = MapFrontmatterError("---\nname: a\n---\n", line: 9);

        response.FrontmatterErrorLine.ShouldBe(9);
        response.FrontmatterErrorText.ShouldBeNull();
    }

    [Theory]
    [InlineData(199)]
    [InlineData(200)]
    public void ToResponse_FrontmatterErrorText_OfUpToTwoHundredCharacters_IsNotCut(int length)
    {
        string line = "key: " + new string('x', length - "key: ".Length);

        FileResponse response = MapFrontmatterError($"---\n  {line}  \n---\n", line: 2);

        response.FrontmatterErrorText.ShouldNotBeNull().Length.ShouldBe(length);
        response.FrontmatterErrorText.ShouldBe(line);
    }

    [Theory]
    [InlineData(201)]
    [InlineData(1000)]
    public void ToResponse_FrontmatterErrorText_OfMoreThanTwoHundredCharacters_IsCutToTwoHundredWithTheEllipsisIncluded(int length)
    {
        string line = "key: " + new string('x', length - "key: ".Length);

        FileResponse response = MapFrontmatterError($"---\n{line}\n---\n", line: 2);

        response.FrontmatterErrorText.ShouldNotBeNull().Length.ShouldBe(200);
        response.FrontmatterErrorText.ShouldBe(line[..199] + "…");
    }

    [Fact]
    public void ToResponse_Serialized_ErrorTextCutNextToAnEmoji_IsStillValidJson()
    {
        // A cut inside a surrogate pair would leave a lone surrogate, which the JSON writer refuses to write.
        string line = new string('a', 198) + "\U0001F600" + ": tail";

        FileResponse response = MapFrontmatterError($"---\n{line}\n---\n", line: 2);

        using JsonDocument json = Should.NotThrow(() => Serialize(response));
        json.RootElement.GetProperty("frontmatterErrorText").GetString().ShouldBe(new string('a', 198) + "…");
    }

    [Fact]
    public void ToResponse_Serialized_ErrorLineAndTextAreCamelCasePropertiesThatAreThereOnlyWhenTheyAreKnown()
    {
        static string[] ErrorProperties(JsonDocument json) =>
            [.. json.RootElement.EnumerateObject().Select(property => property.Name).Where(name => name.StartsWith("frontmatterError", StringComparison.Ordinal))];

        using JsonDocument located = Serialize(MapFrontmatterError($"---\n{BrokenLine}\n---\n", line: 2));
        using JsonDocument unlocated = Serialize(MapFrontmatterError("---\n- a\n---\n", line: null, error: "Frontmatter is not a mapping"));
        ConfigFile fine = IndexFactory.File("a.md");
        using JsonDocument healthy = Serialize(FileMapper.ToResponse(IndexFactory.Index([fine]), fine));

        ErrorProperties(located).ShouldBe(["frontmatterError", "frontmatterErrorLine", "frontmatterErrorText"]);
        located.RootElement.GetProperty("frontmatterErrorLine").GetInt32().ShouldBe(2);
        located.RootElement.GetProperty("frontmatterErrorText").GetString().ShouldBe(BrokenLine);
        ErrorProperties(unlocated).ShouldBe(["frontmatterError"]);
        ErrorProperties(healthy).ShouldBeEmpty();
    }

    [Fact]
    public void ToResponse_Tags_AreCarriedWhenTheNoteHasAnyAndLeftOutOtherwise()
    {
        ConfigFile tagged = IndexFactory.File("Tagged.md", Layer.Note, tags: ["moc", "todo"]);
        ConfigFile plain = IndexFactory.File("Plain.md", Layer.Note);
        ConfigIndex index = IndexFactory.Index([tagged, plain], profile: SourceProfile.Vault);

        FileMapper.ToResponse(index, tagged).Tags.ShouldBe(["moc", "todo"]);
        FileMapper.ToResponse(index, plain).Tags.ShouldBeNull();
    }
}
