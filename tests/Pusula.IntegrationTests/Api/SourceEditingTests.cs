using System.Net;
using System.Text.Json;
using Pusula.Indexing;
using Pusula.IntegrationTests.Support;
using Shouldly;
using Xunit;

namespace Pusula.IntegrationTests.Api;

/// <summary>
/// Adding and removing sources from the browser: <c>POST /api/sources</c> and <c>DELETE /api/sources/{id}</c>, who
/// may do it, and what the list says about it. The server runs on made-up directories; who is calling is simulated
/// (see <see cref="SimulatedConnection"/>): by default a browser on the machine the server runs on.
/// </summary>
public sealed class SourceEditingTests
{
    private static string Json(string path) => JsonSerializer.Serialize(path);

    private static string[] Fields(JsonElement item) =>
        [.. item.EnumerateObject().Select(property => $"{property.Name}={property.Value.GetString()}")];

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

    // The sources file as the tests start with it: one folder with two notes.
    private static async Task<(EditingApp App, string Notes)> StartWithOneSourceAsync(bool allowRemoteEdit = false)
    {
        var app = new EditingApp { AllowRemoteEdit = allowRemoteEdit };
        string notes = app.Folder("notes", "A.md", "B.md");
        app.WriteSourcesFile($$"""{ "sources": [ { "id": "notes", "path": {{Json(notes)}} } ] }""");
        await app.StartAsync();
        return (app, notes);
    }

    // ---- The list says whether sources can be changed -----------------------------------------------------------

    [Fact]
    public async Task GetSources_FromTheMachineTheServerRunsOn_CanEditAndHasNoReasonNot()
    {
        (EditingApp app, _) = await StartWithOneSourceAsync();
        await using (app)
        {
            using JsonDocument json = await app.GetSourcesAsync();

            json.RootElement.PropertyNames().ShouldBe(["sources", "canEdit", "machine", "remoteEdit", "version", "sourcesFile"], ignoreOrder: true);
            json.RootElement.GetProperty("canEdit").GetBoolean().ShouldBeTrue();
            json.RootElement.GetProperty("machine").GetString().ShouldBe(Environment.MachineName);
            json.RootElement.GetProperty("remoteEdit").GetBoolean().ShouldBeFalse();
            json.RootElement.GetProperty("sourcesFile").GetString().ShouldBe(app.SourcesFile);
        }
    }

    [Theory]
    [InlineData("203.0.113.9", "192.0.2.10")]
    [InlineData("100.64.0.2", "100.64.0.1")]
    [InlineData("::ffff:203.0.113.9", "192.0.2.10")]
    [InlineData("203.0.113.9", "127.0.0.1")]
    [InlineData("none", "none")]
    [InlineData("none", "127.0.0.1")]
    public async Task GetSources_FromAnotherMachine_CannotEditBecauseItIsRemote(string remote, string local)
    {
        (EditingApp app, _) = await StartWithOneSourceAsync();
        await using (app)
        {
            using JsonDocument json = await app.GetSourcesAsync(EditingApp.From(remote, local));

            json.RootElement.GetProperty("canEdit").GetBoolean().ShouldBeFalse();
            json.RootElement.GetProperty("editBlocked").GetString().ShouldBe("Remote");
            json.RootElement.GetProperty("sources").GetArrayLength().ShouldBe(1);
        }
    }

    [Theory]
    [InlineData("192.0.2.10", "192.0.2.10")]
    [InlineData("::1", "::1")]
    [InlineData("::ffff:127.0.0.1", "::ffff:127.0.0.1")]
    [InlineData("::ffff:192.0.2.10", "192.0.2.10")]
    [InlineData("127.0.0.1", "192.0.2.10")]
    public async Task GetSources_FromTheMachinesOwnAddress_CanEdit(string remote, string local)
    {
        (EditingApp app, _) = await StartWithOneSourceAsync();
        await using (app)
        {
            using JsonDocument json = await app.GetSourcesAsync(EditingApp.From(remote, local));

            json.RootElement.GetProperty("canEdit").GetBoolean().ShouldBeTrue();
            json.RootElement.TryGetProperty("editBlocked", out _).ShouldBeFalse();
        }
    }

    // A reverse proxy on this machine: the connection is from the loopback address, the headers say it was forwarded.
    [Theory]
    [InlineData("Forwarded", "for=203.0.113.9;proto=https")]
    [InlineData("X-Forwarded-For", "203.0.113.9")]
    [InlineData("X-Forwarded-Host", "pusula.example")]
    [InlineData("X-Real-IP", "203.0.113.9")]
    public async Task GetSources_ThroughAReverseProxyOnThisMachine_CannotEditBecauseItIsRemote(string header, string value)
    {
        (EditingApp app, _) = await StartWithOneSourceAsync();
        await using (app)
        {
            using JsonDocument forwarded = await app.GetSourcesAsync([(header, value)]);
            using JsonDocument direct = await app.GetSourcesAsync();

            forwarded.RootElement.GetProperty("canEdit").GetBoolean().ShouldBeFalse();
            forwarded.RootElement.GetProperty("editBlocked").GetString().ShouldBe("Remote");
            forwarded.RootElement.GetProperty("sources").GetArrayLength().ShouldBe(1);
            direct.RootElement.GetProperty("canEdit").GetBoolean().ShouldBeTrue();
        }
    }

    [Fact]
    public async Task GetSources_FoldersGivenOutsideASourcesFile_CannotEditBecauseOfTheCommandLine()
    {
        await using var app = new EditingApp();
        app.Root = app.Folder("given", "G.md");
        await app.StartAsync();

        using JsonDocument local = await app.GetSourcesAsync();
        using JsonDocument remote = await app.GetSourcesAsync(EditingApp.From(EditingApp.AnotherMachine, EditingApp.ThisMachine));

        local.RootElement.PropertyNames().ShouldBe(["sources", "canEdit", "machine", "remoteEdit", "version", "editBlocked"], ignoreOrder: true);
        local.RootElement.GetProperty("canEdit").GetBoolean().ShouldBeFalse();
        local.RootElement.GetProperty("editBlocked").GetString().ShouldBe("CommandLine");
        remote.RootElement.GetProperty("canEdit").GetBoolean().ShouldBeFalse();
        remote.RootElement.GetProperty("editBlocked").GetString().ShouldBe("Remote");
    }

    [Fact]
    public async Task GetSources_SourcesFileBrokenWhileTheServerRuns_SaysWhyAndKeepsShowingTheLastList()
    {
        (EditingApp app, string notes) = await StartWithOneSourceAsync();
        await using (app)
        {
            app.WriteSourcesFile("""{ "sources": [ { "path": """);

            await EditingApp.WaitUntilAsync(
                async () =>
                {
                    using JsonDocument json = await app.GetSourcesAsync();
                    return json.RootElement.TryGetProperty("sourcesFileError", out _);
                },
                "the broken sources file to be noticed");
            using JsonDocument broken = await app.GetSourcesAsync();

            broken.RootElement.GetProperty("sourcesFileError").GetString().ShouldStartWith("invalid JSON: ");
            broken.RootElement.GetProperty("canEdit").GetBoolean().ShouldBeTrue();
            broken.RootElement.GetProperty("sources").EnumerateArray().Select(source => source.GetProperty("path").GetString()).ShouldBe([notes]);

            app.WriteSourcesFile($$"""{ "sources": [ { "id": "notes", "path": {{Json(notes)}} } ] }""");

            await EditingApp.WaitUntilAsync(
                async () =>
                {
                    using JsonDocument json = await app.GetSourcesAsync();
                    return !json.RootElement.TryGetProperty("sourcesFileError", out _);
                },
                "the fixed sources file to be noticed");
        }
    }

    // ---- Pusula:AllowRemoteEdit ---------------------------------------------------------------------------------
    // The setting lifts "from the machine the server runs on" and nothing else: the sources file, the page of this
    // server and the Host check are asked for all the same.

    private static readonly (string Name, string Value)[] AnotherMachine = EditingApp.From(EditingApp.AnotherMachine, EditingApp.ThisMachine);

    [Fact]
    public async Task GetSources_RemoteEditAllowed_EveryMachineCanEditAndTheListSaysThatItIsAllowed()
    {
        (EditingApp app, _) = await StartWithOneSourceAsync(allowRemoteEdit: true);
        await using (app)
        {
            using JsonDocument remote = await app.GetSourcesAsync(AnotherMachine);
            using JsonDocument noAddress = await app.GetSourcesAsync(EditingApp.From("none", "none"));
            using JsonDocument proxied = await app.GetSourcesAsync([("X-Forwarded-For", "203.0.113.9")]);
            using JsonDocument local = await app.GetSourcesAsync();

            foreach (JsonDocument json in new[] { remote, noAddress, proxied, local })
            {
                json.RootElement.PropertyNames().ShouldBe(["sources", "canEdit", "machine", "remoteEdit", "version", "sourcesFile"], ignoreOrder: true);
                json.RootElement.GetProperty("canEdit").GetBoolean().ShouldBeTrue();
                json.RootElement.GetProperty("remoteEdit").GetBoolean().ShouldBeTrue();
                json.RootElement.GetProperty("machine").GetString().ShouldBe(Environment.MachineName);
                json.RootElement.GetProperty("sources").GetArrayLength().ShouldBe(1);
            }
        }
    }

    [Fact]
    public async Task GetSources_RemoteEditAllowedButTheListIsFromTheCommandLine_SaysCommandLineNotRemote()
    {
        await using var app = new EditingApp { AllowRemoteEdit = true };
        app.Root = app.Folder("given", "G.md");
        await app.StartAsync();

        using JsonDocument remote = await app.GetSourcesAsync(AnotherMachine);
        using JsonDocument local = await app.GetSourcesAsync();

        foreach (JsonDocument json in new[] { remote, local })
        {
            json.RootElement.GetProperty("canEdit").GetBoolean().ShouldBeFalse();
            json.RootElement.GetProperty("editBlocked").GetString().ShouldBe("CommandLine");
            json.RootElement.GetProperty("remoteEdit").GetBoolean().ShouldBeTrue();
        }
    }

    [Theory]
    [InlineData("203.0.113.9", "192.0.2.10")]
    [InlineData("100.64.0.2", "100.64.0.1")]
    [InlineData("::ffff:203.0.113.9", "192.0.2.10")]
    [InlineData("fd7a:115c:a1e0::2", "fd7a:115c:a1e0::1")]
    [InlineData("203.0.113.9", "127.0.0.1")]
    [InlineData("none", "none")]
    public async Task Post_RemoteEditAllowed_FromAnotherMachine_Returns201AndTheSourceIsAdded(string remote, string local)
    {
        (EditingApp app, _) = await StartWithOneSourceAsync(allowRemoteEdit: true);
        await using (app)
        {
            string folder = app.Folder("other", "O.md");

            using HttpResponseMessage response = await app.PostAsync(folder, headers: EditingApp.From(remote, local));

            response.StatusCode.ShouldBe(HttpStatusCode.Created);
            response.Headers.Location?.ToString().ShouldBe("/api/sources/other");
            app.Ids.ShouldBe(["notes", "other"]);
            Fields(app.ReadItems()[1]).ShouldBe(["id=other", "name=other", $"path={folder}", "profile=auto"]);
        }
    }

    [Theory]
    [InlineData("Forwarded", "for=203.0.113.9;proto=https")]
    [InlineData("X-Forwarded-For", "203.0.113.9")]
    [InlineData("X-Forwarded-Host", "pusula.example")]
    [InlineData("X-Real-IP", "203.0.113.9")]
    public async Task Post_RemoteEditAllowed_ThroughAReverseProxy_IsAcceptedToo(string header, string value)
    {
        (EditingApp app, _) = await StartWithOneSourceAsync(allowRemoteEdit: true);
        await using (app)
        {
            using HttpResponseMessage response = await app.PostAsync(app.Folder("other", "O.md"), headers: [.. AnotherMachine, (header, value)]);

            response.StatusCode.ShouldBe(HttpStatusCode.Created);
            app.Ids.ShouldBe(["notes", "other"]);
        }
    }

    [Fact]
    public async Task Post_RemoteEditAllowed_FromAnotherMachine_IsCheckedLikeAnyOtherRequestAfterThat()
    {
        (EditingApp app, _) = await StartWithOneSourceAsync(allowRemoteEdit: true);
        await using (app)
        {
            byte[] before = File.ReadAllBytes(app.SourcesFile);

            using HttpResponseMessage noPath = await app.PostJsonAsync("{}", AnotherMachine);
            using HttpResponseMessage listed = await app.PostAsync(app.Folder("again", "A.md"), headers: AnotherMachine);
            using HttpResponseMessage again = await app.PostAsync(app.Folder("again", "A.md"), headers: AnotherMachine);

            await ShouldBeProblemAsync(noPath, HttpStatusCode.BadRequest, "PathRequired");
            listed.StatusCode.ShouldBe(HttpStatusCode.Created);
            await ShouldBeProblemAsync(again, HttpStatusCode.Conflict, "AlreadyListed");
            File.ReadAllBytes(app.SourcesFile).ShouldNotBe(before);
            app.Ids.ShouldBe(["notes", "again"]);
        }
    }

    [Fact]
    public async Task Post_RemoteEditAllowed_FromAPageOfThisServerOnAnotherMachine_IsAccepted()
    {
        (EditingApp app, _) = await StartWithOneSourceAsync(allowRemoteEdit: true);
        await using (app)
        {
            using HttpResponseMessage response = await app.PostAsync(
                app.Folder("other", "O.md"),
                headers: [.. AnotherMachine, ("Origin", "http://localhost"), ("Sec-Fetch-Site", "same-origin")]);

            response.StatusCode.ShouldBe(HttpStatusCode.Created);
        }
    }

    [Theory]
    [InlineData("Origin", "http://evil.example")]
    [InlineData("Origin", "http://localhost:5190")]
    [InlineData("Origin", "null")]
    [InlineData("Sec-Fetch-Site", "cross-site")]
    [InlineData("Sec-Fetch-Site", "same-site")]
    [InlineData("Sec-Fetch-Site", "none")]
    public async Task Post_RemoteEditAllowed_FromAnotherPage_StillReturns403CrossOriginAndChangesNothing(string header, string value)
    {
        (EditingApp app, _) = await StartWithOneSourceAsync(allowRemoteEdit: true);
        await using (app)
        {
            byte[] before = File.ReadAllBytes(app.SourcesFile);

            using HttpResponseMessage fromAnotherMachine = await app.PostAsync(app.Folder("other", "O.md"), headers: [.. AnotherMachine, (header, value)]);
            using HttpResponseMessage fromThisMachine = await app.PostAsync(app.Folder("other", "O.md"), headers: [(header, value)]);
            using HttpResponseMessage removal = await app.DeleteAsync("notes", [.. AnotherMachine, (header, value)]);

            await ShouldBeProblemAsync(fromAnotherMachine, HttpStatusCode.Forbidden, "CrossOrigin");
            await ShouldBeProblemAsync(fromThisMachine, HttpStatusCode.Forbidden, "CrossOrigin");
            await ShouldBeProblemAsync(removal, HttpStatusCode.Forbidden, "CrossOrigin");
            File.ReadAllBytes(app.SourcesFile).ShouldBe(before);
            app.Ids.ShouldBe(["notes"]);
        }
    }

    [Fact]
    public async Task Post_RemoteEditAllowed_FoldersGivenOutsideASourcesFile_StillReturns403CommandLineWhoeverAsks()
    {
        await using var app = new EditingApp { AllowRemoteEdit = true };
        app.Root = app.Folder("given", "G.md");
        await app.StartAsync();
        string folder = app.Folder("other", "O.md");

        using HttpResponseMessage remote = await app.PostAsync(folder, headers: AnotherMachine);
        using HttpResponseMessage local = await app.PostAsync(folder);
        using HttpResponseMessage removal = await app.DeleteAsync("given", AnotherMachine);

        await ShouldBeProblemAsync(remote, HttpStatusCode.Forbidden, "CommandLine");
        await ShouldBeProblemAsync(local, HttpStatusCode.Forbidden, "CommandLine");
        await ShouldBeProblemAsync(removal, HttpStatusCode.Forbidden, "CommandLine");
        app.Ids.ShouldBe(["given"]);
        File.Exists(app.SourcesFile).ShouldBeFalse();
    }

    [Theory]
    [InlineData("evil.example")]
    [InlineData("evil.example:5190")]
    [InlineData("rebind.attacker.test")]
    public async Task Post_RemoteEditAllowed_ToAHostThatIsNotThisServers_StillReturns400AndChangesNothing(string host)
    {
        (EditingApp app, _) = await StartWithOneSourceAsync(allowRemoteEdit: true);
        await using (app)
        {
            byte[] before = File.ReadAllBytes(app.SourcesFile);

            using HttpResponseMessage post = await app.PostAsync(app.Folder("other", "O.md"), headers: [.. AnotherMachine, ("Host", host)]);
            using HttpResponseMessage delete = await app.DeleteAsync("notes", [.. AnotherMachine, ("Host", host)]);
            using HttpResponseMessage list = await app.SendAsync(HttpMethod.Get, "/api/sources", headers: [.. AnotherMachine, ("Host", host)]);

            post.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
            delete.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
            list.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
            File.ReadAllBytes(app.SourcesFile).ShouldBe(before);
            app.Ids.ShouldBe(["notes"]);
        }
    }

    [Fact]
    public async Task Delete_RemoteEditAllowed_FromAnotherMachine_Returns204AndTheSourceIsGone()
    {
        (EditingApp app, string notes) = await StartWithOneSourceAsync(allowRemoteEdit: true);
        await using (app)
        {
            using HttpResponseMessage response = await app.DeleteAsync("notes", AnotherMachine);
            using HttpResponseMessage again = await app.DeleteAsync("notes", AnotherMachine);

            response.StatusCode.ShouldBe(HttpStatusCode.NoContent);
            await ShouldBeProblemAsync(again, HttpStatusCode.NotFound, "NotFound");
            app.Ids.ShouldBeEmpty();
            app.ReadItems().ShouldBeEmpty();
            File.Exists(Path.Join(notes, "A.md")).ShouldBeTrue();
        }
    }

    [Fact]
    public async Task Post_RemoteEditNotAllowed_IsTheDefaultAndAnotherMachineStillGets403Remote()
    {
        (EditingApp app, _) = await StartWithOneSourceAsync();
        await using (app)
        {
            using HttpResponseMessage response = await app.PostAsync(app.Folder("other", "O.md"), headers: AnotherMachine);
            using JsonDocument list = await app.GetSourcesAsync(AnotherMachine);

            await ShouldBeProblemAsync(response, HttpStatusCode.Forbidden, "Remote");
            list.RootElement.GetProperty("remoteEdit").GetBoolean().ShouldBeFalse();
            list.RootElement.GetProperty("canEdit").GetBoolean().ShouldBeFalse();
            list.RootElement.GetProperty("editBlocked").GetString().ShouldBe("Remote");
            app.Ids.ShouldBe(["notes"]);
        }
    }

    // ---- POST /api/sources --------------------------------------------------------------------------------------

    [Fact]
    public async Task Post_Folder_Returns201WithItsLocationAndTheSourceWithItsFirstIndexBuilt()
    {
        (EditingApp app, _) = await StartWithOneSourceAsync();
        await using (app)
        {
            string folder = app.Folder("vault/Work notes", "Home.md", "Plan.md", "Ideas.md");

            using HttpResponseMessage response = await app.PostAsync(folder, name: "Work notes");
            using JsonDocument json = await response.ReadJsonAsync();

            response.StatusCode.ShouldBe(HttpStatusCode.Created);
            response.Headers.Location?.ToString().ShouldBe("/api/sources/work-notes");
            response.Content.Headers.ContentType?.MediaType.ShouldBe("application/json");
            json.RootElement.PropertyNames().ShouldBe(["id", "name", "path", "profile", "available", "fileCount"], ignoreOrder: true);
            json.RootElement.GetProperty("id").GetString().ShouldBe("work-notes");
            json.RootElement.GetProperty("name").GetString().ShouldBe("Work notes");
            json.RootElement.GetProperty("path").GetString().ShouldBe(folder);
            json.RootElement.GetProperty("profile").GetString().ShouldBe("Markdown");
            json.RootElement.GetProperty("available").GetBoolean().ShouldBeTrue();
            json.RootElement.GetProperty("fileCount").GetInt32().ShouldBe(3);
        }
    }

    [Fact]
    public async Task Post_Folder_IsListedLastAndItsEndpointsWorkAtOnce()
    {
        (EditingApp app, string notes) = await StartWithOneSourceAsync();
        await using (app)
        {
            string vault = app.Folder("vault", "Home.md");
            Directory.CreateDirectory(Path.Join(vault, ".obsidian"));

            using HttpResponseMessage response = await app.PostAsync(vault);
            using JsonDocument list = await app.GetSourcesAsync();
            using JsonDocument tree = await app.Client.GetJsonAsync("/api/sources/vault/tree");
            using JsonDocument overview = await app.Client.GetJsonAsync("/api/sources/vault/overview");

            response.StatusCode.ShouldBe(HttpStatusCode.Created);
            list.RootElement.GetProperty("sources").EnumerateArray().Select(source => source.GetProperty("id").GetString()).ShouldBe(["notes", "vault"]);
            list.RootElement.GetProperty("sources")[0].GetProperty("path").GetString().ShouldBe(notes);
            list.RootElement.GetProperty("sources")[1].GetProperty("profile").GetString().ShouldBe("Vault");
            tree.RootElement.GetProperty("root").GetString().ShouldBe(vault);
            tree.RootElement.GetProperty("nodes")[0].GetProperty("name").GetString().ShouldBe("Home.md");
            overview.RootElement.GetProperty("profile").GetString().ShouldBe("Vault");
        }
    }

    [Fact]
    public async Task Post_TildePathNameAndProfile_AreExpandedTrimmedAndKept()
    {
        (EditingApp app, _) = await StartWithOneSourceAsync();
        await using (app)
        {
            string folder = app.HomeFolder("Documents/notlar", "N.md");

            using HttpResponseMessage response = await app.PostAsync("~/Documents/notlar/", name: "  Notlarım ", profile: "VAULT");
            using JsonDocument json = await response.ReadJsonAsync();

            response.StatusCode.ShouldBe(HttpStatusCode.Created);
            json.RootElement.GetProperty("id").GetString().ShouldBe("notlarim");
            json.RootElement.GetProperty("name").GetString().ShouldBe("Notlarım");
            json.RootElement.GetProperty("path").GetString().ShouldBe(folder);
            json.RootElement.GetProperty("profile").GetString().ShouldBe("Vault");
            Fields(app.ReadItems()[1]).ShouldBe(["id=notlarim", "name=Notlarım", "path=~/Documents/notlar", "profile=vault"]);
        }
    }

    [Fact]
    public async Task Post_NoSourcesFileYet_CreatesItWithTheListThatWasShownAndTheNewSource()
    {
        await using var app = new EditingApp();
        string notes = app.Folder("notes", "N.md");
        await app.StartAsync();
        app.Ids.ShouldBe(["claude"]);
        File.Exists(app.SourcesFile).ShouldBeFalse();

        using HttpResponseMessage response = await app.PostAsync(notes);
        using JsonDocument list = await app.GetSourcesAsync();

        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        File.Exists(app.SourcesFile).ShouldBeTrue();
        JsonElement[] items = app.ReadItems();
        Fields(items[0]).ShouldBe(["id=claude", "name=.claude", "path=~/.claude", "profile=auto"]);
        Fields(items[1]).ShouldBe(["id=notes", "name=notes", $"path={notes}", "profile=auto"]);
        list.RootElement.GetProperty("sources").EnumerateArray().Select(source => source.GetProperty("id").GetString()).ShouldBe(["claude", "notes"]);
        list.RootElement.GetProperty("sourcesFile").GetString().ShouldBe(app.SourcesFile);
    }

    [Fact]
    public async Task Post_ExistingFile_KeepsItsOtherItemsAsTheyWereWritten()
    {
        await using var app = new EditingApp();
        app.HomeFolder("Documents/old", "O.md");
        string gone = app.Missing("gone");
        app.WriteSourcesFile(
            $$"""
            {
              // this comment is lost
              "sources": [
                { "path": "~/Documents/old", "profile": "auto" },
                { "id": "gone", "path": {{Json(gone)}} },
              ],
            }
            """);
        await app.StartAsync();
        string notes = app.Folder("notes", "N.md");

        using HttpResponseMessage response = await app.PostAsync(notes);

        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        JsonElement[] items = app.ReadItems();
        items.Length.ShouldBe(3);
        Fields(items[0]).ShouldBe(["path=~/Documents/old", "profile=auto"]);
        Fields(items[1]).ShouldBe(["id=gone", $"path={gone}"]);
        Fields(items[2]).ShouldBe(["id=notes", "name=notes", $"path={notes}", "profile=auto"]);
        app.ReadSourcesFile().ShouldNotContain("this comment is lost");
        app.Ids.ShouldBe(["old", "gone", "notes"]);
    }

    // The root of the file system is not one of the cases on purpose: if the check that refuses it ever broke, this test
    // would make the server index the whole machine instead of failing. The check is tested on its own (SourceRequestValidatorTests).
    [Theory]
    [InlineData("PathRequired", "no-path")]
    [InlineData("PathRequired", "blank-path")]
    [InlineData("PathRequired", "null-path")]
    [InlineData("PathRequired", "null-body")]
    [InlineData("PathNotAbsolute", "relative")]
    [InlineData("PathNotAbsolute", "dot-relative")]
    [InlineData("PathNotAbsolute", "other-user")]
    [InlineData("FolderNotFound", "missing")]
    [InlineData("FolderNotFound", "tilde-missing")]
    [InlineData("FolderNotFound", "a-file")]
    [InlineData("TooBroad", "home")]
    [InlineData("TooBroad", "tilde")]
    [InlineData("TooBroad", "tilde-slash")]
    [InlineData("InvalidName", "long-name")]
    [InlineData("InvalidName", "control-name")]
    [InlineData("InvalidProfile", "bad-profile")]
    [InlineData("InvalidProfile", "numeric-profile")]
    public async Task Post_InvalidRequest_Returns400WithProblemDetailsThatNameTheCode(string code, string kind)
    {
        (EditingApp app, _) = await StartWithOneSourceAsync();
        await using (app)
        {
            string folder = app.Folder("ok", "A.md");
            string file = app.WriteFile("folders/a-file.md", "x");
            byte[] before = File.ReadAllBytes(app.SourcesFile);
            string body = kind switch
            {
                "no-path" => "{}",
                "blank-path" => """{ "path": "   " }""",
                "null-path" => """{ "path": null }""",
                "null-body" => "null",
                "relative" => """{ "path": "notes/sub" }""",
                "dot-relative" => """{ "path": "./notes" }""",
                "other-user" => """{ "path": "~other/notes" }""",
                "missing" => $$"""{ "path": {{Json(app.Missing("nope"))}} }""",
                "tilde-missing" => """{ "path": "~/pusula-test-no-such-folder" }""",
                "a-file" => $$"""{ "path": {{Json(file)}} }""",
                "home" => $$"""{ "path": {{Json(app.Home)}} }""",
                "tilde" => """{ "path": "~" }""",
                "tilde-slash" => """{ "path": "~/" }""",
                "long-name" => $$"""{ "path": {{Json(folder)}}, "name": {{Json(new string('n', 81))}} }""",
                "control-name" => $$"""{ "path": {{Json(folder)}}, "name": "a\nb" }""",
                "bad-profile" => $$"""{ "path": {{Json(folder)}}, "profile": "obsidian" }""",
                "numeric-profile" => $$"""{ "path": {{Json(folder)}}, "profile": "1" }""",
                _ => throw new ArgumentOutOfRangeException(nameof(kind)),
            };

            using HttpResponseMessage response = await app.PostJsonAsync(body);

            await ShouldBeProblemAsync(response, HttpStatusCode.BadRequest, code);
            File.ReadAllBytes(app.SourcesFile).ShouldBe(before);
            app.Ids.ShouldBe(["notes"]);
        }
    }

    // 500 never: a path with a character that no path has is a folder that is not found.
    [Theory]
    [InlineData("""{ "path": "/tmp/a\u0000b" }""")]
    [InlineData("""{ "path": "\u0000" }""")]
    [InlineData("""{ "path": "relative\u0000path" }""")]
    [InlineData("""{ "path": "~/x\u0000" }""")]
    public async Task Post_PathWithACharacterNoPathHas_Returns400FolderNotFoundAndNeverAnError(string body)
    {
        (EditingApp app, _) = await StartWithOneSourceAsync();
        await using (app)
        {
            byte[] before = File.ReadAllBytes(app.SourcesFile);

            using HttpResponseMessage response = await app.PostJsonAsync(body);

            await ShouldBeProblemAsync(response, HttpStatusCode.BadRequest, "FolderNotFound");
            File.ReadAllBytes(app.SourcesFile).ShouldBe(before);
            app.Ids.ShouldBe(["notes"]);
        }
    }

    [Fact]
    public async Task Post_WhileTheSourcesFileHasAPathThatCannotBeAPath_Returns409FileInvalidAndTheListSaysWhy()
    {
        (EditingApp app, _) = await StartWithOneSourceAsync();
        await using (app)
        {
            app.WriteSourcesFile("""{ "sources": [ { "path": "/a/x\u0000y" } ] }""");
            byte[] broken = File.ReadAllBytes(app.SourcesFile);

            using HttpResponseMessage response = await app.PostAsync(app.Folder("other", "O.md"));
            using HttpResponseMessage removed = await app.DeleteAsync("notes");
            await EditingApp.WaitUntilAsync(
                async () =>
                {
                    using JsonDocument json = await app.GetSourcesAsync();
                    return json.RootElement.TryGetProperty("sourcesFileError", out _);
                },
                "the unusable sources file to be noticed");
            using JsonDocument list = await app.GetSourcesAsync();

            await ShouldBeProblemAsync(response, HttpStatusCode.Conflict, "FileInvalid");
            await ShouldBeProblemAsync(removed, HttpStatusCode.Conflict, "FileInvalid");
            list.RootElement.GetProperty("sourcesFileError").GetString().ShouldBe("source 1: \"path\" has a character that no path has");
            list.RootElement.GetProperty("sources").GetArrayLength().ShouldBe(1);
            File.ReadAllBytes(app.SourcesFile).ShouldBe(broken);
        }
    }

    // A folder that is more than is shown is added all the same: 201, the source is not available and says why, and it can be removed.
    [Theory]
    [InlineData("files", "The folder is too large to show: more than 3 Markdown files.")]
    [InlineData("folders", "The folder is too large to show: more than 2 folders.")]
    [InlineData("bytes", "The folder is too large to show: more than 500 bytes of Markdown.")]
    public async Task Post_FolderThatIsTooLargeToShow_Returns201WithTheSourceNotAvailableAndTheReason(string kind, string reason)
    {
        await using var app = new EditingApp { Limits = new ScanLimits(MaxFiles: 3, MaxDirectories: 2, MaxBytes: 500) };
        string folder = app.Folder("big", "1.md");
        switch (kind)
        {
            case "files":
                app.Folder("big", "2.md", "3.md", "4.md");
                break;
            case "folders":
                app.Folder("big/one");
                app.Folder("big/two");
                app.Folder("big/three");
                break;
            default:
                app.WriteFile("folders/big/1.md", new string('x', 300));
                app.WriteFile("folders/big/2.md", new string('y', 300));
                break;
        }

        app.WriteSourcesFile($$"""{ "sources": [ ] }""");
        await app.StartAsync();

        using HttpResponseMessage response = await app.PostAsync(folder);
        using JsonDocument json = await response.ReadJsonAsync();
        using JsonDocument list = await app.GetSourcesAsync();
        using HttpResponseMessage tree = await app.Client.GetResponseAsync("/api/sources/big/tree");
        using JsonDocument unavailable = await tree.ReadJsonAsync();

        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        response.Headers.Location?.ToString().ShouldBe("/api/sources/big");
        json.RootElement.PropertyNames().ShouldBe(["id", "name", "path", "profile", "available", "fileCount", "error", "errorCode"], ignoreOrder: true);
        json.RootElement.GetProperty("available").GetBoolean().ShouldBeFalse();
        json.RootElement.GetProperty("fileCount").GetInt32().ShouldBe(0);
        json.RootElement.GetProperty("error").GetString().ShouldBe(reason);
        json.RootElement.GetProperty("errorCode").GetString().ShouldBe("TooLarge");
        list.RootElement.GetProperty("sources")[0].GetProperty("error").GetString().ShouldBe(reason);
        list.RootElement.GetProperty("sources")[0].GetProperty("errorCode").GetString().ShouldBe("TooLarge");
        app.ReadItems().Length.ShouldBe(1);
        tree.StatusCode.ShouldBe(HttpStatusCode.ServiceUnavailable);
        unavailable.RootElement.GetProperty("detail").GetString().ShouldBe(reason);
        unavailable.RootElement.GetProperty("code").GetString().ShouldBe("TooLarge");

        using HttpResponseMessage removed = await app.DeleteAsync("big");

        removed.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        app.Ids.ShouldBeEmpty();
    }

    [Fact]
    public async Task Post_FolderThatIsListedAlready_Returns409AndChangesNothing()
    {
        (EditingApp app, string notes) = await StartWithOneSourceAsync();
        await using (app)
        {
            byte[] before = File.ReadAllBytes(app.SourcesFile);

            using HttpResponseMessage same = await app.PostAsync(notes, name: "Another name");
            using HttpResponseMessage trailing = await app.PostAsync(notes + Path.DirectorySeparatorChar);
            using HttpResponseMessage dots = await app.PostAsync(Path.Join(notes, "..", "notes"));

            await ShouldBeProblemAsync(same, HttpStatusCode.Conflict, "AlreadyListed");
            await ShouldBeProblemAsync(trailing, HttpStatusCode.Conflict, "AlreadyListed");
            await ShouldBeProblemAsync(dots, HttpStatusCode.Conflict, "AlreadyListed");
            File.ReadAllBytes(app.SourcesFile).ShouldBe(before);
            app.Ids.ShouldBe(["notes"]);
        }
    }

    [Fact]
    public async Task Post_WhileTheSourcesFileCannotBeUsed_Returns409AndLeavesTheFileAlone()
    {
        (EditingApp app, _) = await StartWithOneSourceAsync();
        await using (app)
        {
            app.WriteSourcesFile("""{ "sources": [ { "name": "no path" } ] }""");
            byte[] broken = File.ReadAllBytes(app.SourcesFile);

            using HttpResponseMessage response = await app.PostAsync(app.Folder("other", "O.md"));

            await ShouldBeProblemAsync(response, HttpStatusCode.Conflict, "FileInvalid");
            using JsonDocument json = await response.ReadJsonAsync();
            json.RootElement.GetProperty("detail").GetString().ShouldNotBeNull().ShouldEndWith("source 1: \"path\" is required");
            File.ReadAllBytes(app.SourcesFile).ShouldBe(broken);
            app.Ids.ShouldBe(["notes"]);
        }
    }

    [Fact]
    public async Task Post_FileThatCannotBeWritten_Returns500WithTheCodeAndNothingChanges()
    {
        await using var app = new EditingApp();

        // A folder where the sources file should be: nothing can take its place.
        Directory.CreateDirectory(app.SourcesFile);
        await app.StartAsync();

        using HttpResponseMessage response = await app.PostAsync(app.Folder("notes", "N.md"));

        await ShouldBeProblemAsync(response, HttpStatusCode.InternalServerError, "WriteFailed");
        app.Ids.ShouldBe(["claude"]);
    }

    [Theory]
    [InlineData("text/plain")]
    [InlineData("application/x-www-form-urlencoded")]
    [InlineData("multipart/form-data; boundary=x")]
    [InlineData(null)]
    public async Task Post_BodyThatIsNotSentAsJson_Returns415AndChangesNothing(string? contentType)
    {
        (EditingApp app, _) = await StartWithOneSourceAsync();
        await using (app)
        {
            string folder = app.Folder("other", "O.md");
            byte[] before = File.ReadAllBytes(app.SourcesFile);

            using HttpResponseMessage response = await app.SendAsync(HttpMethod.Post, "/api/sources", $$"""{ "path": {{Json(folder)}} }""", contentType);

            response.StatusCode.ShouldBe(HttpStatusCode.UnsupportedMediaType);
            File.ReadAllBytes(app.SourcesFile).ShouldBe(before);
            app.Ids.ShouldBe(["notes"]);
        }
    }

    [Theory]
    [InlineData("""{ "path": """)]
    [InlineData("""{ "path": 5 }""")]
    [InlineData("""{ "path": ["a"] }""")]
    [InlineData("[]")]
    public async Task Post_BodyThatIsNotARequest_Returns400AndChangesNothing(string body)
    {
        (EditingApp app, _) = await StartWithOneSourceAsync();
        await using (app)
        {
            byte[] before = File.ReadAllBytes(app.SourcesFile);

            using HttpResponseMessage response = await app.PostJsonAsync(body);

            response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
            File.ReadAllBytes(app.SourcesFile).ShouldBe(before);
            app.Ids.ShouldBe(["notes"]);
        }
    }

    [Fact]
    public async Task Post_TwoAtOnce_AreBothAddedAndTheFileHasBoth()
    {
        (EditingApp app, _) = await StartWithOneSourceAsync();
        await using (app)
        {
            string[] folders = [.. Enumerable.Range(1, 5).Select(number => app.Folder($"f{number}", "A.md"))];

            HttpResponseMessage[] responses = await Task.WhenAll(folders.Select(folder => app.PostAsync(folder)));

            responses.ShouldAllBe(response => response.StatusCode == HttpStatusCode.Created);
            app.Ids.Length.ShouldBe(6);
            app.ReadItems().Select(item => item.GetProperty("id").GetString()).ShouldBe(app.Ids);
            foreach (HttpResponseMessage response in responses)
            {
                response.Dispose();
            }
        }
    }

    // ---- Who may change the sources -----------------------------------------------------------------------------

    [Theory]
    [InlineData("203.0.113.9", "192.0.2.10")]
    [InlineData("100.64.0.2", "100.64.0.1")]
    [InlineData("::ffff:203.0.113.9", "192.0.2.10")]
    [InlineData("fd7a:115c:a1e0::2", "fd7a:115c:a1e0::1")]
    [InlineData("203.0.113.9", "127.0.0.1")]
    [InlineData("none", "none")]
    [InlineData("none", "127.0.0.1")]
    public async Task Post_FromAnotherMachine_Returns403RemoteAndChangesNothing(string remote, string local)
    {
        (EditingApp app, _) = await StartWithOneSourceAsync();
        await using (app)
        {
            string folder = app.Folder("other", "O.md");
            byte[] before = File.ReadAllBytes(app.SourcesFile);

            using HttpResponseMessage response = await app.PostAsync(folder, headers: EditingApp.From(remote, local));

            await ShouldBeProblemAsync(response, HttpStatusCode.Forbidden, "Remote");
            File.ReadAllBytes(app.SourcesFile).ShouldBe(before);
            app.Ids.ShouldBe(["notes"]);
        }
    }

    [Fact]
    public async Task Post_FromAnotherMachineWithABadRequest_IsRefusedBeforeAnythingAboutItIsSaid()
    {
        (EditingApp app, _) = await StartWithOneSourceAsync();
        await using (app)
        {
            foreach (string body in new[] { "{}", """{ "path": "/" }""", """{ "path": "/no/such/folder" }""", """{ "path": "x", "profile": "nope" }""" })
            {
                using HttpResponseMessage response = await app.PostJsonAsync(body, EditingApp.From(EditingApp.AnotherMachine, EditingApp.ThisMachine));

                await ShouldBeProblemAsync(response, HttpStatusCode.Forbidden, "Remote");
            }
        }
    }

    [Theory]
    [InlineData("192.0.2.10", "192.0.2.10")]
    [InlineData("::ffff:192.0.2.10", "192.0.2.10")]
    [InlineData("::1", "::1")]
    [InlineData("127.0.0.1", "192.0.2.10")]
    [InlineData("::ffff:127.0.0.1", "192.0.2.10")]
    public async Task Post_FromTheMachinesOwnAddress_IsAccepted(string remote, string local)
    {
        (EditingApp app, _) = await StartWithOneSourceAsync();
        await using (app)
        {
            using HttpResponseMessage response = await app.PostAsync(app.Folder("other", "O.md"), headers: EditingApp.From(remote, local));

            response.StatusCode.ShouldBe(HttpStatusCode.Created);
            app.Ids.ShouldBe(["notes", "other"]);
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
    [InlineData("Sec-Fetch-Site", "none")]
    public async Task Post_FromAnotherPageInTheBrowserOnThisMachine_Returns403CrossOriginAndChangesNothing(string header, string value)
    {
        (EditingApp app, _) = await StartWithOneSourceAsync();
        await using (app)
        {
            byte[] before = File.ReadAllBytes(app.SourcesFile);

            using HttpResponseMessage response = await app.PostAsync(app.Folder("other", "O.md"), headers: [(header, value)]);

            await ShouldBeProblemAsync(response, HttpStatusCode.Forbidden, "CrossOrigin");
            File.ReadAllBytes(app.SourcesFile).ShouldBe(before);
            app.Ids.ShouldBe(["notes"]);
        }
    }

    [Theory]
    [InlineData("Forwarded", "for=203.0.113.9;proto=https")]
    [InlineData("X-Forwarded-For", "203.0.113.9")]
    [InlineData("X-Forwarded-Host", "pusula.example")]
    [InlineData("X-Real-IP", "203.0.113.9")]
    public async Task Post_ThroughAReverseProxyOnThisMachine_Returns403RemoteAndChangesNothing(string header, string value)
    {
        (EditingApp app, _) = await StartWithOneSourceAsync();
        await using (app)
        {
            byte[] before = File.ReadAllBytes(app.SourcesFile);

            // The connection is from the loopback address, as a proxy on this machine would make it.
            using HttpResponseMessage loopback = await app.PostAsync(app.Folder("other", "O.md"), headers: [(header, value)]);
            using HttpResponseMessage ownAddress = await app.PostAsync(app.Folder("other", "O.md"), headers: [.. EditingApp.From(EditingApp.ThisMachine, EditingApp.ThisMachine), (header, value)]);
            using HttpResponseMessage badRequest = await app.PostJsonAsync("{}", [(header, value)]);

            await ShouldBeProblemAsync(loopback, HttpStatusCode.Forbidden, "Remote");
            await ShouldBeProblemAsync(ownAddress, HttpStatusCode.Forbidden, "Remote");
            await ShouldBeProblemAsync(badRequest, HttpStatusCode.Forbidden, "Remote");
            File.ReadAllBytes(app.SourcesFile).ShouldBe(before);
            app.Ids.ShouldBe(["notes"]);
        }
    }

    [Fact]
    public async Task Post_FromAPageOfThisServer_IsAccepted()
    {
        (EditingApp app, _) = await StartWithOneSourceAsync();
        await using (app)
        {
            // What a browser sends for a request that the page of this server makes: its own origin, and the fetch site.
            using HttpResponseMessage response = await app.PostAsync(
                app.Folder("other", "O.md"),
                headers: [("Origin", "http://localhost"), ("Sec-Fetch-Site", "same-origin")]);

            response.StatusCode.ShouldBe(HttpStatusCode.Created);
        }
    }

    [Fact]
    public async Task Post_FoldersGivenOutsideASourcesFile_Returns403CommandLine()
    {
        await using var app = new EditingApp();
        app.Root = app.Folder("given", "G.md");
        await app.StartAsync();

        using HttpResponseMessage response = await app.PostAsync(app.Folder("other", "O.md"));

        await ShouldBeProblemAsync(response, HttpStatusCode.Forbidden, "CommandLine");
        app.Ids.ShouldBe(["given"]);
        File.Exists(app.SourcesFile).ShouldBeFalse();
    }

    [Fact]
    public async Task Post_ReasonsForRefusing_ComeInTheOrderRemoteCommandLineCrossOrigin()
    {
        await using var app = new EditingApp();
        app.Root = app.Folder("given", "G.md");
        await app.StartAsync();
        string folder = app.Folder("other", "O.md");

        using HttpResponseMessage remote = await app.PostAsync(folder, headers: [.. EditingApp.From(EditingApp.AnotherMachine, EditingApp.ThisMachine), ("Origin", "http://evil.example")]);
        using HttpResponseMessage commandLine = await app.PostAsync(folder, headers: [("Origin", "http://evil.example")]);

        await ShouldBeProblemAsync(remote, HttpStatusCode.Forbidden, "Remote");
        await ShouldBeProblemAsync(commandLine, HttpStatusCode.Forbidden, "CommandLine");
    }

    // ---- DELETE /api/sources/{id} -------------------------------------------------------------------------------

    [Fact]
    public async Task Delete_Source_Returns204AndItIsGoneFromTheListTheFileAndItsEndpoints()
    {
        (EditingApp app, string notes) = await StartWithOneSourceAsync();
        await using (app)
        {
            string other = app.Folder("other", "O.md");
            (await app.PostAsync(other)).Dispose();
            using JsonDocument before = await app.Client.GetJsonAsync("/api/sources/notes/tree");

            using HttpResponseMessage response = await app.DeleteAsync("notes");
            using HttpResponseMessage tree = await app.Client.GetResponseAsync("/api/sources/notes/tree");
            using JsonDocument list = await app.GetSourcesAsync();

            before.RootElement.GetProperty("root").GetString().ShouldBe(notes);
            response.StatusCode.ShouldBe(HttpStatusCode.NoContent);
            (await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken)).ShouldBeEmpty();
            tree.StatusCode.ShouldBe(HttpStatusCode.NotFound);
            list.RootElement.GetProperty("sources").EnumerateArray().Select(source => source.GetProperty("id").GetString()).ShouldBe(["other"]);
            Fields(app.ReadItems().Single()).ShouldBe(["id=other", "name=other", $"path={other}", "profile=auto"]);
            File.Exists(Path.Join(notes, "A.md")).ShouldBeTrue();
        }
    }

    [Fact]
    public async Task Delete_SourceInTheMiddleOfTheFile_TakesOutThatOneAndKeepsTheOthersAsTheyWereWritten()
    {
        await using var app = new EditingApp();
        string first = app.Folder("first", "F.md");
        string second = app.Folder("second", "S.md");
        string third = app.Folder("third", "T.md");
        app.WriteSourcesFile(
            $$"""
            {
              "sources": [
                { "path": {{Json(first)}}, "profile": "vault" },
                { "path": {{Json(second)}}, "name": "Second" },
                { "path": {{Json(third)}} },
              ]
            }
            """);
        await app.StartAsync();
        app.Ids.ShouldBe(["first", "second", "third"]);

        using HttpResponseMessage response = await app.DeleteAsync("second");

        response.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        app.Ids.ShouldBe(["first", "third"]);
        JsonElement[] items = app.ReadItems();
        Fields(items[0]).ShouldBe([$"path={first}", "profile=vault"]);
        Fields(items[1]).ShouldBe([$"path={third}"]);
    }

    [Fact]
    public async Task Delete_SourceTwice_IsNotFoundTheSecondTime()
    {
        (EditingApp app, _) = await StartWithOneSourceAsync();
        await using (app)
        {
            using HttpResponseMessage first = await app.DeleteAsync("notes");
            using HttpResponseMessage second = await app.DeleteAsync("notes");

            first.StatusCode.ShouldBe(HttpStatusCode.NoContent);
            await ShouldBeProblemAsync(second, HttpStatusCode.NotFound, "NotFound");
        }
    }

    [Theory]
    [InlineData("nope")]
    [InlineData("NOTES")]
    [InlineData("..")]
    [InlineData("%2e%2e")]
    [InlineData("notes%2F..%2Fnotes")]
    public async Task Delete_IdThatNoSourceHas_Returns404AndChangesNothing(string id)
    {
        (EditingApp app, _) = await StartWithOneSourceAsync();
        await using (app)
        {
            byte[] before = File.ReadAllBytes(app.SourcesFile);

            using HttpResponseMessage response = await app.DeleteAsync(id);

            response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
            File.ReadAllBytes(app.SourcesFile).ShouldBe(before);
            app.Ids.ShouldBe(["notes"]);
        }
    }

    [Fact]
    public async Task Delete_SourceThatIsNotAvailable_Returns204()
    {
        await using var app = new EditingApp();
        app.WriteSourcesFile($$"""{ "sources": [ { "id": "gone", "path": {{Json(app.Missing("gone"))}} }, { "path": {{Json(app.Folder("a", "A.md"))}} } ] }""");
        await app.StartAsync();
        using HttpResponseMessage unavailable = await app.Client.GetResponseAsync("/api/sources/gone/tree");
        unavailable.StatusCode.ShouldBe(HttpStatusCode.ServiceUnavailable);

        using HttpResponseMessage response = await app.DeleteAsync("gone");

        response.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        app.Ids.ShouldBe(["a"]);
    }

    [Fact]
    public async Task Delete_LastSource_LeavesAnEmptyListThatIsStillAValidFile()
    {
        (EditingApp app, _) = await StartWithOneSourceAsync();
        await using (app)
        {
            using HttpResponseMessage response = await app.DeleteAsync("notes");
            using JsonDocument list = await app.GetSourcesAsync();

            response.StatusCode.ShouldBe(HttpStatusCode.NoContent);
            list.RootElement.GetProperty("sources").GetArrayLength().ShouldBe(0);
            list.RootElement.GetProperty("canEdit").GetBoolean().ShouldBeTrue();
            app.ReadItems().ShouldBeEmpty();

            // And the next one can be added.
            using HttpResponseMessage added = await app.PostAsync(app.Folder("again", "A.md"));
            added.StatusCode.ShouldBe(HttpStatusCode.Created);
            app.Ids.ShouldBe(["again"]);
        }
    }

    [Fact]
    public async Task Delete_ThenAddTheSameFolderAgain_Works()
    {
        (EditingApp app, string notes) = await StartWithOneSourceAsync();
        await using (app)
        {
            (await app.DeleteAsync("notes")).Dispose();

            using HttpResponseMessage response = await app.PostAsync(notes);

            response.StatusCode.ShouldBe(HttpStatusCode.Created);
            app.Ids.ShouldBe(["notes"]);
        }
    }

    [Fact]
    public async Task Delete_SourceWhoseEventsAreBeingRead_EndsTheStream()
    {
        (EditingApp app, _) = await StartWithOneSourceAsync();
        await using (app)
        await using (SseClient events = await SseClient.ConnectAsync(app.Client, "/api/sources/notes/events"))
        {
            (await events.NextAsync()).Type.ShouldBe("ready");
            Task<(string Type, JsonDocument Data)?> next = events.TryNextAsync(TimeSpan.FromSeconds(20));

            using HttpResponseMessage response = await app.DeleteAsync("notes");

            response.StatusCode.ShouldBe(HttpStatusCode.NoContent);
            (await next).ShouldBeNull();
        }
    }

    [Fact]
    public async Task Delete_FromAnotherMachine_Returns403RemoteAndChangesNothing()
    {
        (EditingApp app, _) = await StartWithOneSourceAsync();
        await using (app)
        {
            byte[] before = File.ReadAllBytes(app.SourcesFile);

            using HttpResponseMessage response = await app.DeleteAsync("notes", EditingApp.From(EditingApp.AnotherMachine, EditingApp.ThisMachine));
            using HttpResponseMessage unknown = await app.DeleteAsync("nope", EditingApp.From(EditingApp.AnotherMachine, EditingApp.ThisMachine));

            await ShouldBeProblemAsync(response, HttpStatusCode.Forbidden, "Remote");
            await ShouldBeProblemAsync(unknown, HttpStatusCode.Forbidden, "Remote");
            File.ReadAllBytes(app.SourcesFile).ShouldBe(before);
            app.Ids.ShouldBe(["notes"]);
        }
    }

    [Theory]
    [InlineData("Origin", "http://evil.example")]
    [InlineData("Origin", "null")]
    [InlineData("Sec-Fetch-Site", "cross-site")]
    [InlineData("Sec-Fetch-Site", "same-site")]
    public async Task Delete_FromAnotherPageInTheBrowserOnThisMachine_Returns403CrossOriginAndChangesNothing(string header, string value)
    {
        (EditingApp app, _) = await StartWithOneSourceAsync();
        await using (app)
        {
            using HttpResponseMessage response = await app.DeleteAsync("notes", [(header, value)]);

            await ShouldBeProblemAsync(response, HttpStatusCode.Forbidden, "CrossOrigin");
            app.Ids.ShouldBe(["notes"]);
        }
    }

    [Theory]
    [InlineData("Forwarded", "for=203.0.113.9")]
    [InlineData("X-Forwarded-For", "203.0.113.9")]
    [InlineData("X-Forwarded-Host", "pusula.example")]
    [InlineData("X-Real-IP", "203.0.113.9")]
    public async Task Delete_ThroughAReverseProxyOnThisMachine_Returns403RemoteAndChangesNothing(string header, string value)
    {
        (EditingApp app, _) = await StartWithOneSourceAsync();
        await using (app)
        {
            using HttpResponseMessage response = await app.DeleteAsync("notes", [(header, value)]);

            await ShouldBeProblemAsync(response, HttpStatusCode.Forbidden, "Remote");
            app.Ids.ShouldBe(["notes"]);
        }
    }

    [Fact]
    public async Task Delete_FromAPageOfThisServer_IsAccepted()
    {
        (EditingApp app, _) = await StartWithOneSourceAsync();
        await using (app)
        {
            using HttpResponseMessage response = await app.DeleteAsync("notes", [("Origin", "http://localhost"), ("Sec-Fetch-Site", "same-origin")]);

            response.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        }
    }

    [Fact]
    public async Task Delete_FoldersGivenOutsideASourcesFile_Returns403CommandLine()
    {
        await using var app = new EditingApp();
        app.Root = app.Folder("given", "G.md");
        await app.StartAsync();

        using HttpResponseMessage response = await app.DeleteAsync("given");

        await ShouldBeProblemAsync(response, HttpStatusCode.Forbidden, "CommandLine");
        app.Ids.ShouldBe(["given"]);
    }

    [Fact]
    public async Task Delete_WhileTheSourcesFileCannotBeUsed_Returns409AndLeavesTheFileAlone()
    {
        (EditingApp app, _) = await StartWithOneSourceAsync();
        await using (app)
        {
            app.WriteSourcesFile("{ nope");

            using HttpResponseMessage response = await app.DeleteAsync("notes");

            await ShouldBeProblemAsync(response, HttpStatusCode.Conflict, "FileInvalid");
            app.ReadSourcesFile().ShouldBe("{ nope");
            app.Ids.ShouldBe(["notes"]);
        }
    }

    // ---- The file is edited by hand -----------------------------------------------------------------------------

    [Fact]
    public async Task ManualEdit_SourcesFileChanged_ChangesTheListWithoutARestart()
    {
        (EditingApp app, string notes) = await StartWithOneSourceAsync();
        await using (app)
        {
            string other = app.Folder("other", "O.md");
            app.WriteSourcesFile($$"""{ "sources": [ { "id": "other", "name": "By hand", "path": {{Json(other)}} }, { "id": "notes", "path": {{Json(notes)}} } ] }""");

            await EditingApp.WaitUntilAsync(
                async () =>
                {
                    using JsonDocument json = await app.GetSourcesAsync();
                    return json.RootElement.GetProperty("sources").GetArrayLength() == 2;
                },
                "the edited sources file to be read");
            using JsonDocument list = await app.GetSourcesAsync();
            using JsonDocument tree = await app.Client.GetJsonAsync("/api/sources/other/tree");

            list.RootElement.GetProperty("sources").EnumerateArray().Select(source => source.GetProperty("name").GetString()).ShouldBe(["By hand", "notes"]);
            tree.RootElement.GetProperty("root").GetString().ShouldBe(other);

            // The next change through the page starts from what the hand wrote.
            using HttpResponseMessage response = await app.DeleteAsync("notes");
            response.StatusCode.ShouldBe(HttpStatusCode.NoContent);
            Fields(app.ReadItems().Single()).ShouldBe(["id=other", "name=By hand", $"path={other}"]);
        }
    }
}
