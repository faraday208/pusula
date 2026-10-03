using System.Net;
using System.Text.Json;
using Pusula.IntegrationTests.Support;
using Shouldly;
using Xunit;

namespace Pusula.IntegrationTests.Api;

public sealed class OverviewEndpointTests(ApiFixture fixture) : IClassFixture<ApiFixture>
{
    private static string[] Strings(JsonElement array, string property) =>
        [.. array.EnumerateArray().Select(item => item.GetProperty(property).GetString()!)];

    [Fact]
    public async Task GetOverview_SyntheticRoot_ReturnsTheSummaryOfTheGivenRoot()
    {
        using HttpResponseMessage response = await fixture.Client.GetResponseAsync(fixture.Api("overview"));
        using JsonDocument json = await response.ReadJsonAsync();
        JsonElement overview = json.RootElement;

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        response.Content.Headers.ContentType?.MediaType.ShouldBe("application/json");
        overview.PropertyNames().ShouldBe(
        [
            "root", "version", "builtAt", "fileCount", "everySessionTokens", "layers", "heaviest", "projectMemory",
            "broken", "pending", "orphans", "frontmatterErrors", "outputStyle", "profile", "tags", "recent", "mostLinked",
        ], ignoreOrder: true);
        overview.GetProperty("root").GetString().ShouldBe(fixture.Root.Path);
        overview.GetProperty("version").GetInt64().ShouldBe(1);
        overview.GetProperty("fileCount").GetInt32().ShouldBe(SyntheticRoot.IndexedFiles.Count);
        overview.GetProperty("outputStyle").GetString().ShouldBe("Terse");
    }

    [Fact]
    public async Task GetOverview_Layers_ListOnlyLayersThatHaveFiles()
    {
        using JsonDocument json = await fixture.Client.GetJsonAsync(fixture.Api("overview"));
        JsonElement layers = json.RootElement.GetProperty("layers");

        layers.EnumerateArray().Select(layer => $"{layer.GetProperty("layer").GetString()}:{layer.GetProperty("files").GetInt32()}").ShouldBe(
        [
            "ClaudeMd:1", "Rule:2", "PathRule:1", "Skill:1", "SkillResource:2", "Agent:1", "OutputStyle:2",
            "MemoryIndex:1", "Memory:2", "Reference:1", "Shared:1",
        ]);
        layers.EnumerateArray().Sum(layer => layer.GetProperty("everySessionTokens").GetInt32())
            .ShouldBe(json.RootElement.GetProperty("everySessionTokens").GetInt32());
        json.RootElement.GetProperty("everySessionTokens").GetInt32().ShouldBeGreaterThan(0);
        layers.EnumerateArray().Single(layer => layer.GetProperty("layer").GetString() == "PathRule").GetProperty("everySessionTokens").GetInt32().ShouldBe(0);
    }

    [Fact]
    public async Task GetOverview_Heaviest_ListsFilesThatAddToEverySessionLargestFirst()
    {
        using JsonDocument json = await fixture.Client.GetJsonAsync(fixture.Api("overview"));
        JsonElement heaviest = json.RootElement.GetProperty("heaviest");

        Strings(heaviest, "path").Order(StringComparer.Ordinal).ShouldBe(
        [
            "CLAUDE.md", "agents/reviewer.md", "output-styles/terse.md", "rules/kurallar ve şablon.md", "rules/style.md", "skills/deploy/SKILL.md",
        ]);
        heaviest[0].GetProperty("path").GetString().ShouldBe("CLAUDE.md");
        heaviest[0].PropertyNames().ShouldBe(["path", "layer", "loadMode", "everySessionTokens"], ignoreOrder: true);
        heaviest.EnumerateArray().Select(entry => entry.GetProperty("everySessionTokens").GetInt32()).ShouldBe(
            heaviest.EnumerateArray().Select(entry => entry.GetProperty("everySessionTokens").GetInt32()).OrderByDescending(tokens => tokens));
    }

    [Fact]
    public async Task GetOverview_ProjectMemory_ListsTheMemoryIndexOfEachProject()
    {
        using JsonDocument json = await fixture.Client.GetJsonAsync(fixture.Api("overview"));
        JsonElement entry = json.RootElement.GetProperty("projectMemory").EnumerateArray().Single();

        entry.GetProperty("path").GetString().ShouldBe("projects/demo/memory/MEMORY.md");
        entry.GetProperty("project").GetString().ShouldBe("demo");
        entry.GetProperty("tokens").GetInt32().ShouldBeGreaterThan(0);
    }

    [Fact]
    public async Task GetOverview_BrokenAndPendingLinks_AreListedSeparately()
    {
        using JsonDocument json = await fixture.Client.GetJsonAsync(fixture.Api("overview"));

        json.RootElement.GetProperty("broken").EnumerateArray().Select(item =>
            $"{item.GetProperty("source").GetString()}|{item.GetProperty("line").GetInt32()}|{item.GetProperty("kind").GetString()}|{item.GetProperty("raw").GetString()}|"
            + $"{(item.TryGetProperty("target", out JsonElement target) ? target.GetString() : "-")}").ShouldBe(
        [
            "CLAUDE.md|4|MarkdownLink|rules/gone.md|rules/gone.md",
            "CLAUDE.md|4|RelativePath|rules/missing.md|rules/missing.md",
            "rules/style.md|5|WikiLink|unknown-target|-",
        ]);
        json.RootElement.GetProperty("pending").EnumerateArray().Select(item =>
            $"{item.GetProperty("source").GetString()}|{item.GetProperty("line").GetInt32()}|{item.GetProperty("kind").GetString()}|{item.GetProperty("raw").GetString()}").ShouldBe(
        [
            "projects/demo/memory/MEMORY.md|2|WikiLink|ghost",
        ]);
        json.RootElement.GetProperty("pending")[0].PropertyNames().ShouldBe(["source", "line", "kind", "raw"], ignoreOrder: true);
    }

    [Fact]
    public async Task GetOverview_OrphansAndFrontmatterErrors_ListTheFlaggedFiles()
    {
        using JsonDocument json = await fixture.Client.GetJsonAsync(fixture.Api("overview"));

        json.RootElement.GetProperty("orphans").EnumerateArray().Select(item => $"{item.GetProperty("path").GetString()}|{item.GetProperty("layer").GetString()}").ShouldBe(
        [
            "projects/demo/memory/lonely.md|Memory",
            "reference.md|Reference",
            "shared/shared-note.md|Shared",
            "skills/deploy/unlinked.md|SkillResource",
        ]);
        JsonElement errors = json.RootElement.GetProperty("frontmatterErrors");
        errors.GetArrayLength().ShouldBe(1);
        errors[0].GetProperty("path").GetString().ShouldBe("reference.md");
        errors[0].GetProperty("error").GetString()!.ShouldStartWith("Line ");

        // The same line that the message names, as a number of its own.
        int line = errors[0].GetProperty("line").GetInt32();
        errors[0].GetProperty("error").GetString()!.ShouldStartWith($"Line {line}, column ");
        errors[0].PropertyNames().ShouldBe(["path", "error", "line"], ignoreOrder: true);
    }

    [Fact]
    public async Task GetOverview_NeverMentionsSettingsCredentialsOrHiddenFiles()
    {
        using HttpResponseMessage response = await fixture.Client.GetResponseAsync(fixture.Api("overview"));
        string body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);

        body.ShouldNotContain(SyntheticRoot.SettingsSecret);
        body.ShouldNotContain(SyntheticRoot.CredentialsSecret);
        body.ShouldNotContain(".credentials");
        body.ShouldNotContain("transcript");
    }

    [Fact]
    public async Task GetOverview_RootWithoutSettingsOrIssues_HasNoOutputStyleAndEmptyLists()
    {
        using var temp = new TempDirectory();
        temp.Write("CLAUDE.md", "# Just one file\n");

        // A CLAUDE.md alone does not make a Claude Code folder (it would be a plain Markdown folder, where it is an
        // orphan note); next to a rules directory it does.
        Directory.CreateDirectory(temp.Resolve("rules"));
        await using var factory = new PusulaFactory(temp.Path);
        using HttpClient client = factory.CreateClient();

        using JsonDocument overview = await client.GetJsonAsync(factory.Api("overview"));
        using JsonDocument tree = await client.GetJsonAsync(factory.Api("tree"));

        overview.RootElement.TryGetProperty("outputStyle", out _).ShouldBeFalse();
        overview.RootElement.GetProperty("fileCount").GetInt32().ShouldBe(1);
        foreach (string list in new[] { "projectMemory", "broken", "pending", "orphans", "frontmatterErrors" })
        {
            overview.RootElement.GetProperty(list).GetArrayLength().ShouldBe(0, list);
        }

        tree.RootElement.GetProperty("nodes").GetArrayLength().ShouldBe(1);
    }

    [Fact]
    public async Task GetOverview_ClaudeFolder_HasNoEntryAndEmptyRecentAndMostLinkedLists()
    {
        using JsonDocument json = await fixture.Client.GetJsonAsync(fixture.Api("overview"));

        json.RootElement.GetProperty("profile").GetString().ShouldBe("Claude");
        json.RootElement.TryGetProperty("entry", out _).ShouldBeFalse();
        json.RootElement.GetProperty("recent").GetArrayLength().ShouldBe(0);
        json.RootElement.GetProperty("mostLinked").GetArrayLength().ShouldBe(0);
    }

    [Fact]
    public async Task GetOverview_Vault_NamesTheEntryNoteTheRecentAndMostLinkedNotesAndOnlyNotesWithoutAnyLinkAreOrphans()
    {
        using var temp = new TempDirectory();
        Directory.CreateDirectory(temp.Resolve(".obsidian"));
        SetTime(temp.Write("Home.md", "---\ntags: [moc]\n---\nStart: [[Plan]] and [[Ideas]].\n"), 2);
        SetTime(temp.Write("Plan.md", "# Plan\nSee [[Ideas]].\n"), 1);
        SetTime(temp.Write("Ideas.md", "# Ideas\n"), 3);
        SetTime(temp.Write("notes/Old.md", "# Old\nBack to [[Plan]].\n"), -1);
        SetTime(temp.Write("notes/Alone.md", "# Alone\nNo links here.\n"), 0);
        await using var factory = new PusulaFactory(temp.Path);
        using HttpClient client = factory.CreateClient();

        using JsonDocument json = await client.GetJsonAsync(factory.Api("overview"));
        JsonElement overview = json.RootElement;

        overview.GetProperty("profile").GetString().ShouldBe("Vault");
        overview.GetProperty("entry").PropertyNames().ShouldBe(["path", "title"], ignoreOrder: true);
        overview.GetProperty("entry").GetProperty("path").GetString().ShouldBe("Home.md");
        overview.GetProperty("entry").GetProperty("title").GetString().ShouldBe("Home");

        JsonElement[] recent = [.. overview.GetProperty("recent").EnumerateArray()];
        recent.Select(note => note.GetProperty("path").GetString()).ShouldBe(["Ideas.md", "Home.md", "Plan.md", "notes/Alone.md", "notes/Old.md"]);
        recent.Select(note => note.GetProperty("modifiedAt").GetDateTimeOffset()).ShouldBe([Day(3), Day(2), Day(1), Day(0), Day(-1)]);
        recent[0].PropertyNames().ShouldBe(["path", "modifiedAt"], ignoreOrder: true);

        // Ideas is linked to by Home and Plan, Plan by Home and Old; the same number goes by path.
        JsonElement[] mostLinked = [.. overview.GetProperty("mostLinked").EnumerateArray()];
        mostLinked.Select(note => $"{note.GetProperty("path").GetString()}:{note.GetProperty("count").GetInt32()}").ShouldBe(["Ideas.md:2", "Plan.md:2"]);
        mostLinked[0].PropertyNames().ShouldBe(["path", "count"], ignoreOrder: true);

        // Home links out and Old does, though nothing links to them: they are not alone.
        overview.GetProperty("orphans").EnumerateArray().Select(orphan => orphan.GetProperty("path").GetString()).ShouldBe(["notes/Alone.md"]);

        static DateTimeOffset Day(int day) => new DateTimeOffset(2026, 9, 30, 12, 0, 0, TimeSpan.Zero).AddDays(day);

        static void SetTime(string path, int day) => File.SetLastWriteTimeUtc(path, Day(day).UtcDateTime);
    }

    [Fact]
    public async Task GetOverviewAndTree_EmptyRoot_AreEmpty()
    {
        using var temp = new TempDirectory();
        await using var factory = new PusulaFactory(temp.Path);
        using HttpClient client = factory.CreateClient();

        using JsonDocument overview = await client.GetJsonAsync(factory.Api("overview"));
        using JsonDocument tree = await client.GetJsonAsync(factory.Api("tree"));

        overview.RootElement.GetProperty("fileCount").GetInt32().ShouldBe(0);
        overview.RootElement.GetProperty("everySessionTokens").GetInt32().ShouldBe(0);
        overview.RootElement.GetProperty("layers").GetArrayLength().ShouldBe(0);
        overview.RootElement.GetProperty("heaviest").GetArrayLength().ShouldBe(0);
        overview.RootElement.GetProperty("recent").GetArrayLength().ShouldBe(0);
        overview.RootElement.GetProperty("mostLinked").GetArrayLength().ShouldBe(0);
        overview.RootElement.TryGetProperty("entry", out _).ShouldBeFalse();
        tree.RootElement.GetProperty("nodes").GetArrayLength().ShouldBe(0);
    }
}
