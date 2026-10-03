using System.Net;
using System.Text.Json;
using Pusula.IntegrationTests.Support;
using Shouldly;
using Xunit;

namespace Pusula.IntegrationTests.Api;

public sealed class SourcesEndpointTests(SourcesFixture fixture) : IClassFixture<SourcesFixture>
{
    private static string[] Describe(JsonElement links) =>
    [
        .. links.EnumerateArray().Select(link =>
            $"{link.GetProperty("kind").GetString()}|{link.GetProperty("raw").GetString()}|{link.GetProperty("line").GetInt32()}|"
            + $"{link.GetProperty("status").GetString()}|{(link.TryGetProperty("target", out JsonElement target) ? target.GetString() : "-")}"),
    ];

    private static string[] Names(JsonElement nodes) =>
    [
        .. nodes.EnumerateArray().SelectMany(node =>
            node.GetProperty("type").GetString() == "Directory" ? Names(node.GetProperty("children")) : [node.GetProperty("path").GetString()!]),
    ];

    // ---- /api/sources -----------------------------------------------------------------------------------------

    [Fact]
    public async Task GetSources_ListsEverySourceInOrderWithItsProfileAvailabilityAndFileCount()
    {
        using HttpResponseMessage response = await fixture.Client.GetResponseAsync("/api/sources");
        using JsonDocument json = await response.ReadJsonAsync();
        JsonElement root = json.RootElement;
        JsonElement[] sources = [.. root.GetProperty("sources").EnumerateArray()];

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        response.Content.Headers.ContentType?.MediaType.ShouldBe("application/json");
        root.PropertyNames().ShouldBe(["sources", "canEdit", "machine", "remoteEdit", "sourcesFile", "editBlocked"], ignoreOrder: true);
        root.GetProperty("sourcesFile").GetString().ShouldBe(fixture.SourcesFile);
        root.GetProperty("machine").GetString().ShouldBe(Environment.MachineName);
        root.GetProperty("remoteEdit").GetBoolean().ShouldBeFalse();

        // The in-memory test server gives a request no address of its client: it is not "from this machine", so the
        // list cannot be changed by it (see SourceEditingTests for the requests that can).
        root.GetProperty("canEdit").GetBoolean().ShouldBeFalse();
        root.GetProperty("editBlocked").GetString().ShouldBe("Remote");
        sources.Select(source => source.GetProperty("id").GetString()).ShouldBe(["claude", "not-defteri", "kayip"]);

        sources[0].PropertyNames().ShouldBe(["id", "name", "path", "profile", "available", "fileCount"], ignoreOrder: true);
        sources[0].GetProperty("name").GetString().ShouldBe("Claude Config");
        sources[0].GetProperty("path").GetString().ShouldBe(fixture.ClaudeFolder);
        sources[0].GetProperty("profile").GetString().ShouldBe("Claude");
        sources[0].GetProperty("available").GetBoolean().ShouldBeTrue();
        sources[0].GetProperty("fileCount").GetInt32().ShouldBe(2);

        sources[1].GetProperty("name").GetString().ShouldBe("Not Defteri");
        sources[1].GetProperty("profile").GetString().ShouldBe("Vault");
        sources[1].GetProperty("fileCount").GetInt32().ShouldBe(3);
        sources[1].TryGetProperty("error", out _).ShouldBeFalse();
        sources[1].TryGetProperty("errorCode", out _).ShouldBeFalse();

        sources[2].PropertyNames().ShouldBe(["id", "name", "path", "profile", "available", "fileCount", "error", "errorCode"], ignoreOrder: true);
        sources[2].GetProperty("available").GetBoolean().ShouldBeFalse();
        sources[2].GetProperty("fileCount").GetInt32().ShouldBe(0);
        sources[2].GetProperty("error").GetString().ShouldNotBeNullOrWhiteSpace();
        sources[2].GetProperty("errorCode").GetString().ShouldBe("FolderMissing");
        sources[2].GetProperty("profile").GetString().ShouldBe("Markdown");
    }

    [Fact]
    public async Task GetSources_FolderFromPusulaRoot_IsOneSourceAndHasNoSourcesFile()
    {
        using var root = new SyntheticRoot();
        await using var factory = new PusulaFactory(root.Path);
        using HttpClient client = factory.CreateClient();

        using JsonDocument json = await client.GetJsonAsync("/api/sources");

        json.RootElement.PropertyNames().ShouldBe(["sources", "canEdit", "machine", "remoteEdit", "editBlocked"], ignoreOrder: true);
        json.RootElement.GetProperty("canEdit").GetBoolean().ShouldBeFalse();
        JsonElement source = json.RootElement.GetProperty("sources").EnumerateArray().Single();
        source.GetProperty("id").GetString().ShouldBe("root");
        source.GetProperty("name").GetString().ShouldBe("root");
        source.GetProperty("path").GetString().ShouldBe(root.Path);
        source.GetProperty("profile").GetString().ShouldBe("Claude");
        source.GetProperty("available").GetBoolean().ShouldBeTrue();
        source.GetProperty("fileCount").GetInt32().ShouldBe(SyntheticRoot.IndexedFiles.Count);
    }

    // ---- Each source has its own tree, files and overview -----------------------------------------------------

    [Fact]
    public async Task GetTree_EachSource_ListsOnlyItsOwnFiles()
    {
        using JsonDocument claude = await fixture.Client.GetJsonAsync("/api/sources/claude/tree");
        using JsonDocument vault = await fixture.Client.GetJsonAsync("/api/sources/not-defteri/tree");

        claude.RootElement.GetProperty("root").GetString().ShouldBe(fixture.ClaudeFolder);
        Names(claude.RootElement.GetProperty("nodes")).ShouldBe(["CLAUDE.md", "rules/style.md"], ignoreOrder: true);
        vault.RootElement.GetProperty("root").GetString().ShouldBe(fixture.VaultFolder);
        Names(vault.RootElement.GetProperty("nodes")).ShouldBe(["Alpha.md", "Home.md", "folder/Beta.md"], ignoreOrder: true);
    }

    [Fact]
    public async Task GetTree_VaultNotes_CarryTheirTagsAndClaudeFilesDoNot()
    {
        using JsonDocument vault = await fixture.Client.GetJsonAsync("/api/sources/not-defteri/tree");
        using JsonDocument claude = await fixture.Client.GetJsonAsync("/api/sources/claude/tree");

        JsonElement home = vault.RootElement.GetProperty("nodes").EnumerateArray().Single(node => node.GetProperty("name").GetString() == "Home.md");
        home.GetProperty("tags").EnumerateArray().Select(tag => tag.GetString()).ShouldBe(["moc", "todo"]);
        home.GetProperty("layer").GetString().ShouldBe("Note");
        home.GetProperty("loadMode").GetString().ShouldBe("OnDemand");
        home.GetProperty("everySessionTokens").GetInt32().ShouldBe(0);
        vault.RootElement.GetProperty("nodes").EnumerateArray().Single(node => node.GetProperty("name").GetString() == "Alpha.md")
            .GetProperty("tags").EnumerateArray().Select(tag => tag.GetString()).ShouldBe(["todo", "Done"]);
        Names(vault.RootElement.GetProperty("nodes")).ShouldContain("folder/Beta.md");
        claude.RootElement.GetRawText().ShouldNotContain("\"tags\"");
    }

    [Fact]
    public async Task GetFile_VaultNote_ListsItsLinksBacklinksAndTags()
    {
        using JsonDocument home = await fixture.Client.GetJsonAsync("/api/sources/not-defteri/file?path=Home.md");
        using JsonDocument alpha = await fixture.Client.GetJsonAsync("/api/sources/not-defteri/file?path=Alpha.md");

        home.RootElement.GetProperty("layer").GetString().ShouldBe("Note");
        home.RootElement.GetProperty("tags").EnumerateArray().Select(tag => tag.GetString()).ShouldBe(["moc", "todo"]);
        home.RootElement.GetProperty("tokens").GetProperty("everySession").GetInt32().ShouldBe(0);
        Describe(home.RootElement.GetProperty("links")).ShouldBe(
        [
            "WikiLink|Alpha|5|Resolved|Alpha.md",
            "Embed|pic.png|5|NonMarkdown|_attachments/pic.png",
            "WikiLink|Unwritten|5|Pending|-",
            "MarkdownLink|folder/Beta.md|5|Resolved|folder/Beta.md",
        ]);
        home.RootElement.GetProperty("backlinks").EnumerateArray().Select(backlink => $"{backlink.GetProperty("source").GetString()}|{backlink.GetProperty("kind").GetString()}")
            .ShouldBe(["Alpha.md|WikiLink"]);

        alpha.RootElement.GetProperty("tags").EnumerateArray().Select(tag => tag.GetString()).ShouldBe(["todo", "Done"]);
        alpha.RootElement.GetProperty("backlinks").GetArrayLength().ShouldBe(1);
    }

    [Fact]
    public async Task GetOverview_Vault_NamesItsProfileCountsTagsAndListsPendingNotesAndOrphans()
    {
        using JsonDocument json = await fixture.Client.GetJsonAsync("/api/sources/not-defteri/overview");
        JsonElement overview = json.RootElement;

        overview.GetProperty("profile").GetString().ShouldBe("Vault");
        overview.GetProperty("fileCount").GetInt32().ShouldBe(3);
        overview.GetProperty("everySessionTokens").GetInt32().ShouldBe(0);
        overview.GetProperty("layers").EnumerateArray().Select(layer => $"{layer.GetProperty("layer").GetString()}:{layer.GetProperty("files").GetInt32()}").ShouldBe(["Note:3"]);
        overview.GetProperty("tags").EnumerateArray().Select(tag => $"{tag.GetProperty("name").GetString()}:{tag.GetProperty("count").GetInt32()}")
            .ShouldBe(["todo:2", "Done:1", "moc:1"]);
        overview.GetProperty("pending").EnumerateArray().Select(item => item.GetProperty("raw").GetString()).ShouldBe(["Unwritten"]);
        overview.GetProperty("broken").GetArrayLength().ShouldBe(0);
        overview.GetProperty("orphans").GetArrayLength().ShouldBe(0);
    }

    [Fact]
    public async Task GetOverview_ClaudeFolder_NamesItsProfileAndHasNoTags()
    {
        using JsonDocument json = await fixture.Client.GetJsonAsync("/api/sources/claude/overview");

        json.RootElement.GetProperty("profile").GetString().ShouldBe("Claude");
        json.RootElement.GetProperty("tags").GetArrayLength().ShouldBe(0);
        json.RootElement.GetProperty("fileCount").GetInt32().ShouldBe(2);
        json.RootElement.GetProperty("everySessionTokens").GetInt32().ShouldBeGreaterThan(0);
    }

    [Fact]
    public async Task GetFile_PathOfAnotherSource_IsNotFoundInThisOne()
    {
        using HttpResponseMessage inVault = await fixture.Client.GetResponseAsync("/api/sources/not-defteri/file?path=CLAUDE.md");
        using HttpResponseMessage inClaude = await fixture.Client.GetResponseAsync("/api/sources/claude/file?path=Home.md");
        using HttpResponseMessage settings = await fixture.Client.GetResponseAsync("/api/sources/claude/file?path=settings.json");
        string body = await settings.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);

        inVault.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        inClaude.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        settings.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        body.ShouldNotContain(SourcesFixture.ClaudeSecret);
    }

    // ---- Sources that are not there ---------------------------------------------------------------------------

    [Theory]
    [InlineData("tree")]
    [InlineData("overview")]
    [InlineData("file?path=Home.md")]
    [InlineData("events")]
    public async Task Get_UnknownSource_Is404WithProblemDetails(string endpoint)
    {
        using HttpResponseMessage response = await fixture.Client.GetResponseAsync($"/api/sources/no-such-source/{endpoint}");
        using JsonDocument json = await response.ReadJsonAsync();

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        response.Content.Headers.ContentType?.MediaType.ShouldBe("application/problem+json");
        json.RootElement.GetProperty("status").GetInt32().ShouldBe(404);
        json.RootElement.GetProperty("title").GetString().ShouldBe("Source not found");
        json.RootElement.TryGetProperty("code", out _).ShouldBeFalse();
    }

    [Theory]
    [InlineData("tree")]
    [InlineData("overview")]
    [InlineData("file?path=Home.md")]
    [InlineData("file")]
    [InlineData("events")]
    public async Task Get_SourceWhoseFolderIsMissing_Is503WithTheReason(string endpoint)
    {
        using HttpResponseMessage response = await fixture.Client.GetResponseAsync($"/api/sources/kayip/{endpoint}");
        using JsonDocument json = await response.ReadJsonAsync();

        response.StatusCode.ShouldBe(HttpStatusCode.ServiceUnavailable);
        response.Content.Headers.ContentType?.MediaType.ShouldBe("application/problem+json");
        json.RootElement.GetProperty("status").GetInt32().ShouldBe(503);
        json.RootElement.GetProperty("title").GetString().ShouldBe("Source unavailable");
        json.RootElement.GetProperty("detail").GetString().ShouldNotBeNullOrWhiteSpace();
        json.RootElement.GetProperty("code").GetString().ShouldBe("FolderMissing");
    }

    // The id only selects one of the sources that were configured; whatever else it says is not a source.
    [Theory]
    [InlineData("..")]
    [InlineData("%2e%2e")]
    [InlineData("claude%2F..%2Fnot-defteri")]
    [InlineData("%2Fetc")]
    [InlineData("CLAUDE")]
    [InlineData("claude%20")]
    public async Task Get_SourceIdThatIsNotOneOfTheConfiguredIds_Is404(string id)
    {
        using HttpResponseMessage response = await fixture.Client.GetResponseAsync($"/api/sources/{id}/tree");

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Theory]
    [InlineData("/api/tree")]
    [InlineData("/api/overview")]
    [InlineData("/api/file?path=CLAUDE.md")]
    [InlineData("/api/events")]
    public async Task Get_RoutesWithoutASource_AreGone(string url)
    {
        using HttpResponseMessage response = await fixture.Client.GetResponseAsync(url);

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task GetEvents_OfASource_StartsWithReadyForThatSource()
    {
        await using SseClient events = await SseClient.ConnectAsync(fixture.Client, "/api/sources/not-defteri/events");

        (string type, JsonDocument data) = await events.NextAsync();

        type.ShouldBe("ready");
        data.RootElement.GetProperty("version").GetInt64().ShouldBe(1);
    }
}
