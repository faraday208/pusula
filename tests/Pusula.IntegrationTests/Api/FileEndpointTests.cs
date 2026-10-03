using System.Net;
using System.Text;
using System.Text.Json;
using Pusula.IntegrationTests.Support;
using Shouldly;
using Xunit;

namespace Pusula.IntegrationTests.Api;

public sealed class FileEndpointTests(ApiFixture fixture) : IClassFixture<ApiFixture>
{
    private static string Url(string path) => SyntheticRoot.Api($"file?path={Uri.EscapeDataString(path)}");

    private static string[] Describe(JsonElement links) =>
    [
        .. links.EnumerateArray().Select(link =>
            $"{link.GetProperty("kind").GetString()}|{link.GetProperty("raw").GetString()}|{link.GetProperty("line").GetInt32()}|"
            + $"{link.GetProperty("status").GetString()}|{(link.TryGetProperty("target", out JsonElement target) ? target.GetString() : "-")}"),
    ];

    // ---- Found ------------------------------------------------------------------------------------------------

    [Fact]
    public async Task GetFile_Skill_ReturnsFrontmatterBodyLinksAndBacklinks()
    {
        using HttpResponseMessage response = await fixture.Client.GetResponseAsync(Url("skills/deploy/SKILL.md"));
        using JsonDocument json = await response.ReadJsonAsync();
        JsonElement file = json.RootElement;

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        response.Content.Headers.ContentType?.MediaType.ShouldBe("application/json");
        file.GetProperty("path").GetString().ShouldBe("skills/deploy/SKILL.md");
        file.GetProperty("name").GetString().ShouldBe("SKILL.md");
        file.GetProperty("layer").GetString().ShouldBe("Skill");
        file.GetProperty("loadMode").GetString().ShouldBe("DescriptionEverySession");
        file.GetProperty("tokens").PropertyNames().ShouldBe(["total", "everySession", "projectSession"], ignoreOrder: true);
        file.GetProperty("tokens").GetProperty("everySession").GetInt32().ShouldBe(("deploy: Deploys the thing".Length + 3) / 4);
        file.GetProperty("tokens").GetProperty("projectSession").GetInt32().ShouldBe(0);
        file.GetProperty("tokens").GetProperty("total").GetInt32().ShouldBeGreaterThan(7);
        file.GetProperty("frontmatter").GetProperty("name").GetString().ShouldBe("deploy");
        file.GetProperty("frontmatter").GetProperty("description").GetString().ShouldBe("Deploys the thing");
        file.TryGetProperty("frontmatterError", out _).ShouldBeFalse();
        file.GetProperty("bodyStartLine").GetInt32().ShouldBe(5);
        string body = file.GetProperty("body").GetString()!;
        body.ShouldStartWith("# Deploy");
        body.ShouldContain("[notes](notes.md)");
        body.ShouldNotContain("description: Deploys");
        Describe(file.GetProperty("links")).ShouldBe(["MarkdownLink|notes.md|6|Resolved|skills/deploy/notes.md"]);
        file.GetProperty("backlinks").GetArrayLength().ShouldBe(1);
        file.GetProperty("backlinks")[0].GetProperty("source").GetString().ShouldBe("CLAUDE.md");
        file.GetProperty("backlinks")[0].GetProperty("kind").GetString().ShouldBe("RelativePath");
        file.GetProperty("backlinks")[0].GetProperty("line").GetInt32().ShouldBe(3);
        file.GetProperty("backlinks")[0].PropertyNames().ShouldBe(["source", "kind", "line", "excerpt"], ignoreOrder: true);
        file.GetProperty("backlinks")[0].GetProperty("excerpt").GetString()
            .ShouldBe("Read [the style rule](rules/style.md) and ~/.claude/rules/scoped.md and `skills/deploy/SKILL.md`.");
        file.GetProperty("orphan").GetBoolean().ShouldBeFalse();
        file.GetProperty("version").GetInt64().ShouldBe(1);
    }

    [Fact]
    public async Task GetFile_ClaudeMd_ListsEveryKindOfLinkWithItsStatus()
    {
        using JsonDocument json = await fixture.Client.GetJsonAsync(Url("CLAUDE.md"));

        Describe(json.RootElement.GetProperty("links")).ShouldBe(
        [
            "MarkdownLink|rules/style.md|3|Resolved|rules/style.md",
            "ClaudePath|~/.claude/rules/scoped.md|3|Resolved|rules/scoped.md",
            "RelativePath|skills/deploy/SKILL.md|3|Resolved|skills/deploy/SKILL.md",
            "MarkdownLink|rules/gone.md|4|Broken|rules/gone.md",
            "RelativePath|rules/missing.md|4|Broken|rules/missing.md",
        ]);
        json.RootElement.GetProperty("layer").GetString().ShouldBe("ClaudeMd");
        json.RootElement.GetProperty("backlinks").GetArrayLength().ShouldBe(0);
    }

    [Fact]
    public async Task GetFile_MemoryIndex_ResolvesWikiLinksByFrontmatterNameAndMarksTheMissingOnePending()
    {
        using JsonDocument json = await fixture.Client.GetJsonAsync(Url("projects/demo/memory/MEMORY.md"));
        JsonElement file = json.RootElement;

        file.GetProperty("layer").GetString().ShouldBe("MemoryIndex");
        file.GetProperty("loadMode").GetString().ShouldBe("ProjectSession");
        file.GetProperty("tokens").GetProperty("projectSession").GetInt32().ShouldBe(file.GetProperty("tokens").GetProperty("total").GetInt32());
        file.GetProperty("tokens").GetProperty("everySession").GetInt32().ShouldBe(0);
        Describe(file.GetProperty("links")).ShouldBe(
        [
            "WikiLink|alpha|1|Resolved|projects/demo/memory/alpha.md",
            "WikiLink|ghost|2|Pending|-",
        ]);
    }

    [Fact]
    public async Task GetFile_LinkedMemory_ListsTheBacklink()
    {
        using JsonDocument json = await fixture.Client.GetJsonAsync(Url("projects/demo/memory/alpha.md"));

        json.RootElement.GetProperty("frontmatter").GetProperty("type").GetString().ShouldBe("note");
        json.RootElement.GetProperty("backlinks").EnumerateArray().Select(backlink => $"{backlink.GetProperty("source").GetString()}|{backlink.GetProperty("kind").GetString()}|{backlink.GetProperty("line").GetInt32()}|{backlink.GetProperty("excerpt").GetString()}")
            .ShouldBe(["projects/demo/memory/MEMORY.md|WikiLink|1|- [[alpha]]"]);
        json.RootElement.GetProperty("orphan").GetBoolean().ShouldBeFalse();
    }

    [Fact]
    public async Task GetFile_UnlinkedSharedFile_IsAnOrphanWithoutFrontmatterProperties()
    {
        using JsonDocument json = await fixture.Client.GetJsonAsync(Url("shared/shared-note.md"));

        json.RootElement.GetProperty("orphan").GetBoolean().ShouldBeTrue();
        json.RootElement.PropertyNames().ShouldBe(
            ["path", "name", "layer", "loadMode", "tokens", "body", "bodyStartLine", "links", "backlinks", "orphan", "version"],
            ignoreOrder: true);
        json.RootElement.GetProperty("bodyStartLine").GetInt32().ShouldBe(1);
        json.RootElement.GetProperty("links").GetArrayLength().ShouldBe(0);
    }

    [Fact]
    public async Task GetFile_BrokenFrontmatter_ReportsTheErrorAndKeepsTheKeysThatCouldBeRead()
    {
        using JsonDocument json = await fixture.Client.GetJsonAsync(Url("reference.md"));

        json.RootElement.GetProperty("frontmatterError").GetString()!.ShouldStartWith("Line ");
        json.RootElement.GetProperty("frontmatter").GetProperty("key").GetString().ShouldBe("[unclosed");
        json.RootElement.GetProperty("body").GetString()!.ShouldStartWith("Reference with broken frontmatter.");
    }

    [Fact]
    public async Task GetFile_BrokenFrontmatter_NamesALineOfTheFileAndGivesTheTextOfThatLine()
    {
        using JsonDocument json = await fixture.Client.GetJsonAsync(Url("reference.md"));
        string[] lines = File.ReadAllLines(Path.Combine(fixture.Root.Path, "reference.md"));

        // YAML notices the unclosed bracket only at the end of the block: whichever line it names, the message names the same.
        int line = json.RootElement.GetProperty("frontmatterErrorLine").GetInt32();
        line.ShouldBeInRange(2, json.RootElement.GetProperty("bodyStartLine").GetInt32() - 1);
        json.RootElement.GetProperty("frontmatterError").GetString()!.ShouldStartWith($"Line {line}, column ");
        json.RootElement.GetProperty("frontmatterErrorText").GetString().ShouldBe(lines[line - 1].Trim());
    }

    [Fact]
    public async Task GetFile_PathWithSpacesAndNonAsciiLetters_IsFoundByItsEncodedKey()
    {
        using JsonDocument json = await fixture.Client.GetJsonAsync(Url("rules/kurallar ve şablon.md"));

        json.RootElement.GetProperty("path").GetString().ShouldBe("rules/kurallar ve şablon.md");
        json.RootElement.GetProperty("body").GetString()!.ShouldStartWith("# Şablon");
    }

    [Fact]
    public async Task GetFile_PathOfEveryTreeFile_IsAKeyThatIsFound()
    {
        using JsonDocument tree = await fixture.Client.GetJsonAsync(fixture.Api("tree"));
        string[] paths = [.. Files(tree.RootElement.GetProperty("nodes"))];

        paths.ShouldNotBeEmpty();
        foreach (string path in paths)
        {
            using JsonDocument file = await fixture.Client.GetJsonAsync(Url(path));
            file.RootElement.GetProperty("path").GetString().ShouldBe(path);
        }

        static IEnumerable<string> Files(JsonElement nodes) =>
            nodes.EnumerateArray().SelectMany(node =>
                node.GetProperty("type").GetString() == "Directory" ? Files(node.GetProperty("children")) : [node.GetProperty("path").GetString()!]);
    }

    [Fact]
    public async Task GetFile_BacklinkFromAFileWithFrontmatter_ExcerptIsTheLineCountedFromTheTopOfTheFile()
    {
        using JsonDocument json = await fixture.Client.GetJsonAsync(Url("skills/deploy/notes.md"));

        // SKILL.md has four lines of frontmatter, so its link sits on line 6 of the file and not on line 2 of the body.
        JsonElement backlink = json.RootElement.GetProperty("backlinks").EnumerateArray().Single();
        backlink.GetProperty("source").GetString().ShouldBe("skills/deploy/SKILL.md");
        backlink.GetProperty("line").GetInt32().ShouldBe(6);
        backlink.GetProperty("excerpt").GetString().ShouldBe("Steps live in [notes](notes.md).");
    }

    [Fact]
    public async Task GetFile_BacklinkExcerpts_ComeFromTheFilesAsTheyAreOnDisk()
    {
        const string link = "[the target](target.md)";
        using var sandbox = new TempDirectory();
        sandbox.Write("target.md", "# Target\n");

        // A file written on Windows: carriage returns, an indented line, trailing spaces.
        sandbox.Write("crlf.md", $"# Windows\r\n\r\n    - see {link} and **more**   \r\n");

        // A file with a byte order mark whose very first line holds the link.
        File.WriteAllText(sandbox.Resolve("bom.md"), $"{link} first line\n", new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));

        // A long line with an emoji where it is cut: the 199 characters before the ellipsis would split the pair.
        string prefix = $"{link} ";
        string beforeEmoji = prefix + new string('a', 198 - prefix.Length);
        string longLine = beforeEmoji + "\U0001F600" + new string('b', 100);
        char.IsHighSurrogate(longLine[198]).ShouldBeTrue();
        char.IsLowSurrogate(longLine[199]).ShouldBeTrue();
        sandbox.Write("long.md", $"# Long\n{longLine}\n");

        await using var factory = new PusulaFactory(sandbox.Path);
        using HttpClient client = factory.CreateClient();

        using JsonDocument json = await client.GetJsonAsync(factory.Api("file?path=target.md"));

        Dictionary<string, JsonElement> bySource = json.RootElement.GetProperty("backlinks").EnumerateArray()
            .ToDictionary(backlink => backlink.GetProperty("source").GetString()!);
        bySource.Keys.ShouldBe(["bom.md", "crlf.md", "long.md"], ignoreOrder: true);
        bySource["crlf.md"].GetProperty("line").GetInt32().ShouldBe(3);
        bySource["crlf.md"].GetProperty("excerpt").GetString().ShouldBe($"- see {link} and **more**");
        bySource["bom.md"].GetProperty("line").GetInt32().ShouldBe(1);
        bySource["bom.md"].GetProperty("excerpt").GetString().ShouldBe($"{link} first line");
        bySource["long.md"].GetProperty("line").GetInt32().ShouldBe(2);
        bySource["long.md"].GetProperty("excerpt").GetString().ShouldBe(beforeEmoji + "…");
    }

    // ---- 400 --------------------------------------------------------------------------------------------------

    [Theory]
    [InlineData("/api/sources/root/file")]
    [InlineData("/api/sources/root/file?path=")]
    [InlineData("/api/sources/root/file?path=%20")]
    [InlineData("/api/sources/root/file?path=%09%20")]
    [InlineData("/api/sources/root/file?other=CLAUDE.md")]
    public async Task GetFile_MissingOrBlankPath_Returns400WithProblemDetails(string url)
    {
        using HttpResponseMessage response = await fixture.Client.GetResponseAsync(url);
        using JsonDocument json = await response.ReadJsonAsync();

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        response.Content.Headers.ContentType?.MediaType.ShouldBe("application/problem+json");
        json.RootElement.GetProperty("status").GetInt32().ShouldBe(400);
        json.RootElement.GetProperty("title").GetString().ShouldBe("Missing path");
        json.RootElement.GetProperty("detail").GetString().ShouldBe("The 'path' query parameter is required.");
    }

    // ---- 404 --------------------------------------------------------------------------------------------------

    [Fact]
    public async Task GetFile_UnknownPath_Returns404WithProblemDetails()
    {
        using HttpResponseMessage response = await fixture.Client.GetResponseAsync(Url("rules/nope.md"));
        using JsonDocument json = await response.ReadJsonAsync();

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        response.Content.Headers.ContentType?.MediaType.ShouldBe("application/problem+json");
        json.RootElement.GetProperty("status").GetInt32().ShouldBe(404);
        json.RootElement.GetProperty("title").GetString().ShouldBe("File not found");
        json.RootElement.GetProperty("detail").GetString().ShouldBe("No indexed file has this path.");
    }

    [Fact]
    public async Task GetFile_PathsThatClimbOutOfTheRootOrAreAbsolute_AreNotFound()
    {
        string[] paths =
        [
            "../outside.md",
            "..\\outside.md",
            "..%2Foutside.md",
            "%2e%2e/outside.md",
            "rules/../../outside.md",
            "rules/../../../../../../etc/passwd",
            "../../../../etc/passwd",
            "/etc/passwd",
            "\\etc\\passwd",
            "C:\\Windows\\win.ini",
            "file:///etc/passwd",
            fixture.Root.OutsideFile,
            Path.Combine(fixture.Root.Path, "CLAUDE.md"),
            Path.Combine(fixture.Root.Path, "settings.json"),
        ];

        foreach (string path in paths)
        {
            using HttpResponseMessage response = await fixture.Client.GetResponseAsync(fixture.Api($"file?path={path}"));
            string body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);

            response.StatusCode.ShouldBe(HttpStatusCode.NotFound, path);
            body.ShouldNotContain(SyntheticRoot.OutsideSecret, customMessage: path);
            body.ShouldNotContain(SyntheticRoot.SettingsSecret, customMessage: path);
        }
    }

    [Fact]
    public async Task GetFile_PathsThatAreNotIndexKeys_AreNotFoundEvenWhenTheyMeanAnIndexedFile()
    {
        string[] paths =
        [
            "./CLAUDE.md",
            "rules/../CLAUDE.md",
            "rules//style.md",
            "rules/style.md/",
            "rules/style.md/.",
            "/CLAUDE.md",
            "CLAUDE.md ",
            "CLAUDE.md%00",
            "CLAUDE",
            "rules",
            "skills/deploy",
        ];

        foreach (string path in paths)
        {
            using HttpResponseMessage response = await fixture.Client.GetResponseAsync(Url(path));

            response.StatusCode.ShouldBe(HttpStatusCode.NotFound, path);
        }
    }

    [Fact]
    public async Task GetFile_FilesThatExistButAreNotIndexed_AreNotFoundAndNeverRead()
    {
        string[] paths =
        [
            "settings.json",
            ".credentials.json",
            ".hidden/secret.md",
            "plugins/cache/ignored.md",
            "projects/demo/transcript.jsonl",
        ];

        foreach (string path in paths)
        {
            using HttpResponseMessage response = await fixture.Client.GetResponseAsync(Url(path));
            string body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);

            response.StatusCode.ShouldBe(HttpStatusCode.NotFound, path);
            body.ShouldNotContain(SyntheticRoot.SettingsSecret, customMessage: path);
            body.ShouldNotContain(SyntheticRoot.CredentialsSecret, customMessage: path);
        }
    }

    [Fact]
    public async Task GetFile_KeyCase_FollowsThePlatform()
    {
        using HttpResponseMessage response = await fixture.Client.GetResponseAsync(Url("claude.md"));

        response.StatusCode.ShouldBe(OperatingSystem.IsWindows() ? HttpStatusCode.OK : HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task GetFile_WrongMethod_Returns405()
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, Url("CLAUDE.md"));

        using HttpResponseMessage response = await fixture.Client.SendAsync(request, TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.MethodNotAllowed);
    }
}
