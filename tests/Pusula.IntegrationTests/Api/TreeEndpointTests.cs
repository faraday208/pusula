using System.Net;
using System.Text.Json;
using Pusula.IntegrationTests.Support;
using Shouldly;
using Xunit;

namespace Pusula.IntegrationTests.Api;

public sealed class TreeEndpointTests(ApiFixture fixture) : IClassFixture<ApiFixture>
{
    private static JsonElement[] Flatten(JsonElement nodes) =>
    [
        .. nodes.EnumerateArray().SelectMany(node =>
            node.GetProperty("type").GetString() == "Directory" ? Flatten(node.GetProperty("children")) : [node]),
    ];

    private static JsonElement Child(JsonElement nodes, string name) =>
        nodes.EnumerateArray().Single(node => node.GetProperty("name").GetString() == name);

    [Fact]
    public async Task GetTree_SyntheticRoot_ReturnsJsonForTheGivenRoot()
    {
        using HttpResponseMessage response = await fixture.Client.GetResponseAsync(fixture.Api("tree"));
        using JsonDocument json = await response.ReadJsonAsync();

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        response.Content.Headers.ContentType?.MediaType.ShouldBe("application/json");
        json.RootElement.PropertyNames().ShouldBe(["root", "version", "builtAt", "nodes"], ignoreOrder: true);
        json.RootElement.GetProperty("root").GetString().ShouldBe(fixture.Root.Path);
        json.RootElement.GetProperty("version").GetInt64().ShouldBe(1);
        json.RootElement.GetProperty("builtAt").GetDateTimeOffset().ShouldBe(DateTimeOffset.UtcNow, tolerance: TimeSpan.FromMinutes(5));
    }

    [Fact]
    public async Task GetTree_TopLevel_ListsDirectoriesFirstThenFilesEachSortedIgnoringCase()
    {
        using JsonDocument json = await fixture.Client.GetJsonAsync(fixture.Api("tree"));
        JsonElement nodes = json.RootElement.GetProperty("nodes");

        nodes.EnumerateArray().Select(node => node.GetProperty("name").GetString()).ShouldBe(
            ["agents", "output-styles", "projects", "rules", "shared", "skills", "CLAUDE.md", "reference.md"]);
        nodes.EnumerateArray().Select(node => node.GetProperty("type").GetString()).ShouldBe(
            ["Directory", "Directory", "Directory", "Directory", "Directory", "Directory", "File", "File"]);
    }

    [Fact]
    public async Task GetTree_ListsExactlyTheIndexedMarkdownFiles()
    {
        using JsonDocument json = await fixture.Client.GetJsonAsync(fixture.Api("tree"));

        string[] paths = [.. Flatten(json.RootElement.GetProperty("nodes")).Select(file => file.GetProperty("path").GetString()!)];

        paths.Order(StringComparer.Ordinal).ShouldBe(SyntheticRoot.IndexedFiles.Order(StringComparer.Ordinal));
    }

    [Fact]
    public async Task GetTree_NeverMentionsSettingsCredentialsHiddenOrRuntimeFiles()
    {
        using HttpResponseMessage response = await fixture.Client.GetResponseAsync(fixture.Api("tree"));
        string body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);

        body.ShouldNotContain("settings.json");
        body.ShouldNotContain(".credentials");
        body.ShouldNotContain("transcript");
        body.ShouldNotContain("plugins");
        body.ShouldNotContain("ignored.md");
        body.ShouldNotContain(".hidden");
        body.ShouldNotContain(SyntheticRoot.SettingsSecret);
        body.ShouldNotContain(SyntheticRoot.CredentialsSecret);
    }

    [Fact]
    public async Task GetTree_FileNode_HasLayerLoadModeTokensBrokenLinksAndOrphanFlagOnly()
    {
        using JsonDocument json = await fixture.Client.GetJsonAsync(fixture.Api("tree"));
        JsonElement claudeMd = Child(json.RootElement.GetProperty("nodes"), "CLAUDE.md");

        claudeMd.PropertyNames().ShouldBe(["type", "name", "path", "tokens", "everySessionTokens", "layer", "loadMode", "brokenLinks", "orphan"], ignoreOrder: true);
        claudeMd.GetProperty("type").GetString().ShouldBe("File");
        claudeMd.GetProperty("path").GetString().ShouldBe("CLAUDE.md");
        claudeMd.GetProperty("layer").GetString().ShouldBe("ClaudeMd");
        claudeMd.GetProperty("loadMode").GetString().ShouldBe("EverySession");
        claudeMd.GetProperty("tokens").GetInt32().ShouldBeGreaterThan(0);
        claudeMd.GetProperty("everySessionTokens").GetInt32().ShouldBe(claudeMd.GetProperty("tokens").GetInt32());
        claudeMd.GetProperty("brokenLinks").GetInt32().ShouldBe(2);
        claudeMd.GetProperty("orphan").GetBoolean().ShouldBeFalse();
    }

    [Fact]
    public async Task GetTree_DirectoryNode_HasFileCountTokensAndChildrenOnly()
    {
        using JsonDocument json = await fixture.Client.GetJsonAsync(fixture.Api("tree"));
        JsonElement skills = Child(json.RootElement.GetProperty("nodes"), "skills");
        JsonElement deploy = Child(skills.GetProperty("children"), "deploy");

        skills.PropertyNames().ShouldBe(["type", "name", "path", "tokens", "everySessionTokens", "fileCount", "children"], ignoreOrder: true);
        skills.GetProperty("type").GetString().ShouldBe("Directory");
        skills.GetProperty("path").GetString().ShouldBe("skills");
        skills.GetProperty("fileCount").GetInt32().ShouldBe(3);
        deploy.GetProperty("path").GetString().ShouldBe("skills/deploy");
        deploy.GetProperty("children").EnumerateArray().Select(node => node.GetProperty("name").GetString()).ShouldBe(["notes.md", "SKILL.md", "unlinked.md"]);
        skills.GetProperty("tokens").GetInt32().ShouldBe(
            deploy.GetProperty("children").EnumerateArray().Sum(node => node.GetProperty("tokens").GetInt32()));
        skills.GetProperty("everySessionTokens").GetInt32().ShouldBe(
            deploy.GetProperty("children").EnumerateArray().Sum(node => node.GetProperty("everySessionTokens").GetInt32()));
    }

    [Fact]
    public async Task GetTree_FilesOfEveryKind_CarryTheirLayerAndLoadMode()
    {
        using JsonDocument json = await fixture.Client.GetJsonAsync(fixture.Api("tree"));
        Dictionary<string, (string Layer, string LoadMode)> byPath = Flatten(json.RootElement.GetProperty("nodes"))
            .ToDictionary(file => file.GetProperty("path").GetString()!, file => (file.GetProperty("layer").GetString()!, file.GetProperty("loadMode").GetString()!));

        byPath["CLAUDE.md"].ShouldBe(("ClaudeMd", "EverySession"));
        byPath["rules/style.md"].ShouldBe(("Rule", "EverySession"));
        byPath["rules/scoped.md"].ShouldBe(("PathRule", "Conditional"));
        byPath["skills/deploy/SKILL.md"].ShouldBe(("Skill", "DescriptionEverySession"));
        byPath["skills/deploy/notes.md"].ShouldBe(("SkillResource", "OnDemand"));
        byPath["agents/reviewer.md"].ShouldBe(("Agent", "DescriptionEverySession"));
        byPath["output-styles/terse.md"].ShouldBe(("OutputStyle", "EverySession"));
        byPath["output-styles/verbose.md"].ShouldBe(("OutputStyle", "Inactive"));
        byPath["projects/demo/memory/MEMORY.md"].ShouldBe(("MemoryIndex", "ProjectSession"));
        byPath["projects/demo/memory/alpha.md"].ShouldBe(("Memory", "OnDemand"));
        byPath["reference.md"].ShouldBe(("Reference", "OnDemand"));
        byPath["shared/shared-note.md"].ShouldBe(("Shared", "OnDemand"));
    }

    [Fact]
    public async Task GetTree_OrphanFlag_IsSetOnlyForUnlinkedFilesOfTheRightKinds()
    {
        using JsonDocument json = await fixture.Client.GetJsonAsync(fixture.Api("tree"));

        string[] orphans =
        [
            .. Flatten(json.RootElement.GetProperty("nodes"))
                .Where(file => file.GetProperty("orphan").GetBoolean())
                .Select(file => file.GetProperty("path").GetString()!),
        ];

        orphans.ShouldBe(
        [
            "projects/demo/memory/lonely.md",
            "reference.md",
            "shared/shared-note.md",
            "skills/deploy/unlinked.md",
        ], ignoreOrder: true);
    }

    [Fact]
    public async Task GetTree_EverySessionTokens_FollowTheLoadModeOfEachFile()
    {
        using JsonDocument json = await fixture.Client.GetJsonAsync(fixture.Api("tree"));
        Dictionary<string, JsonElement> byPath = Flatten(json.RootElement.GetProperty("nodes"))
            .ToDictionary(file => file.GetProperty("path").GetString()!);

        int EverySession(string path) => byPath[path].GetProperty("everySessionTokens").GetInt32();
        int Total(string path) => byPath[path].GetProperty("tokens").GetInt32();

        // Loaded whole every session: everything counts.
        EverySession("CLAUDE.md").ShouldBe(Total("CLAUDE.md"));
        EverySession("rules/style.md").ShouldBe(Total("rules/style.md"));
        EverySession("output-styles/terse.md").ShouldBe(Total("output-styles/terse.md"));
        EverySession("CLAUDE.md").ShouldBeGreaterThan(0);

        // Only the name and the description are loaded: "<name>: <description>", at one token per four characters.
        EverySession("skills/deploy/SKILL.md").ShouldBe(("deploy: Deploys the thing".Length + 3) / 4);
        EverySession("agents/reviewer.md").ShouldBe(("reviewer: Reviews code".Length + 3) / 4);
        EverySession("skills/deploy/SKILL.md").ShouldBeLessThan(Total("skills/deploy/SKILL.md"));

        // Not loaded every session: nothing counts, although the file itself has tokens.
        foreach (string path in new[]
        {
            "rules/scoped.md",
            "output-styles/verbose.md",
            "projects/demo/memory/MEMORY.md",
            "projects/demo/memory/alpha.md",
            "skills/deploy/notes.md",
            "reference.md",
            "shared/shared-note.md",
        })
        {
            EverySession(path).ShouldBe(0, path);
            Total(path).ShouldBeGreaterThan(0, path);
        }
    }

    [Fact]
    public async Task GetTree_EverySessionTokens_AgreeWithTheFileEndpointAndWithEachDirectorySum()
    {
        using JsonDocument tree = await fixture.Client.GetJsonAsync(fixture.Api("tree"));

        async Task<int> Check(JsonElement node)
        {
            if (node.GetProperty("type").GetString() == "File")
            {
                using JsonDocument file = await fixture.Client.GetJsonAsync(fixture.Api($"file?path={Uri.EscapeDataString(node.GetProperty("path").GetString()!)}"));
                JsonElement tokens = file.RootElement.GetProperty("tokens");
                node.GetProperty("tokens").GetInt32().ShouldBe(tokens.GetProperty("total").GetInt32());
                node.GetProperty("everySessionTokens").GetInt32().ShouldBe(tokens.GetProperty("everySession").GetInt32());
                return node.GetProperty("everySessionTokens").GetInt32();
            }

            int sum = 0;
            foreach (JsonElement child in node.GetProperty("children").EnumerateArray())
            {
                sum += await Check(child);
            }

            node.GetProperty("everySessionTokens").GetInt32().ShouldBe(sum, node.GetProperty("path").GetString());
            return sum;
        }

        int total = 0;
        foreach (JsonElement node in tree.RootElement.GetProperty("nodes").EnumerateArray())
        {
            total += await Check(node);
        }

        // The tree adds up to the number the overview reports for every session.
        using JsonDocument overview = await fixture.Client.GetJsonAsync(fixture.Api("overview"));
        total.ShouldBeGreaterThan(0);
        total.ShouldBe(overview.RootElement.GetProperty("everySessionTokens").GetInt32());
    }
}
