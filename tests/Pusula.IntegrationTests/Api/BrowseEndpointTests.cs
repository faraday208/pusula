using System.Net;
using System.Text.Json;
using Pusula.Browse;
using Pusula.IntegrationTests.Support;
using Shouldly;
using Xunit;

namespace Pusula.IntegrationTests.Api;

/// <summary>
/// The folder browser: <c>GET /api/browse</c> and <c>GET /api/browse/found</c>, who may use them, and what they say. The
/// server runs on made-up directories (a home directory in a scratch directory, no drive folders); who is calling is
/// simulated (see <see cref="SimulatedConnection"/>): by default a browser on the machine the server runs on.
/// </summary>
public sealed class BrowseEndpointTests
{
    private static readonly string[] Endpoints = ["browse", "found"];

    private static readonly (string Name, string Value)[] AnotherMachine = EditingApp.From(EditingApp.AnotherMachine, EditingApp.ThisMachine);

    private static Task<HttpResponseMessage> GetAsync(EditingApp app, string endpoint, (string Name, string Value)[]? headers = null) =>
        endpoint == "browse" ? app.GetBrowseAsync(headers: headers) : app.GetFoundAsync(headers);

    private static async Task ShouldBeProblemAsync(HttpResponseMessage response, HttpStatusCode status, string code)
    {
        using JsonDocument json = await response.ReadJsonAsync();

        response.StatusCode.ShouldBe(status);
        response.Content.Headers.ContentType?.MediaType.ShouldBe("application/problem+json");
        json.RootElement.GetProperty("status").GetInt32().ShouldBe((int)status);
        json.RootElement.GetProperty("code").GetString().ShouldBe(code);
        json.RootElement.GetProperty("title").GetString().ShouldNotBeNullOrWhiteSpace();
        json.RootElement.GetProperty("detail").GetString().ShouldNotBeNullOrWhiteSpace();
    }

    private static string[] Names(JsonElement folders) => [.. folders.EnumerateArray().Select(folder => folder.GetProperty("name").GetString()!)];

    private static JsonElement Folder(JsonElement folders, string name) =>
        folders.EnumerateArray().Single(folder => folder.GetProperty("name").GetString() == name);

    private static string Tilde(string rest) => "~" + Path.DirectorySeparatorChar + rest.Replace('/', Path.DirectorySeparatorChar);

    private static string[] ListedNames(JsonElement folders) =>
        [.. folders.EnumerateArray().Where(folder => folder.GetProperty("listed").GetBoolean()).Select(folder => folder.GetProperty("name").GetString()!)];

    // ---- Who may ask --------------------------------------------------------------------------------------------

    [Fact]
    public async Task Get_FromTheMachineTheServerRunsOn_Returns200()
    {
        await using var app = new EditingApp();
        await app.StartAsync();

        foreach (string endpoint in Endpoints)
        {
            using HttpResponseMessage response = await GetAsync(app, endpoint);

            response.StatusCode.ShouldBe(HttpStatusCode.OK, endpoint);
            response.Content.Headers.ContentType?.MediaType.ShouldBe("application/json", endpoint);
        }
    }

    [Theory]
    [InlineData("203.0.113.9", "192.0.2.10")]
    [InlineData("100.64.0.2", "100.64.0.1")]
    [InlineData("::ffff:203.0.113.9", "192.0.2.10")]
    [InlineData("fd7a:115c:a1e0::2", "fd7a:115c:a1e0::1")]
    [InlineData("203.0.113.9", "127.0.0.1")]
    [InlineData("none", "none")]
    [InlineData("none", "127.0.0.1")]
    public async Task Get_FromAnotherMachine_Returns403Remote(string remote, string local)
    {
        await using var app = new EditingApp();
        await app.StartAsync();

        foreach (string endpoint in Endpoints)
        {
            using HttpResponseMessage response = await GetAsync(app, endpoint, EditingApp.From(remote, local));

            await ShouldBeProblemAsync(response, HttpStatusCode.Forbidden, "Remote");
        }
    }

    [Theory]
    [InlineData("192.0.2.10", "192.0.2.10")]
    [InlineData("::1", "::1")]
    [InlineData("::ffff:192.0.2.10", "192.0.2.10")]
    [InlineData("127.0.0.1", "192.0.2.10")]
    public async Task Get_FromTheMachinesOwnAddress_Returns200(string remote, string local)
    {
        await using var app = new EditingApp();
        await app.StartAsync();

        foreach (string endpoint in Endpoints)
        {
            using HttpResponseMessage response = await GetAsync(app, endpoint, EditingApp.From(remote, local));

            response.StatusCode.ShouldBe(HttpStatusCode.OK, endpoint);
        }
    }

    // A reverse proxy on this machine: the connection is from the loopback address, the headers say it was forwarded.
    [Theory]
    [InlineData("Forwarded", "for=203.0.113.9;proto=https")]
    [InlineData("X-Forwarded-For", "203.0.113.9")]
    [InlineData("X-Forwarded-Host", "pusula.example")]
    [InlineData("X-Real-IP", "203.0.113.9")]
    public async Task Get_ThroughAReverseProxyOnThisMachine_Returns403Remote(string header, string value)
    {
        await using var app = new EditingApp();
        await app.StartAsync();

        foreach (string endpoint in Endpoints)
        {
            using HttpResponseMessage proxied = await GetAsync(app, endpoint, [(header, value)]);
            using HttpResponseMessage direct = await GetAsync(app, endpoint);

            await ShouldBeProblemAsync(proxied, HttpStatusCode.Forbidden, "Remote");
            direct.StatusCode.ShouldBe(HttpStatusCode.OK, endpoint);
        }
    }

    [Fact]
    public async Task Get_FromAnotherMachineWithABadPath_IsRefusedBeforeAnythingAboutItIsSaid()
    {
        await using var app = new EditingApp();
        await app.StartAsync();

        foreach (string path in new[] { "relative", "/no/such/folder", "~other", "/tmp/a\0b" })
        {
            using HttpResponseMessage response = await app.GetBrowseAsync(path, headers: AnotherMachine);

            await ShouldBeProblemAsync(response, HttpStatusCode.Forbidden, "Remote");
        }
    }

    [Fact]
    public async Task Get_RemoteEditAllowed_FromAnotherMachineOrThroughAProxy_Returns200()
    {
        await using var app = new EditingApp { AllowRemoteEdit = true };
        await app.StartAsync();

        foreach (string endpoint in Endpoints)
        {
            using HttpResponseMessage remote = await GetAsync(app, endpoint, AnotherMachine);
            using HttpResponseMessage noAddress = await GetAsync(app, endpoint, EditingApp.From("none", "none"));
            using HttpResponseMessage proxied = await GetAsync(app, endpoint, [.. AnotherMachine, ("X-Forwarded-For", "203.0.113.9")]);

            remote.StatusCode.ShouldBe(HttpStatusCode.OK, endpoint);
            noAddress.StatusCode.ShouldBe(HttpStatusCode.OK, endpoint);
            proxied.StatusCode.ShouldBe(HttpStatusCode.OK, endpoint);
        }
    }

    [Fact]
    public async Task Get_RemoteEditNotAllowed_IsTheDefault_AndAnotherMachineIsRefused()
    {
        await using var app = new EditingApp { AllowRemoteEdit = false };
        await app.StartAsync();

        using HttpResponseMessage response = await app.GetBrowseAsync(headers: AnotherMachine);

        await ShouldBeProblemAsync(response, HttpStatusCode.Forbidden, "Remote");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Get_FoldersGivenOutsideASourcesFile_Returns403CommandLineWhoeverAsks(bool allowRemoteEdit)
    {
        await using var app = new EditingApp { AllowRemoteEdit = allowRemoteEdit };
        app.Root = app.Folder("given", "G.md");
        await app.StartAsync();

        foreach (string endpoint in Endpoints)
        {
            using HttpResponseMessage local = await GetAsync(app, endpoint);
            using HttpResponseMessage remote = await GetAsync(app, endpoint, AnotherMachine);

            await ShouldBeProblemAsync(local, HttpStatusCode.Forbidden, "CommandLine");
            await ShouldBeProblemAsync(remote, HttpStatusCode.Forbidden, allowRemoteEdit ? "CommandLine" : "Remote");
        }
    }

    [Theory]
    [InlineData("Origin", "http://evil.example")]
    [InlineData("Origin", "http://localhost:5190")]
    [InlineData("Origin", "https://localhost")]
    [InlineData("Origin", "http://127.0.0.1")]
    [InlineData("Origin", "null")]
    [InlineData("Sec-Fetch-Site", "cross-site")]
    [InlineData("Sec-Fetch-Site", "same-site")]
    public async Task Get_FromAnotherPageInTheBrowserOnThisMachine_Returns403CrossOrigin(string header, string value)
    {
        await using var app = new EditingApp();
        await app.StartAsync();

        foreach (string endpoint in Endpoints)
        {
            using HttpResponseMessage response = await GetAsync(app, endpoint, [(header, value)]);

            await ShouldBeProblemAsync(response, HttpStatusCode.Forbidden, "CrossOrigin");
        }
    }

    [Fact]
    public async Task Get_RemoteEditAllowed_FromAnotherPage_StillReturns403CrossOrigin()
    {
        await using var app = new EditingApp { AllowRemoteEdit = true };
        await app.StartAsync();

        foreach (string endpoint in Endpoints)
        {
            using HttpResponseMessage response = await GetAsync(app, endpoint, [.. AnotherMachine, ("Origin", "http://evil.example")]);

            await ShouldBeProblemAsync(response, HttpStatusCode.Forbidden, "CrossOrigin");
        }
    }

    [Fact]
    public async Task Get_FromAPageOfThisServer_Returns200()
    {
        await using var app = new EditingApp();
        await app.StartAsync();

        foreach (string endpoint in Endpoints)
        {
            using HttpResponseMessage response = await GetAsync(app, endpoint, [("Origin", "http://localhost"), ("Sec-Fetch-Site", "same-origin")]);

            response.StatusCode.ShouldBe(HttpStatusCode.OK, endpoint);
        }
    }

    // The address typed into the browser: it only reads, so the user may open it by hand. Changing the sources may not be done so.
    [Fact]
    public async Task Get_RequestTheUserMadeByHand_Returns200ButChangingTheSourcesIsStillRefused()
    {
        await using var app = new EditingApp();
        await app.StartAsync();
        (string Name, string Value)[] byHand = [("Sec-Fetch-Site", "none")];

        foreach (string endpoint in Endpoints)
        {
            using HttpResponseMessage response = await GetAsync(app, endpoint, byHand);

            response.StatusCode.ShouldBe(HttpStatusCode.OK, endpoint);
        }

        using HttpResponseMessage post = await app.PostAsync(app.Folder("other", "O.md"), headers: byHand);
        await ShouldBeProblemAsync(post, HttpStatusCode.Forbidden, "CrossOrigin");
    }

    [Fact]
    public async Task Get_ByHandWithAnotherOrigin_IsStillRefused()
    {
        await using var app = new EditingApp();
        await app.StartAsync();

        using HttpResponseMessage response = await app.GetBrowseAsync(headers: [("Sec-Fetch-Site", "none"), ("Origin", "http://evil.example")]);

        await ShouldBeProblemAsync(response, HttpStatusCode.Forbidden, "CrossOrigin");
    }

    [Fact]
    public async Task Get_EveryRefusal_ComesInTheOrderRemoteCommandLineCrossOrigin()
    {
        await using var app = new EditingApp();
        app.Root = app.Folder("given", "G.md");
        await app.StartAsync();
        (string Name, string Value)[] foreign = [("Origin", "http://evil.example")];

        using HttpResponseMessage remote = await app.GetBrowseAsync(headers: [.. AnotherMachine, .. foreign]);
        using HttpResponseMessage commandLine = await app.GetBrowseAsync(headers: foreign);

        await ShouldBeProblemAsync(remote, HttpStatusCode.Forbidden, "Remote");
        await ShouldBeProblemAsync(commandLine, HttpStatusCode.Forbidden, "CommandLine");
    }

    [Theory]
    [InlineData("evil.example")]
    [InlineData("evil.example:5190")]
    [InlineData("rebind.attacker.test")]
    public async Task Get_ToAHostThatIsNotThisServers_Returns400(string host)
    {
        await using var app = new EditingApp { AllowRemoteEdit = true };
        await app.StartAsync();

        foreach (string endpoint in Endpoints)
        {
            using HttpResponseMessage response = await GetAsync(app, endpoint, [.. AnotherMachine, ("Host", host)]);

            response.StatusCode.ShouldBe(HttpStatusCode.BadRequest, endpoint);
        }
    }

    // What only reads must not write: neither the folders it lists nor the sources file.
    [Fact]
    public async Task Get_ChangesNothing()
    {
        await using var app = new EditingApp();
        string notes = app.HomeFolder("Notes", "A.md");
        app.WriteFile("home/Vault/.obsidian/app.json", "{}");
        app.WriteSourcesFile($$"""{ "sources": [ { "id": "notes", "path": {{JsonSerializer.Serialize(notes)}} } ] }""");
        await app.StartAsync();
        byte[] before = File.ReadAllBytes(app.SourcesFile);
        string[] files = [.. Directory.EnumerateFileSystemEntries(app.Home, "*", SearchOption.AllDirectories).Order(StringComparer.Ordinal)];

        (await app.GetBrowseAsync()).Dispose();
        (await app.GetBrowseAsync(hidden: true)).Dispose();
        (await app.GetFoundAsync()).Dispose();

        File.ReadAllBytes(app.SourcesFile).ShouldBe(before);
        Directory.EnumerateFileSystemEntries(app.Home, "*", SearchOption.AllDirectories).Order(StringComparer.Ordinal).ShouldBe(files);
    }

    // ---- GET /api/browse ----------------------------------------------------------------------------------------

    [Fact]
    public async Task GetBrowse_NoPath_ListsTheHomeDirectoryWithTheFieldsOfTheContract()
    {
        await using var app = new EditingApp();
        string documents = app.HomeFolder("Documents", "Plan.md", "Ideas.md");
        app.HomeFolder("Music");
        await app.StartAsync();

        using HttpResponseMessage response = await app.GetBrowseAsync();
        using JsonDocument json = await response.ReadJsonAsync();
        JsonElement root = json.RootElement;
        JsonElement folders = root.GetProperty("folders");

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        response.Content.Headers.ContentType?.MediaType.ShouldBe("application/json");
        root.PropertyNames().ShouldBe(["path", "display", "home", "folders", "truncated", "parent"], ignoreOrder: true);
        root.GetProperty("path").GetString().ShouldBe(app.Home);
        root.GetProperty("display").GetString().ShouldBe("~");
        root.GetProperty("home").GetString().ShouldBe(app.Home);
        root.GetProperty("parent").GetString().ShouldBe(Path.GetDirectoryName(app.Home));
        root.GetProperty("truncated").GetBoolean().ShouldBeFalse();

        // .claude is the home directory's own and always listed; it is the source that is shown while there is no sources file.
        Names(folders).ShouldBe([".claude", "Documents", "Music"]);
        JsonElement claude = Folder(folders, ".claude");
        claude.PropertyNames().ShouldBe(["name", "path", "display", "listed", "kind", "markdownCount"], ignoreOrder: true);
        claude.GetProperty("kind").GetString().ShouldBe("Claude");
        claude.GetProperty("listed").GetBoolean().ShouldBeTrue();
        claude.GetProperty("markdownCount").GetInt32().ShouldBe(1);

        JsonElement documentsItem = Folder(folders, "Documents");
        documentsItem.PropertyNames().ShouldBe(["name", "path", "display", "listed", "markdownCount"], ignoreOrder: true);
        documentsItem.GetProperty("path").GetString().ShouldBe(documents);
        documentsItem.GetProperty("display").GetString().ShouldBe(Tilde("Documents"));
        documentsItem.GetProperty("listed").GetBoolean().ShouldBeFalse();
        documentsItem.GetProperty("markdownCount").GetInt32().ShouldBe(2);
        Folder(folders, "Music").GetProperty("markdownCount").GetInt32().ShouldBe(0);
    }

    [Fact]
    public async Task GetBrowse_PathInEveryForm_NamesTheSameFolder()
    {
        await using var app = new EditingApp();
        string notes = app.HomeFolder("Documents/notes", "N.md");
        app.HomeFolder("Documents/notes/sub");
        await app.StartAsync();

        foreach (string typed in new[] { notes, notes + Path.DirectorySeparatorChar, "~/Documents/notes", "~/Documents/notes/", Path.Join(app.Home, "no-such", "..", "Documents", "notes") })
        {
            using HttpResponseMessage response = await app.GetBrowseAsync(typed);
            using JsonDocument json = await response.ReadJsonAsync();

            response.StatusCode.ShouldBe(HttpStatusCode.OK, typed);
            json.RootElement.GetProperty("path").GetString().ShouldBe(notes, typed);
            json.RootElement.GetProperty("display").GetString().ShouldBe(Tilde("Documents/notes"), typed);
            json.RootElement.GetProperty("parent").GetString().ShouldBe(Path.Join(app.Home, "Documents"), typed);
            Names(json.RootElement.GetProperty("folders")).ShouldBe(["sub"], typed);
        }

        using HttpResponseMessage tilde = await app.GetBrowseAsync("~");
        using JsonDocument home = await tilde.ReadJsonAsync();
        home.RootElement.GetProperty("path").GetString().ShouldBe(app.Home);
    }

    [Fact]
    public async Task GetBrowse_FolderOutsideTheHome_HasTheFullPathAsItsDisplay()
    {
        await using var app = new EditingApp();
        string outside = app.Folder("outside", "O.md");
        app.Folder("outside/inner");
        await app.StartAsync();

        using HttpResponseMessage response = await app.GetBrowseAsync(outside);
        using JsonDocument json = await response.ReadJsonAsync();

        json.RootElement.GetProperty("display").GetString().ShouldBe(outside);
        json.RootElement.GetProperty("home").GetString().ShouldBe(app.Home);
        Folder(json.RootElement.GetProperty("folders"), "inner").GetProperty("display").GetString().ShouldBe(Path.Join(outside, "inner"));
    }

    // The root of the file system has no parent: the property is left out. The limits keep this from reading anything of the machine but a few names.
    [Fact]
    public async Task GetBrowse_RootOfTheFileSystem_HasNoParent()
    {
        await using var app = new EditingApp { BrowseLimits = BrowseLimits.Default with { MaxFolders = 1, CountEntries = 0 } };
        await app.StartAsync();
        string root = Path.GetPathRoot(app.Home)!;

        using HttpResponseMessage response = await app.GetBrowseAsync(root);
        using JsonDocument json = await response.ReadJsonAsync();

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        json.RootElement.GetProperty("path").GetString().ShouldBe(root);
        json.RootElement.TryGetProperty("parent", out _).ShouldBeFalse();
        json.RootElement.GetProperty("truncated").GetBoolean().ShouldBeTrue();
    }

    [Fact]
    public async Task GetBrowse_FolderWithNoFolders_IsAnEmptyListAndNotTruncated()
    {
        await using var app = new EditingApp();
        app.HomeFolder("Empty");
        app.WriteFile("home/Empty/only-a-file.txt", "x");
        await app.StartAsync();

        using HttpResponseMessage response = await app.GetBrowseAsync(Path.Join(app.Home, "Empty"));
        using JsonDocument json = await response.ReadJsonAsync();

        json.RootElement.GetProperty("folders").GetArrayLength().ShouldBe(0);
        json.RootElement.GetProperty("truncated").GetBoolean().ShouldBeFalse();
    }

    [Theory]
    [InlineData("relative", HttpStatusCode.BadRequest, "PathNotAbsolute")]
    [InlineData("./notes", HttpStatusCode.BadRequest, "PathNotAbsolute")]
    [InlineData("~other/notes", HttpStatusCode.BadRequest, "PathNotAbsolute")]
    [InlineData("null-in-the-path", HttpStatusCode.BadRequest, "PathNotAbsolute")]
    [InlineData("only-a-null", HttpStatusCode.BadRequest, "PathNotAbsolute")]
    [InlineData("~/pusula-test-no-such-folder", HttpStatusCode.NotFound, "FolderNotFound")]
    [InlineData("a-file", HttpStatusCode.NotFound, "FolderNotFound")]
    [InlineData("missing", HttpStatusCode.NotFound, "FolderNotFound")]
    public async Task GetBrowse_PathThatCannotBeListed_ReturnsProblemDetailsThatNameTheCode(string kind, HttpStatusCode status, string code)
    {
        await using var app = new EditingApp();
        string file = app.WriteFile("home/a-file.md", "x");
        await app.StartAsync();
        string path = kind switch
        {
            "a-file" => file,
            "missing" => app.Missing("nope"),
            "null-in-the-path" => "/tmp/a\0b",
            "only-a-null" => "\0",
            _ => kind,
        };

        using HttpResponseMessage response = await app.GetBrowseAsync(path);

        await ShouldBeProblemAsync(response, status, code);
    }

    [Fact]
    public async Task GetBrowse_HiddenFolders_AreLeftOutUnlessAskedFor()
    {
        await using var app = new EditingApp();
        app.HomeFolder("Visible");
        app.WriteFile("home/.ssh/config", "x");
        app.WriteFile("home/project/.git/HEAD", "x");
        app.WriteFile("home/project/.claude/CLAUDE.md", "x");
        app.WriteFile("home/project/src/a.md", "x");
        await app.StartAsync();
        string project = Path.Join(app.Home, "project");

        async Task<string[]> ListAsync(string? path, string? hidden)
        {
            var query = new List<string>();
            if (path is not null)
            {
                query.Add("path=" + Uri.EscapeDataString(path));
            }

            if (hidden is not null)
            {
                query.Add("hidden=" + hidden);
            }

            using HttpResponseMessage response = await app.SendAsync(HttpMethod.Get, "/api/browse?" + string.Join('&', query));
            using JsonDocument json = await response.ReadJsonAsync();
            response.StatusCode.ShouldBe(HttpStatusCode.OK);
            return Names(json.RootElement.GetProperty("folders"));
        }

        // The .claude of the home directory is always there; the others are not, unless asked for.
        (await ListAsync(null, null)).ShouldBe([".claude", "project", "Visible"]);
        (await ListAsync(null, "1")).ShouldBe([".claude", ".ssh", "project", "Visible"]);
        (await ListAsync(null, "true")).ShouldBe([".claude", ".ssh", "project", "Visible"]);
        (await ListAsync(null, "0")).ShouldBe([".claude", "project", "Visible"]);
        (await ListAsync(null, "yes")).ShouldBe([".claude", "project", "Visible"]);
        (await ListAsync(null, string.Empty)).ShouldBe([".claude", "project", "Visible"]);

        // Anywhere else, .claude is hidden like the rest.
        (await ListAsync(project, null)).ShouldBe(["src"]);
        (await ListAsync(project, "1")).ShouldBe([".claude", ".git", "src"]);
    }

    [Fact]
    public async Task GetBrowse_NodeModules_IsNeverListed()
    {
        await using var app = new EditingApp();
        app.WriteFile("home/node_modules/pkg/index.js", "x");
        app.WriteFile("home/project/node_modules/pkg/index.js", "x");
        app.HomeFolder("project/src");
        await app.StartAsync();

        using HttpResponseMessage home = await app.GetBrowseAsync(hidden: true);
        using HttpResponseMessage project = await app.GetBrowseAsync(Path.Join(app.Home, "project"), hidden: true);
        using JsonDocument homeJson = await home.ReadJsonAsync();
        using JsonDocument projectJson = await project.ReadJsonAsync();

        Names(homeJson.RootElement.GetProperty("folders")).ShouldBe([".claude", "project"]);
        Names(projectJson.RootElement.GetProperty("folders")).ShouldBe(["src"]);
    }

    [Fact]
    public async Task GetBrowse_Folders_AreSortedByTheTurkishAlphabetIgnoringCase()
    {
        await using var app = new EditingApp();
        foreach (string name in new[] { "Zeytin", "Çiçek", "Şeker", "ankara", "Bursa", "İzmir", "ığdır" })
        {
            app.HomeFolder(name);
        }

        await app.StartAsync();

        using HttpResponseMessage response = await app.GetBrowseAsync();
        using JsonDocument json = await response.ReadJsonAsync();

        Names(json.RootElement.GetProperty("folders")).ShouldBe([".claude", "ankara", "Bursa", "Çiçek", "ığdır", "İzmir", "Şeker", "Zeytin"]);
    }

    [Fact]
    public async Task GetBrowse_Kind_IsVaultOrClaudeAndLeftOutForTheRest()
    {
        await using var app = new EditingApp();
        app.WriteFile("home/Vault/.obsidian/app.json", "{}");
        app.WriteFile("home/Vault/Home.md", "x");
        app.WriteFile("home/Config/CLAUDE.md", "x");
        app.WriteFile("home/Config/rules/style.md", "x");
        app.HomeFolder("Plain", "P.md");
        await app.StartAsync();

        using HttpResponseMessage response = await app.GetBrowseAsync();
        using JsonDocument json = await response.ReadJsonAsync();
        JsonElement folders = json.RootElement.GetProperty("folders");

        Folder(folders, "Vault").GetProperty("kind").GetString().ShouldBe("Vault");
        Folder(folders, "Config").GetProperty("kind").GetString().ShouldBe("Claude");
        Folder(folders, ".claude").GetProperty("kind").GetString().ShouldBe("Claude");
        Folder(folders, "Plain").TryGetProperty("kind", out _).ShouldBeFalse();
    }

    [Fact]
    public async Task GetBrowse_MarkdownCount_CountsAtAnyDepthAndSaysWhenThereAreMore()
    {
        await using var app = new EditingApp { BrowseLimits = BrowseLimits.Default with { MaxMarkdownFiles = 3 } };
        app.HomeFolder("Few", "a.md");
        app.WriteFile("home/Few/deep/er/b.md", "x");
        app.WriteFile("home/Few/node_modules/c.md", "x");
        app.WriteFile("home/Few/.hidden/d.md", "x");
        app.HomeFolder("Exact", "1.md", "2.md", "3.md");
        app.HomeFolder("Many", "1.md", "2.md", "3.md", "4.md", "5.md");
        await app.StartAsync();

        using HttpResponseMessage response = await app.GetBrowseAsync();
        using JsonDocument json = await response.ReadJsonAsync();
        JsonElement folders = json.RootElement.GetProperty("folders");

        Folder(folders, "Few").GetProperty("markdownCount").GetInt32().ShouldBe(2);
        Folder(folders, "Few").TryGetProperty("more", out _).ShouldBeFalse();
        Folder(folders, "Exact").GetProperty("markdownCount").GetInt32().ShouldBe(3);
        Folder(folders, "Exact").TryGetProperty("more", out _).ShouldBeFalse();
        Folder(folders, "Many").GetProperty("markdownCount").GetInt32().ShouldBe(3);
        Folder(folders, "Many").GetProperty("more").GetBoolean().ShouldBeTrue();
        Folder(folders, "Many").PropertyNames().ShouldBe(["name", "path", "display", "listed", "markdownCount", "more"], ignoreOrder: true);
    }

    [Fact]
    public async Task GetBrowse_MarkdownCountThatTheBudgetDoesNotReach_IsLeftOutWhileTheKindIsNot()
    {
        await using var app = new EditingApp { BrowseLimits = BrowseLimits.Default with { CountEntries = 3 } };
        app.HomeFolder("a", "1.md", "2.md");
        app.WriteFile("home/b/.obsidian/app.json", "{}");
        app.WriteFile("home/b/2.md", "x");
        app.HomeFolder("c", "3.md");
        await app.StartAsync();

        using HttpResponseMessage response = await app.GetBrowseAsync();
        using JsonDocument json = await response.ReadJsonAsync();
        JsonElement folders = json.RootElement.GetProperty("folders");

        // Three entries are all the budget has, in the order of the list: CLAUDE.md of .claude, then 1.md and 2.md of a. b is where it runs out.
        Folder(folders, ".claude").GetProperty("markdownCount").GetInt32().ShouldBe(1);
        Folder(folders, "a").GetProperty("markdownCount").GetInt32().ShouldBe(2);
        Folder(folders, "b").TryGetProperty("markdownCount", out _).ShouldBeFalse();
        Folder(folders, "b").GetProperty("kind").GetString().ShouldBe("Vault");
        Folder(folders, "c").TryGetProperty("markdownCount", out _).ShouldBeFalse();
    }

    [Fact]
    public async Task GetBrowse_FolderOfMoreEntriesThanOneFolderMayTake_HasWhatWasCountedAndMoreWhileTheOthersKeepTheirCounts()
    {
        await using var app = new EditingApp { BrowseLimits = BrowseLimits.Default with { CountEntriesPerFolder = 3 } };
        for (int i = 0; i < 6; i++)
        {
            app.WriteFile($"home/a-big/{i}.txt", "x");
        }

        app.HomeFolder("b-small", "1.md");
        app.HomeFolder("c-small", "1.md", "2.md");
        await app.StartAsync();

        using HttpResponseMessage response = await app.GetBrowseAsync();
        using JsonDocument json = await response.ReadJsonAsync();
        JsonElement folders = json.RootElement.GetProperty("folders");

        // Nothing was counted in a-big before the cap: "0 and more" is what is known. The folders after it are counted all the same.
        Folder(folders, "a-big").GetProperty("markdownCount").GetInt32().ShouldBe(0);
        Folder(folders, "a-big").GetProperty("more").GetBoolean().ShouldBeTrue();
        Folder(folders, "b-small").GetProperty("markdownCount").GetInt32().ShouldBe(1);
        Folder(folders, "b-small").TryGetProperty("more", out _).ShouldBeFalse();
        Folder(folders, "c-small").GetProperty("markdownCount").GetInt32().ShouldBe(2);
        Folder(folders, ".claude").GetProperty("markdownCount").GetInt32().ShouldBe(1);
    }

    // The number next to a Claude Code folder is the file count of the source over it, which the list of sources has too.
    [Fact]
    public async Task GetBrowse_ClaudeCodeFolder_HasTheFileCountOfItsSource()
    {
        await using var app = new EditingApp();
        app.WriteFile("home/.claude/rules/style.md", "x");
        app.WriteFile("home/.claude/plugins/some-plugin/README.md", "x");
        app.WriteFile("home/.claude/sessions/log.md", "x");
        app.WriteFile("home/.claude/projects/demo/memory/MEMORY.md", "x");
        app.WriteFile("home/.claude/projects/demo/transcript.md", "x");
        app.WriteFile("home/Vault/.obsidian/app.json", "{}");
        app.WriteFile("home/Vault/Home.md", "x");
        app.WriteFile("home/Vault/plugins/some-plugin/README.md", "x");
        await app.StartAsync();

        using HttpResponseMessage response = await app.GetBrowseAsync();
        using JsonDocument json = await response.ReadJsonAsync();
        using JsonDocument sources = await app.GetSourcesAsync();
        JsonElement folders = json.RootElement.GetProperty("folders");
        int fileCount = sources.RootElement.GetProperty("sources")[0].GetProperty("fileCount").GetInt32();

        // CLAUDE.md, rules/style.md and the memory of the project: the runtime folders and the transcript are not what a source reads.
        fileCount.ShouldBe(3);
        Folder(folders, ".claude").GetProperty("kind").GetString().ShouldBe("Claude");
        Folder(folders, ".claude").GetProperty("markdownCount").GetInt32().ShouldBe(fileCount);

        // A vault is read in full: its plugins folder is no runtime folder.
        Folder(folders, "Vault").GetProperty("markdownCount").GetInt32().ShouldBe(2);
    }

    [Fact]
    public async Task GetBrowse_NoTimeForTheCounts_StillListsEveryFolder()
    {
        await using var app = new EditingApp { BrowseLimits = BrowseLimits.Default with { CountTime = TimeSpan.Zero } };
        app.HomeFolder("a", "1.md");
        app.HomeFolder("b", "2.md");
        await app.StartAsync();

        using HttpResponseMessage response = await app.GetBrowseAsync();
        using JsonDocument json = await response.ReadJsonAsync();

        Names(json.RootElement.GetProperty("folders")).ShouldBe([".claude", "a", "b"]);
        json.RootElement.GetProperty("folders").EnumerateArray().Select(folder => folder.TryGetProperty("markdownCount", out _)).ShouldAllBe(hasCount => !hasCount);
    }

    [Fact]
    public async Task GetBrowse_MoreFoldersThanTheLimit_ListsTheFirstOnesAndSaysTruncated()
    {
        await using var app = new EditingApp { BrowseLimits = BrowseLimits.Default with { MaxFolders = 2 } };
        app.HomeFolder("c");
        app.HomeFolder("b");
        app.HomeFolder("a");
        await app.StartAsync();

        using HttpResponseMessage response = await app.GetBrowseAsync();
        using JsonDocument json = await response.ReadJsonAsync();

        Names(json.RootElement.GetProperty("folders")).ShouldBe([".claude", "a"]);
        json.RootElement.GetProperty("truncated").GetBoolean().ShouldBeTrue();
    }

    [Fact]
    public async Task GetBrowse_LimitOfTheApplication_Is500Folders()
    {
        await using var app = new EditingApp { BrowseLimits = BrowseLimits.Default with { CountEntries = 0 } };
        for (int i = 0; i < 501; i++)
        {
            app.HomeFolder($"d{i:D3}");
        }

        await app.StartAsync();

        using HttpResponseMessage response = await app.GetBrowseAsync();
        using JsonDocument json = await response.ReadJsonAsync();

        json.RootElement.GetProperty("folders").GetArrayLength().ShouldBe(500);
        json.RootElement.GetProperty("truncated").GetBoolean().ShouldBeTrue();
    }

    [Fact]
    public async Task GetBrowse_FolderThatASourceShows_IsListedUntilTheSourceIsRemoved()
    {
        await using var app = new EditingApp();
        string notes = app.HomeFolder("Notes", "A.md");
        app.HomeFolder("Notes/sub");
        app.HomeFolder("Other");
        await app.StartAsync();

        async Task<string[]> ListedAsync(string? path = null)
        {
            using HttpResponseMessage response = await app.GetBrowseAsync(path);
            using JsonDocument json = await response.ReadJsonAsync();
            return ListedNames(json.RootElement.GetProperty("folders"));
        }

        (await ListedAsync()).ShouldBe([".claude"]);

        using HttpResponseMessage added = await app.PostAsync(notes);
        added.StatusCode.ShouldBe(HttpStatusCode.Created);

        // The source is the folder itself: the folders inside it and beside it are not listed.
        (await ListedAsync()).ShouldBe([".claude", "Notes"]);
        (await ListedAsync(notes)).ShouldBeEmpty();

        using HttpResponseMessage removed = await app.DeleteAsync("notes");
        removed.StatusCode.ShouldBe(HttpStatusCode.NoContent);

        (await ListedAsync()).ShouldBe([".claude"]);
    }

    [Fact]
    public async Task GetBrowse_NamesAndContentOfFiles_AreInNoAnswer()
    {
        await using var app = new EditingApp();
        app.WriteFile("home/secret-plan.txt", "TOP-SECRET-CONTENT");
        app.WriteFile("home/Documents/diary.md", "TOP-SECRET-NOTE");
        app.WriteFile("home/Documents/passwords.txt", "TOP-SECRET-PASSWORD");
        app.WriteFile("home/Vault/.obsidian/app.json", "TOP-SECRET-SETTING");
        app.WriteFile("home/Vault/Private notes.md", "TOP-SECRET-NOTE");
        await app.StartAsync();

        string[] answers =
        [
            await ReadAsync(app.GetBrowseAsync()),
            await ReadAsync(app.GetBrowseAsync(hidden: true)),
            await ReadAsync(app.GetBrowseAsync(Path.Join(app.Home, "Documents"))),
            await ReadAsync(app.GetBrowseAsync(Path.Join(app.Home, "Vault"))),
            await ReadAsync(app.GetFoundAsync()),
        ];

        foreach (string answer in answers)
        {
            answer.ShouldContain("\"folders\"");
            foreach (string secret in new[] { "secret-plan", "diary", "passwords", "app.json", "Private notes", "TOP-SECRET" })
            {
                answer.ShouldNotContain(secret);
            }
        }

        static async Task<string> ReadAsync(Task<HttpResponseMessage> request)
        {
            using HttpResponseMessage response = await request;
            response.StatusCode.ShouldBe(HttpStatusCode.OK);
            return await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        }
    }

    // ---- GET /api/browse/found ----------------------------------------------------------------------------------

    [Fact]
    public async Task GetFound_VaultsAndTheClaudeFolderOfTheHome_AreFoundWithTheFieldsOfTheContract()
    {
        await using var app = new EditingApp();
        app.WriteFile("home/Documents/Work/.obsidian/app.json", "{}");
        app.WriteFile("home/Documents/Work/Plan.md", "x");
        app.WriteFile("home/Documents/Work/sub/Idea.md", "x");
        app.HomeFolder("Documents/Plain", "P.md");
        await app.StartAsync();

        using HttpResponseMessage response = await app.GetFoundAsync();
        using JsonDocument json = await response.ReadJsonAsync();
        JsonElement root = json.RootElement;
        JsonElement[] folders = [.. root.GetProperty("folders").EnumerateArray()];

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        response.Content.Headers.ContentType?.MediaType.ShouldBe("application/json");
        root.PropertyNames().ShouldBe(["folders", "complete"], ignoreOrder: true);
        root.GetProperty("complete").GetBoolean().ShouldBeTrue();
        folders.Select(folder => folder.GetProperty("name").GetString()).ShouldBe([".claude", "Work"]);

        folders[0].PropertyNames().ShouldBe(["name", "path", "display", "listed", "kind", "markdownCount"], ignoreOrder: true);
        folders[0].GetProperty("kind").GetString().ShouldBe("Claude");
        folders[0].GetProperty("listed").GetBoolean().ShouldBeTrue();
        folders[0].GetProperty("display").GetString().ShouldBe(Tilde(".claude"));

        folders[1].PropertyNames().ShouldBe(["name", "path", "display", "listed", "kind", "markdownCount"], ignoreOrder: true);
        folders[1].GetProperty("path").GetString().ShouldBe(Path.Join(app.Home, "Documents", "Work"));
        folders[1].GetProperty("display").GetString().ShouldBe(Tilde("Documents/Work"));
        folders[1].GetProperty("kind").GetString().ShouldBe("Vault");
        folders[1].GetProperty("listed").GetBoolean().ShouldBeFalse();
        folders[1].GetProperty("markdownCount").GetInt32().ShouldBe(2);
    }

    [Fact]
    public async Task GetFound_Folders_AreSortedByNameAndThenByDisplay()
    {
        await using var app = new EditingApp();
        app.WriteFile("home/Zeta/.obsidian/app.json", "{}");
        app.WriteFile("home/Çiçek/.obsidian/app.json", "{}");
        app.WriteFile("home/b/Alpha/.obsidian/app.json", "{}");
        app.WriteFile("home/a/alpha/.obsidian/app.json", "{}");
        await app.StartAsync();

        using HttpResponseMessage response = await app.GetFoundAsync();
        using JsonDocument json = await response.ReadJsonAsync();
        JsonElement[] folders = [.. json.RootElement.GetProperty("folders").EnumerateArray()];

        // By name, in the Turkish alphabet, whatever case: a dot, Alpha (twice), Çiçek, Zeta. The two Alphas are told apart by their display.
        folders.Select(folder => folder.GetProperty("name").GetString()).ShouldBe([".claude", "alpha", "Alpha", "Çiçek", "Zeta"]);
        folders.Select(folder => folder.GetProperty("display").GetString()).Skip(1).Take(2).ShouldBe([Tilde("a/alpha"), Tilde("b/Alpha")]);
    }

    [Fact]
    public async Task GetFound_ClaudeFolder_HasTheFileCountOfItsSource()
    {
        await using var app = new EditingApp();
        app.WriteFile("home/.claude/rules/style.md", "x");
        app.WriteFile("home/.claude/plugins/some-plugin/README.md", "x");
        app.WriteFile("home/.claude/projects/demo/memory/MEMORY.md", "x");
        app.WriteFile("home/.claude/projects/demo/transcript.md", "x");
        await app.StartAsync();

        using HttpResponseMessage response = await app.GetFoundAsync();
        using JsonDocument json = await response.ReadJsonAsync();
        using JsonDocument sources = await app.GetSourcesAsync();

        Folder(json.RootElement.GetProperty("folders"), ".claude").GetProperty("markdownCount").GetInt32()
            .ShouldBe(sources.RootElement.GetProperty("sources")[0].GetProperty("fileCount").GetInt32());
    }

    [Fact]
    public async Task GetFound_FolderOfLinkedNotesWithoutObsidian_IsFoundWithTheKindNotesAndADocumentationFolderIsNot()
    {
        await using var app = new EditingApp();
        app.WriteFile("home/Ideas/Home.md", "# Home\n[[n01]]\n");
        for (int i = 1; i <= 11; i++)
        {
            app.WriteFile($"home/Ideas/n{i:D2}.md", "See [[Home]].\n");
            app.WriteFile($"home/Docs/page{i:D2}.md", "Plain text, no links.\n");
        }

        app.WriteFile("home/Docs/README.md", "# Docs\n");
        await app.StartAsync();

        using HttpResponseMessage response = await app.GetFoundAsync();
        using JsonDocument json = await response.ReadJsonAsync();
        JsonElement folders = json.RootElement.GetProperty("folders");
        JsonElement ideas = Folder(folders, "Ideas");

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        Names(folders).ShouldBe([".claude", "Ideas"]);
        ideas.PropertyNames().ShouldBe(["name", "path", "display", "listed", "kind", "markdownCount"], ignoreOrder: true);
        ideas.GetProperty("kind").GetString().ShouldBe("Notes");
        ideas.GetProperty("path").GetString().ShouldBe(Path.Join(app.Home, "Ideas"));
        ideas.GetProperty("display").GetString().ShouldBe(Tilde("Ideas"));
        ideas.GetProperty("listed").GetBoolean().ShouldBeFalse();
        ideas.GetProperty("markdownCount").GetInt32().ShouldBe(12);
        json.RootElement.GetProperty("complete").GetBoolean().ShouldBeTrue();
    }

    [Fact]
    public async Task GetFound_FolderOfNotesThatIsAddedAsASource_IsAMarkdownSourceWithTheNotesFeatures()
    {
        await using var app = new EditingApp();
        app.WriteFile("home/Ideas/Home.md", "# Home\n[[n01]]\n");
        for (int i = 1; i <= 11; i++)
        {
            app.WriteFile($"home/Ideas/n{i:D2}.md", "See [[Home]].\n");
        }

        await app.StartAsync();

        using (HttpResponseMessage response = await app.PostAsync(Path.Join(app.Home, "Ideas")))
        {
            response.StatusCode.ShouldBe(HttpStatusCode.Created);
            using JsonDocument added = await response.ReadJsonAsync();

            // What adding it makes of it is what it always made of a folder without .obsidian: the kind of a found folder is not a profile.
            added.RootElement.GetProperty("profile").GetString().ShouldBe("Markdown");
            added.RootElement.GetProperty("fileCount").GetInt32().ShouldBe(12);
        }

        using HttpResponseMessage found = await app.GetFoundAsync();
        using JsonDocument json = await found.ReadJsonAsync();
        JsonElement ideas = Folder(json.RootElement.GetProperty("folders"), "Ideas");

        ideas.GetProperty("kind").GetString().ShouldBe("Notes");
        ideas.GetProperty("listed").GetBoolean().ShouldBeTrue();
    }

    [Fact]
    public async Task GetBrowse_FolderOfLinkedNotes_HasNoKindInAListing()
    {
        await using var app = new EditingApp();
        app.WriteFile("home/Ideas/Home.md", "# Home\n[[n01]]\n");
        for (int i = 1; i <= 11; i++)
        {
            app.WriteFile($"home/Ideas/n{i:D2}.md", "See [[Home]].\n");
        }

        await app.StartAsync();

        using HttpResponseMessage response = await app.GetBrowseAsync();
        using JsonDocument json = await response.ReadJsonAsync();

        // The listing says what the folders say by their names and directories; only the search for folders reads what is in them.
        Folder(json.RootElement.GetProperty("folders"), "Ideas").TryGetProperty("kind", out _).ShouldBeFalse();
    }

    [Fact]
    public async Task GetFound_DriveFolders_AreSearchedAndShownWithTheirFullPath()
    {
        await using var app = new EditingApp();
        string drive = app.Folder("usb-drives");
        app.WriteFile("folders/usb-drives/stick/Backup/.obsidian/app.json", "{}");
        app.WriteFile("folders/usb-drives/stick/Backup/Note.md", "x");
        app.Drives = new DriveFolders([new SearchRoot(drive, MaxDepth: 4)]);
        await app.StartAsync();

        using HttpResponseMessage response = await app.GetFoundAsync();
        using JsonDocument json = await response.ReadJsonAsync();
        JsonElement backup = Folder(json.RootElement.GetProperty("folders"), "Backup");

        backup.GetProperty("path").GetString().ShouldBe(Path.Join(drive, "stick", "Backup"));
        backup.GetProperty("display").GetString().ShouldBe(Path.Join(drive, "stick", "Backup"));
        backup.GetProperty("kind").GetString().ShouldBe("Vault");
        backup.GetProperty("markdownCount").GetInt32().ShouldBe(1);
    }

    [Fact]
    public async Task GetFound_NothingToFind_IsAnEmptyCompleteList()
    {
        await using var app = new EditingApp();
        Directory.Delete(Path.Join(app.Home, ".claude"), recursive: true);
        app.WriteSourcesFile("""{ "sources": [ ] }""");
        app.HomeFolder("Plain", "P.md");
        await app.StartAsync();

        using HttpResponseMessage response = await app.GetFoundAsync();
        using JsonDocument json = await response.ReadJsonAsync();

        json.RootElement.GetProperty("folders").GetArrayLength().ShouldBe(0);
        json.RootElement.GetProperty("complete").GetBoolean().ShouldBeTrue();
    }

    [Fact]
    public async Task GetFound_SearchThatStopsAtItsBudget_IsNotComplete()
    {
        await using var app = new EditingApp { BrowseLimits = BrowseLimits.Default with { SearchEntries = 1 } };
        app.WriteFile("home/a/.obsidian/app.json", "{}");
        app.WriteFile("home/b/.obsidian/app.json", "{}");
        await app.StartAsync();

        using HttpResponseMessage response = await app.GetFoundAsync();
        using JsonDocument json = await response.ReadJsonAsync();

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        json.RootElement.GetProperty("complete").GetBoolean().ShouldBeFalse();

        // One entry is all the search may read: the Claude folder is asked for by name and always there, and of the two vaults at most one is looked at.
        string[] names = Names(json.RootElement.GetProperty("folders"));
        names.ShouldContain(".claude");
        names.Length.ShouldBeLessThan(3);
    }

    [Fact]
    public async Task GetFound_Result_IsRememberedForSixtySecondsAndSearchedAgainAfterThat()
    {
        var time = new ManualTimeProvider();
        await using var app = new EditingApp { Time = time };
        app.WriteFile("home/First/.obsidian/app.json", "{}");
        await app.StartAsync();

        async Task<string[]> FoundAsync()
        {
            using HttpResponseMessage response = await app.GetFoundAsync();
            using JsonDocument json = await response.ReadJsonAsync();
            return Names(json.RootElement.GetProperty("folders"));
        }

        (await FoundAsync()).ShouldBe([".claude", "First"]);

        app.WriteFile("home/Second/.obsidian/app.json", "{}");
        time.Advance(TimeSpan.FromSeconds(59));
        (await FoundAsync()).ShouldBe([".claude", "First"]);

        time.Advance(TimeSpan.FromSeconds(1));
        (await FoundAsync()).ShouldBe([".claude", "First", "Second"]);
    }

    // What is remembered is what was found; whether a source shows it is told at the time of the answer.
    [Fact]
    public async Task GetFound_Listed_FollowsTheSourcesWhileTheResultIsRemembered()
    {
        var time = new ManualTimeProvider();
        await using var app = new EditingApp { Time = time };
        app.WriteFile("home/Vault/.obsidian/app.json", "{}");
        string vault = Path.Join(app.Home, "Vault");
        await app.StartAsync();

        async Task<string[]> ListedAsync()
        {
            using HttpResponseMessage response = await app.GetFoundAsync();
            using JsonDocument json = await response.ReadJsonAsync();
            return ListedNames(json.RootElement.GetProperty("folders"));
        }

        (await ListedAsync()).ShouldBe([".claude"]);

        using HttpResponseMessage added = await app.PostAsync(vault);
        added.StatusCode.ShouldBe(HttpStatusCode.Created);
        (await ListedAsync()).ShouldBe([".claude", "Vault"]);

        using HttpResponseMessage removed = await app.DeleteAsync("vault");
        removed.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await ListedAsync()).ShouldBe([".claude"]);
    }
}
