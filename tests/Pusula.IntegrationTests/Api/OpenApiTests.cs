using System.Net;
using System.Text.Json;
using Pusula.IntegrationTests.Support;
using Shouldly;
using Xunit;

namespace Pusula.IntegrationTests.Api;

public sealed class OpenApiTests(ApiFixture fixture) : IClassFixture<ApiFixture>
{
    [Theory]
    [InlineData("Development")]
    [InlineData("Production")]
    public async Task GetOpenApiDocument_InEveryEnvironment_Returns200(string environment)
    {
        await using var factory = new PusulaFactory(fixture.Root.Path, environment: environment);
        using HttpClient client = factory.CreateClient();

        using HttpResponseMessage response = await client.GetResponseAsync("/openapi/v1.json");
        using JsonDocument json = await response.ReadJsonAsync();

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        response.Content.Headers.ContentType?.MediaType.ShouldBe("application/json");
        json.RootElement.GetProperty("openapi").GetString()!.ShouldStartWith("3.");
    }

    [Fact]
    public async Task GetOpenApiDocument_DescribesEveryEndpointWithASummary()
    {
        using JsonDocument json = await fixture.Client.GetJsonAsync("/openapi/v1.json");
        JsonElement paths = json.RootElement.GetProperty("paths");

        paths.PropertyNames().ShouldBe(
        [
            "/api/sources", "/api/sources/{id}", "/api/sources/{source}/tree", "/api/sources/{source}/file", "/api/sources/{source}/overview",
            "/api/sources/{source}/events", "/api/browse", "/api/browse/found",
        ], ignoreOrder: true);
        paths.GetProperty("/api/sources").PropertyNames().ShouldBe(["get", "post"], ignoreOrder: true);
        paths.GetProperty("/api/sources/{id}").PropertyNames().ShouldBe(["delete"]);
        foreach (JsonProperty path in paths.EnumerateObject())
        {
            foreach (JsonProperty operation in path.Value.EnumerateObject())
            {
                string name = $"{operation.Name} {path.Name}";
                operation.Value.GetProperty("summary").GetString().ShouldNotBeNullOrWhiteSpace(name);
                operation.Value.GetProperty("description").GetString().ShouldNotBeNullOrWhiteSpace(name);
                operation.Value.GetProperty("operationId").GetString().ShouldNotBeNullOrWhiteSpace(name);
            }
        }

        paths.GetProperty("/api/sources/{source}/events").GetProperty("get").GetProperty("responses").GetProperty("200").GetProperty("content")
            .TryGetProperty("text/event-stream", out _).ShouldBeTrue();
    }

    [Fact]
    public async Task GetOpenApiDocument_FileEndpoint_DocumentsTheQueryParameterAndTheProblemResponses()
    {
        using JsonDocument json = await fixture.Client.GetJsonAsync("/openapi/v1.json");
        JsonElement get = json.RootElement.GetProperty("paths").GetProperty("/api/sources/{source}/file").GetProperty("get");

        get.GetProperty("parameters").EnumerateArray().Select(parameter => $"{parameter.GetProperty("name").GetString()}:{parameter.GetProperty("in").GetString()}")
            .ShouldBe(["source:path", "path:query"]);
        get.GetProperty("responses").PropertyNames().ShouldBe(["200", "400", "404", "503"], ignoreOrder: true);
        foreach (string status in new[] { "400", "404", "503" })
        {
            get.GetProperty("responses").GetProperty(status).GetProperty("content").TryGetProperty("application/problem+json", out _).ShouldBeTrue(status);
        }
    }

    [Fact]
    public async Task GetOpenApiDocument_AddSource_DocumentsTheBodyTheNewSourceAndEveryProblem()
    {
        using JsonDocument json = await fixture.Client.GetJsonAsync("/openapi/v1.json");
        JsonElement post = json.RootElement.GetProperty("paths").GetProperty("/api/sources").GetProperty("post");

        post.GetProperty("requestBody").GetProperty("content").PropertyNames().ShouldBe(["application/json"]);
        post.GetProperty("requestBody").GetProperty("content").GetProperty("application/json").GetRawText().ShouldContain("CreateSourceRequest");
        post.GetProperty("responses").PropertyNames().ShouldBe(["201", "400", "403", "409", "500"], ignoreOrder: true);
        post.GetProperty("responses").GetProperty("201").GetProperty("content").GetProperty("application/json").GetRawText().ShouldContain("SourceResponse");
        foreach (string status in new[] { "400", "403", "409", "500" })
        {
            post.GetProperty("responses").GetProperty(status).GetProperty("content").TryGetProperty("application/problem+json", out _).ShouldBeTrue(status);
        }

        // The codes of the problems are part of the contract, and they are not in the schema: the description lists them.
        string description = post.GetProperty("description").GetString()!;
        foreach (string code in new[]
        {
            "PathRequired", "PathNotAbsolute", "FolderNotFound", "TooBroad", "InvalidName", "InvalidProfile", "Remote", "CommandLine",
            "CrossOrigin", "AlreadyListed", "FileInvalid", "WriteFailed",
        })
        {
            description.ShouldContain(code);
        }

        // What makes a request "Remote" and what a folder that is too large does are told too.
        foreach (string text in new[] { "Forwarded", "X-Forwarded-For", "X-Forwarded-Host", "X-Real-IP", "too large", "'available' false" })
        {
            description.ShouldContain(text);
        }
    }

    [Fact]
    public async Task GetOpenApiDocument_ListOfSources_TellsWhatMakesASourceNotAvailableAndWhoCanEdit()
    {
        using JsonDocument json = await fixture.Client.GetJsonAsync("/openapi/v1.json");
        JsonElement schemas = json.RootElement.GetProperty("components").GetProperty("schemas");
        string description = json.RootElement.GetProperty("paths").GetProperty("/api/sources").GetProperty("get").GetProperty("description").GetString()!;

        foreach (string text in new[] { "too large", "20,000 Markdown files", "50,000 folders", "256 MiB", "X-Forwarded-For", "editBlocked", "canEdit" })
        {
            description.ShouldContain(text);
        }

        schemas.GetProperty("SourceResponse").GetProperty("properties").GetProperty("error").GetProperty("description").GetString()!.ShouldContain("too large");
        schemas.GetProperty("SourceResponse").GetProperty("properties").GetProperty("available").GetProperty("description").GetString()!.ShouldContain("too large");
    }

    [Fact]
    public async Task GetOpenApiDocument_ListOfSources_TellsTheMachineTheRemoteEditSettingAndTheErrorCodes()
    {
        using JsonDocument json = await fixture.Client.GetJsonAsync("/openapi/v1.json");
        JsonElement paths = json.RootElement.GetProperty("paths");

        string list = paths.GetProperty("/api/sources").GetProperty("get").GetProperty("description").GetString()!;
        foreach (string text in new[] { "'machine'", "'version'", "pusula --version", "'remoteEdit'", "Pusula:AllowRemoteEdit", "'errorCode'", "FolderMissing", "NotReadable", "TooLarge", "'code'" })
        {
            list.ShouldContain(text);
        }

        // Adding and removing say what the setting lifts and what it does not.
        string add = paths.GetProperty("/api/sources").GetProperty("post").GetProperty("description").GetString()!;
        foreach (string text in new[] { "Pusula:AllowRemoteEdit", "anyone who can reach the server", "the page and the sources file are asked for all the same" })
        {
            add.ShouldContain(text);
        }

        paths.GetProperty("/api/sources/{id}").GetProperty("delete").GetProperty("description").GetString()!.ShouldContain("Pusula:AllowRemoteEdit");
    }

    [Fact]
    public async Task GetOpenApiDocument_SourceErrorCode_IsDocumentedOnTheSourceAndLeftOutWhenItIsAvailable()
    {
        using JsonDocument json = await fixture.Client.GetJsonAsync("/openapi/v1.json");
        JsonElement source = json.RootElement.GetProperty("components").GetProperty("schemas").GetProperty("SourceResponse");
        JsonElement errorCode = source.GetProperty("properties").GetProperty("errorCode");

        // The codes are part of the contract, as text (like editBlocked): the description lists them, and says where else they show up.
        string description = errorCode.GetProperty("description").GetString()!;
        foreach (string text in new[] { "FolderMissing", "NotReadable", "TooLarge", "503", "code" })
        {
            description.ShouldContain(text);
        }

        errorCode.GetProperty("type").EnumerateArray().Select(type => type.GetString()).ShouldBe(["null", "string"], ignoreOrder: true);
        source.GetProperty("properties").PropertyNames().ShouldBe(["id", "name", "path", "profile", "available", "fileCount", "error", "errorCode"], ignoreOrder: true);
        source.GetProperty("required").EnumerateArray().Select(name => name.GetString()).ShouldBe(["id", "name", "path", "profile", "available", "fileCount"], ignoreOrder: true);
    }

    [Fact]
    public async Task GetOpenApiDocument_Overview_DocumentsTheEntryNoteTheRecentNotesAndTheMostLinkedOnes()
    {
        using JsonDocument json = await fixture.Client.GetJsonAsync("/openapi/v1.json");
        JsonElement schemas = json.RootElement.GetProperty("components").GetProperty("schemas");
        JsonElement overview = schemas.GetProperty("OverviewResponse");
        JsonElement properties = overview.GetProperty("properties");

        // The lists are always there (empty for a Claude Code folder); the entry note is only there when there is one.
        overview.GetProperty("required").EnumerateArray().Select(name => name.GetString()).ShouldContain("recent");
        overview.GetProperty("required").EnumerateArray().Select(name => name.GetString()).ShouldContain("mostLinked");
        overview.GetProperty("required").EnumerateArray().Select(name => name.GetString()).ShouldNotContain("entry");
        properties.GetProperty("entry").GetRawText().ShouldContain("OverviewEntryNote");
        foreach (string property in new[] { "entry", "recent", "mostLinked", "orphans" })
        {
            properties.GetProperty(property).GetRawText().ShouldContain("description", Case.Sensitive, property);
        }

        schemas.GetProperty("OverviewEntryNote").GetProperty("properties").PropertyNames().ShouldBe(["path", "title"], ignoreOrder: true);
        schemas.GetProperty("OverviewRecentNote").GetProperty("properties").PropertyNames().ShouldBe(["path", "modifiedAt"], ignoreOrder: true);
        schemas.GetProperty("OverviewRecentNote").GetProperty("properties").GetProperty("modifiedAt").GetProperty("format").GetString().ShouldBe("date-time");
        schemas.GetProperty("OverviewMostLinkedNote").GetProperty("properties").PropertyNames().ShouldBe(["path", "count"], ignoreOrder: true);
        foreach (string name in new[] { "OverviewEntryNote", "OverviewRecentNote", "OverviewMostLinkedNote" })
        {
            schemas.GetProperty(name).GetProperty("description").GetString().ShouldNotBeNullOrWhiteSpace(name);
            foreach (JsonProperty property in schemas.GetProperty(name).GetProperty("properties").EnumerateObject())
            {
                property.Value.GetProperty("description").GetString().ShouldNotBeNullOrWhiteSpace($"{name}.{property.Name}");
            }
        }

        string description = json.RootElement.GetProperty("paths").GetProperty("/api/sources/{source}/overview").GetProperty("get").GetProperty("description").GetString()!;
        foreach (string text in new[] { "'entry'", "'recent'", "'mostLinked'", "modifiedAt", "orphan", "no links at all" })
        {
            description.ShouldContain(text);
        }
    }

    [Fact]
    public async Task GetOpenApiDocument_RemoveSource_DocumentsTheIdTheEmptyAnswerAndEveryProblem()
    {
        using JsonDocument json = await fixture.Client.GetJsonAsync("/openapi/v1.json");
        JsonElement delete = json.RootElement.GetProperty("paths").GetProperty("/api/sources/{id}").GetProperty("delete");

        delete.GetProperty("parameters").EnumerateArray().Select(parameter => $"{parameter.GetProperty("name").GetString()}:{parameter.GetProperty("in").GetString()}")
            .ShouldBe(["id:path"]);
        delete.GetProperty("responses").PropertyNames().ShouldBe(["204", "403", "404", "409", "500"], ignoreOrder: true);
        delete.GetProperty("responses").GetProperty("204").TryGetProperty("content", out _).ShouldBeFalse();
        foreach (string status in new[] { "403", "404", "409", "500" })
        {
            delete.GetProperty("responses").GetProperty(status).GetProperty("content").TryGetProperty("application/problem+json", out _).ShouldBeTrue(status);
        }

        string description = delete.GetProperty("description").GetString()!;
        foreach (string code in new[] { "Remote", "CommandLine", "CrossOrigin", "NotFound", "FileInvalid", "WriteFailed" })
        {
            description.ShouldContain(code);
        }
    }

    [Fact]
    public async Task GetOpenApiDocument_SourceSchemas_DocumentTheRequestAndWhatTheListSaysAboutEditing()
    {
        using JsonDocument json = await fixture.Client.GetJsonAsync("/openapi/v1.json");
        JsonElement schemas = json.RootElement.GetProperty("components").GetProperty("schemas");
        JsonElement request = schemas.GetProperty("CreateSourceRequest");
        JsonElement list = schemas.GetProperty("SourcesResponse");

        request.GetProperty("description").GetString().ShouldNotBeNullOrWhiteSpace();
        request.GetProperty("properties").PropertyNames().ShouldBe(["path", "name", "profile"], ignoreOrder: true);
        foreach (JsonProperty property in request.GetProperty("properties").EnumerateObject())
        {
            property.Value.GetProperty("description").GetString().ShouldNotBeNullOrWhiteSpace(property.Name);
        }

        list.GetProperty("properties").PropertyNames().ShouldBe(["sources", "canEdit", "machine", "remoteEdit", "version", "sourcesFile", "editBlocked", "sourcesFileError"], ignoreOrder: true);
        foreach (JsonProperty property in list.GetProperty("properties").EnumerateObject())
        {
            property.Value.GetProperty("description").GetString().ShouldNotBeNullOrWhiteSpace(property.Name);
        }

        list.GetProperty("required").EnumerateArray().Select(name => name.GetString()).ShouldBe(["sources", "canEdit", "machine", "remoteEdit", "version"], ignoreOrder: true);
        list.GetProperty("properties").GetProperty("canEdit").GetProperty("type").GetString().ShouldBe("boolean");
        list.GetProperty("properties").GetProperty("editBlocked").GetProperty("description").GetString()!.ShouldContain("Remote");
        list.GetProperty("properties").GetProperty("editBlocked").GetProperty("description").GetString()!.ShouldContain("CommandLine");
    }

    [Fact]
    public async Task GetOpenApiDocument_Browse_DocumentsTheQueryTheAnswerAndEveryProblem()
    {
        using JsonDocument json = await fixture.Client.GetJsonAsync("/openapi/v1.json");
        JsonElement paths = json.RootElement.GetProperty("paths");
        JsonElement get = paths.GetProperty("/api/browse").GetProperty("get");

        paths.GetProperty("/api/browse").PropertyNames().ShouldBe(["get"]);
        get.GetProperty("operationId").GetString().ShouldBe("BrowseFolders");
        get.GetProperty("parameters").EnumerateArray().Select(parameter => $"{parameter.GetProperty("name").GetString()}:{parameter.GetProperty("in").GetString()}")
            .ShouldBe(["path:query", "hidden:query"]);
        get.GetProperty("responses").PropertyNames().ShouldBe(["200", "400", "403", "404"], ignoreOrder: true);
        get.GetProperty("responses").GetProperty("200").GetProperty("content").GetProperty("application/json").GetRawText().ShouldContain("BrowseResponse");
        foreach (string status in new[] { "400", "403", "404" })
        {
            get.GetProperty("responses").GetProperty(status).GetProperty("content").TryGetProperty("application/problem+json", out _).ShouldBeTrue(status);
        }

        // The codes of the problems are part of the contract, and they are not in the schema: the description lists them.
        string description = get.GetProperty("description").GetString()!;
        foreach (string text in new[]
        {
            "PathNotAbsolute", "FolderNotFound", "Remote", "CommandLine", "CrossOrigin", "hidden=1", "node_modules", "truncated", "markdownCount", "more",
            "listed", "Vault", "Claude", "Turkish", "Pusula:AllowRemoteEdit", "Sec-Fetch-Site", "never the name or the content of a file", "X-Forwarded-For",
            "2,000 file system entries", "the file count of a source over it", "'plugins' and 'sessions'", "Hidden or System attribute",
        })
        {
            description.ShouldContain(text);
        }
    }

    [Fact]
    public async Task GetOpenApiDocument_Found_DocumentsTheAnswerTheBudgetAndTheRefusals()
    {
        using JsonDocument json = await fixture.Client.GetJsonAsync("/openapi/v1.json");
        JsonElement get = json.RootElement.GetProperty("paths").GetProperty("/api/browse/found").GetProperty("get");

        get.GetProperty("operationId").GetString().ShouldBe("FindFolders");
        get.TryGetProperty("parameters", out _).ShouldBeFalse();
        get.GetProperty("responses").PropertyNames().ShouldBe(["200", "403"], ignoreOrder: true);
        get.GetProperty("responses").GetProperty("200").GetProperty("content").GetProperty("application/json").GetRawText().ShouldContain("FoundResponse");
        get.GetProperty("responses").GetProperty("403").GetProperty("content").TryGetProperty("application/problem+json", out _).ShouldBeTrue();

        string description = get.GetProperty("description").GetString()!;
        foreach (string text in new[] { ".obsidian", "/mnt", "/media", "/Volumes", "complete", "60 seconds", "node_modules", "100,000", "listed", "Remote", "CommandLine", "CrossOrigin", "by 'name'", "for the same name, by 'display'", "same limit for one folder", "every ready fixed or removable drive", "Hidden or System attribute" })
        {
            description.ShouldContain(text);
        }

        // The folders of notes that are recognized by what is in them: the rules, and what the budget does to them.
        foreach (string text in new[]
        {
            "kind Notes", "Home.md, index.md, README.md, MOC.md, _index.md or start.md", "at least 10 Markdown files", "up to 3 levels below it",
            "at least half of the files", "at least a fifth of a sample", "'[[' in the first 4 KB", "at most 20 notes", "at least 60% of its wikilinked notes",
            "A vault is never left out", "as deep as a vault is (4 levels) under the home directory and, on Windows, on the system drive",
            "asked for an entry note by reading its first 200 entries", "one that has none among them is taken to have none",
            "Below /mnt, /media and /Volumes and on the other drives of Windows the folders of the last level are not asked",
            "an external disk may sleep or be slow", "down to one level above the last (3 levels, 2 below /Volumes)", "a vault is found at every level",
            "the shallowest first", "one that was not looked into is not listed",
            "The folders of the last level are asked after the others", "one entry for the folder and one for each entry read",
            "the ones inside a folder of notes first", "one that was not asked is not listed", "Every entry that the search reads is taken from the budget",
        })
        {
            description.ShouldContain(text);
        }
    }

    [Fact]
    public async Task GetOpenApiDocument_BrowseSchemas_AreDocumentedAndOnlyRequireWhatIsAlwaysThere()
    {
        using JsonDocument json = await fixture.Client.GetJsonAsync("/openapi/v1.json");
        JsonElement schemas = json.RootElement.GetProperty("components").GetProperty("schemas");
        JsonElement browse = schemas.GetProperty("BrowseResponse");
        JsonElement folder = schemas.GetProperty("BrowseFolder");
        JsonElement found = schemas.GetProperty("FoundResponse");

        browse.GetProperty("properties").PropertyNames().ShouldBe(["path", "display", "home", "folders", "truncated", "parent"], ignoreOrder: true);
        browse.GetProperty("required").EnumerateArray().Select(name => name.GetString()).ShouldBe(["path", "display", "home", "folders", "truncated"], ignoreOrder: true);
        folder.GetProperty("properties").PropertyNames().ShouldBe(["name", "path", "display", "listed", "kind", "markdownCount", "more"], ignoreOrder: true);
        folder.GetProperty("required").EnumerateArray().Select(name => name.GetString()).ShouldBe(["name", "path", "display", "listed"], ignoreOrder: true);
        found.GetProperty("properties").PropertyNames().ShouldBe(["folders", "complete"], ignoreOrder: true);
        found.GetProperty("required").EnumerateArray().Select(name => name.GetString()).ShouldBe(["folders", "complete"], ignoreOrder: true);

        foreach ((string name, JsonElement schema) in new[] { ("BrowseResponse", browse), ("BrowseFolder", folder), ("FoundResponse", found) })
        {
            schema.GetProperty("description").GetString().ShouldNotBeNullOrWhiteSpace(name);
            foreach (JsonProperty property in schema.GetProperty("properties").EnumerateObject())
            {
                // The description of a property that is an enum sits next to the reference to the enum.
                property.Value.GetRawText().ShouldContain("\"description\"", Case.Sensitive, $"{name}.{property.Name}");
            }
        }

        // The kind of a folder is its own enum, not the profile of a source: a folder of notes has a kind that no profile has.
        folder.GetProperty("properties").GetProperty("kind").GetRawText().ShouldContain("FolderKind");
        folder.GetProperty("properties").GetProperty("kind").GetRawText().ShouldContain("Notes");
        folder.GetProperty("properties").GetProperty("kind").GetRawText().ShouldNotContain("SourceProfile");
    }

    [Fact]
    public async Task GetOpenApiDocument_Enums_AreStringsWithoutNull()
    {
        using JsonDocument json = await fixture.Client.GetJsonAsync("/openapi/v1.json");
        JsonElement schemas = json.RootElement.GetProperty("components").GetProperty("schemas");

        string[] Values(string name) => [.. schemas.GetProperty(name).GetProperty("enum").EnumerateArray().Select(value => value.GetString()!)];

        Values("Layer").ShouldBe(["ClaudeMd", "Rule", "PathRule", "Skill", "SkillResource", "Agent", "Command", "OutputStyle", "MemoryIndex", "Memory", "Reference", "Shared", "Other", "Note"]);
        Values("LoadMode").ShouldBe(["EverySession", "DescriptionEverySession", "ProjectSession", "Conditional", "OnDemand", "UserInvoked", "Inactive"]);
        Values("LinkKind").ShouldBe(["MarkdownLink", "WikiLink", "ClaudePath", "RelativePath", "Embed"]);
        Values("SourceProfile").ShouldBe(["Claude", "Vault", "Markdown"]);
        Values("FolderKind").ShouldBe(["Vault", "Claude", "Notes"]);
        Values("LinkStatus").ShouldBe(["Resolved", "NonMarkdown", "Broken", "Pending", "External"]);
    }

    [Fact]
    public async Task GetOpenApiDocument_Schemas_CarryTheXmlDocumentationAndOnlyRequireWhatIsAlwaysThere()
    {
        using JsonDocument json = await fixture.Client.GetJsonAsync("/openapi/v1.json");
        JsonElement schemas = json.RootElement.GetProperty("components").GetProperty("schemas");
        JsonElement file = schemas.GetProperty("FileResponse");

        file.GetProperty("description").GetString().ShouldNotBeNullOrWhiteSpace();
        file.GetProperty("properties").GetProperty("backlinks").GetProperty("description").GetString().ShouldNotBeNullOrWhiteSpace();
        schemas.GetProperty("Layer").GetProperty("description").GetString().ShouldNotBeNullOrWhiteSpace();
        schemas.GetProperty("OverviewResponse").GetProperty("properties").GetProperty("layers").GetProperty("description").GetString().ShouldNotBeNullOrWhiteSpace();

        string[] required = [.. file.GetProperty("required").EnumerateArray().Select(name => name.GetString()!)];
        required.ShouldContain("path");
        required.ShouldNotContain("frontmatter");
        required.ShouldNotContain("frontmatterError");
    }

    [Fact]
    public async Task GetOpenApiDocument_TreeNode_IsADirectoryOrAFileTellApartByType()
    {
        using JsonDocument json = await fixture.Client.GetJsonAsync("/openapi/v1.json");
        JsonElement node = json.RootElement.GetProperty("components").GetProperty("schemas").GetProperty("TreeNode");

        node.GetProperty("discriminator").GetProperty("propertyName").GetString().ShouldBe("type");
        node.GetProperty("discriminator").GetProperty("mapping").PropertyNames().ShouldBe(["Directory", "File"], ignoreOrder: true);
    }

    [Fact]
    public async Task GetOpenApiDocument_EverySessionTokens_AreDocumentedOnBothKindsOfTreeNode()
    {
        using JsonDocument json = await fixture.Client.GetJsonAsync("/openapi/v1.json");
        JsonElement schemas = json.RootElement.GetProperty("components").GetProperty("schemas");
        JsonElement mapping = schemas.GetProperty("TreeNode").GetProperty("discriminator").GetProperty("mapping");

        foreach (string type in new[] { "Directory", "File" })
        {
            // The mapping points at the schema of each kind ("#/components/schemas/<name>"); the name is the generator's choice.
            string reference = mapping.GetProperty(type).GetString()!;
            JsonElement node = schemas.GetProperty(reference[(reference.LastIndexOf('/') + 1)..]);
            JsonElement property = node.GetProperty("properties").GetProperty("everySessionTokens");

            property.GetProperty("type").GetString().ShouldBe("integer", type);
            property.GetProperty("description").GetString().ShouldNotBeNullOrWhiteSpace(type);
            node.GetProperty("required").EnumerateArray().Select(name => name.GetString()).ShouldContain("everySessionTokens", type);
        }
    }

    [Fact]
    public async Task GetOpenApiDocument_FileEndpoint_DescribesWhereTheFrontmatterErrorIs()
    {
        using JsonDocument json = await fixture.Client.GetJsonAsync("/openapi/v1.json");
        JsonElement get = json.RootElement.GetProperty("paths").GetProperty("/api/sources/{source}/file").GetProperty("get");

        string description = get.GetProperty("description").GetString()!;
        description.ShouldContain("frontmatterErrorLine");
        description.ShouldContain("frontmatterErrorText");
    }

    [Fact]
    public async Task GetOpenApiDocument_FrontmatterErrorLocation_IsDocumentedAndOptional()
    {
        using JsonDocument json = await fixture.Client.GetJsonAsync("/openapi/v1.json");
        JsonElement schemas = json.RootElement.GetProperty("components").GetProperty("schemas");
        JsonElement file = schemas.GetProperty("FileResponse");
        JsonElement overviewError = schemas.GetProperty("OverviewFrontmatterError");

        foreach (string property in new[] { "frontmatterErrorLine", "frontmatterErrorText" })
        {
            file.GetProperty("properties").GetProperty(property).GetProperty("description").GetString().ShouldNotBeNullOrWhiteSpace(property);
            file.GetProperty("required").EnumerateArray().Select(name => name.GetString()).ShouldNotContain(property);
        }

        overviewError.GetProperty("properties").PropertyNames().ShouldBe(["path", "error", "line"], ignoreOrder: true);
        overviewError.GetProperty("properties").GetProperty("line").GetProperty("description").GetString().ShouldNotBeNullOrWhiteSpace();
        overviewError.GetProperty("required").EnumerateArray().Select(name => name.GetString()).ShouldBe(["path", "error"], ignoreOrder: true);
    }

    [Fact]
    public async Task GetOpenApiDocument_BacklinkExcerpt_IsDocumentedAndOptional()
    {
        using JsonDocument json = await fixture.Client.GetJsonAsync("/openapi/v1.json");
        JsonElement backlink = json.RootElement.GetProperty("components").GetProperty("schemas").GetProperty("FileBacklink");

        backlink.GetProperty("properties").PropertyNames().ShouldBe(["source", "kind", "line", "excerpt"], ignoreOrder: true);
        backlink.GetProperty("properties").GetProperty("excerpt").GetProperty("description").GetString().ShouldNotBeNullOrWhiteSpace();
        backlink.GetProperty("required").EnumerateArray().Select(name => name.GetString()).ShouldBe(["source", "kind", "line"], ignoreOrder: true);
    }
}
