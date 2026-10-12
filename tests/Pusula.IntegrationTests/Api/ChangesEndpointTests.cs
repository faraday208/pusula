using System.Net;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Pusula.History;
using Pusula.Indexing;
using Pusula.IntegrationTests.Support;
using Pusula.Startup;
using Shouldly;
using Xunit;

namespace Pusula.IntegrationTests.Api;

/// <summary>
/// <c>GET /api/sources/{source}/changes</c>: what changed since the daily summaries, and where they are kept. The application runs over
/// made-up folders: a synthetic Claude Code folder that is shown, and a state directory in a scratch directory of its own (never the
/// one of whoever runs the test), with a clock that tells the day and moves when a test says so.
/// </summary>
public sealed class ChangesEndpointTests(ApiFixture fixture) : IClassFixture<ApiFixture>
{
    private static readonly DateTimeOffset Morning = new(2026, 10, 12, 9, 14, 3, TimeSpan.Zero);

    private static JsonElement[] Flatten(JsonElement nodes) =>
    [
        .. nodes.EnumerateArray().SelectMany(node =>
            node.GetProperty("type").GetString() == "Directory" ? Flatten(node.GetProperty("children")) : [node]),
    ];

    // Every file and folder below a folder, with what a change to it would change: its name, its size and when it was written.
    private static string[] Listing(string folder) =>
    [
        .. new DirectoryInfo(folder).EnumerateFileSystemInfos("*", SearchOption.AllDirectories)
            .Select(info => $"{Path.GetRelativePath(folder, info.FullName)}|{(info is FileInfo file ? file.Length : -1)}|{info.LastWriteTimeUtc.Ticks}")
            .Order(StringComparer.Ordinal),
    ];

    private static string[] Names(string folder) =>
        Directory.Exists(folder)
            ? [.. Directory.GetFileSystemEntries(folder, "*", SearchOption.AllDirectories).Select(path => Path.GetRelativePath(folder, path)).Order(StringComparer.Ordinal)]
            : [];

    private static string[] Paths(JsonElement files) => [.. files.EnumerateArray().Select(file => file.GetProperty("path").GetString()!)];

    // The synthetic folder is shown; the state directory is in a scratch directory of its own, unless a test says it is inside the shown folder.
    private sealed class App : IAsyncDisposable
    {
        private readonly TempDirectory _temp = new();
        private readonly PusulaFactory _factory;

        public App(bool stateInsideTheShownFolder = false)
        {
            Root = new SyntheticRoot();
            State = stateInsideTheShownFolder ? Path.Join(Root.Path, "state") : _temp.Resolve("state");
            Clock = new ManualTimeProvider(Morning, TimeZoneInfo.Utc);
            _factory = new PusulaFactory(
                Root.Path,
                directories: new UserDirectories(_temp.CreateDirectory("home"), _temp.CreateDirectory("data"), State),
                configureServices: services => services.AddSingleton<TimeProvider>(Clock));
            Client = _factory.CreateClient();
        }

        public SyntheticRoot Root { get; }

        public string State { get; }

        public ManualTimeProvider Clock { get; }

        public HttpClient Client { get; }

        public string HistoryFolder => Path.Join(State, "pusula", "history");

        public string StoredFile => Path.Join(HistoryFolder, HistoryFile.NameOf(SourceProfile.Claude, Path.TrimEndingDirectorySeparator(Path.GetFullPath(Root.Path))));

        public string Api(string endpoint) => _factory.Api(endpoint);

        public Task<HttpResponseMessage> GetChangesAsync() => Client.GetResponseAsync(Api("changes"));

        public async Task<JsonDocument> ChangesAsync()
        {
            using HttpResponseMessage response = await GetChangesAsync();
            response.StatusCode.ShouldBe(HttpStatusCode.OK);
            return await response.ReadJsonAsync();
        }

        public async ValueTask DisposeAsync()
        {
            Client.Dispose();
            await _factory.DisposeAsync();
            Root.Dispose();
            _temp.Dispose();
        }
    }

    // ---- The answer ---------------------------------------------------------------------------------------------

    [Fact]
    public async Task GetChanges_FirstRequestOfTheDay_IsKeptWithTheFirstDayAndNothingToCompareWith()
    {
        await using var app = new App();

        using HttpResponseMessage response = await app.GetChangesAsync();
        using JsonDocument json = await response.ReadJsonAsync();

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        response.Content.Headers.ContentType?.MediaType.ShouldBe("application/json");
        json.RootElement.PropertyNames().ShouldBe(["history", "firstDay"], ignoreOrder: true);
        json.RootElement.GetProperty("history").GetString().ShouldBe("Kept");
        json.RootElement.GetProperty("firstDay").GetString().ShouldBe("2026-10-12");
    }

    [Fact]
    public async Task GetChanges_FirstRequestOfTheDay_WritesOneFileToTheStateDirectoryAndNothingToTheShownFolder()
    {
        await using var app = new App();
        string[] before = Listing(app.Root.Path);

        using JsonDocument json = await app.ChangesAsync();

        json.RootElement.GetProperty("history").GetString().ShouldBe("Kept");
        Listing(app.Root.Path).ShouldBe(before);
        string name = HistoryFile.NameOf(SourceProfile.Claude, Path.TrimEndingDirectorySeparator(Path.GetFullPath(app.Root.Path)));
        Names(app.State).ShouldBe(["pusula", Path.Join("pusula", "history"), Path.Join("pusula", "history", name)]);
        using JsonDocument file = JsonDocument.Parse(await File.ReadAllTextAsync(app.StoredFile, TestContext.Current.CancellationToken));
        file.RootElement.GetProperty("version").GetInt32().ShouldBe(1);
        file.RootElement.GetProperty("root").GetString().ShouldBe(app.Root.Path);
        file.RootElement.GetProperty("profile").GetString().ShouldBe("Claude");
        JsonElement day = file.RootElement.GetProperty("days").EnumerateArray().Single();
        day.GetProperty("day").GetString().ShouldBe("2026-10-12");
        day.GetProperty("files").EnumerateObject().Select(entry => entry.Name).ShouldBe(SyntheticRoot.IndexedFiles, ignoreOrder: true);
    }

    [Fact]
    public async Task GetChanges_SettingsCredentialsAndHiddenFiles_AreNotInTheHistoryFile()
    {
        await using var app = new App();

        using JsonDocument json = await app.ChangesAsync();

        string text = await File.ReadAllTextAsync(app.StoredFile, TestContext.Current.CancellationToken);
        foreach (string secret in new[] { SyntheticRoot.SettingsSecret, SyntheticRoot.CredentialsSecret, SyntheticRoot.OutsideSecret, "settings.json", ".credentials.json", "secret.md" })
        {
            text.ShouldNotContain(secret);
        }
    }

    [Fact]
    public async Task GetChanges_SecondRequestOfTheDay_WritesNothingMore()
    {
        await using var app = new App();
        using (await app.ChangesAsync())
        {
        }

        string[] before = Listing(app.State);
        app.Clock.Advance(TimeSpan.FromHours(6));
        using JsonDocument json = await app.ChangesAsync();

        json.RootElement.PropertyNames().ShouldBe(["history", "firstDay"], ignoreOrder: true);
        Listing(app.State).ShouldBe(before);
    }

    [Fact]
    public async Task GetChanges_TheNextDay_ComparesWithTheDayBeforeAndHasNothingToTellWhenNothingChanged()
    {
        await using var app = new App();
        using (await app.ChangesAsync())
        {
        }

        app.Clock.Advance(TimeSpan.FromDays(1));
        using JsonDocument json = await app.ChangesAsync();

        json.RootElement.PropertyNames().ShouldBe(["history", "firstDay", "previousDay"], ignoreOrder: true);
        json.RootElement.GetProperty("firstDay").GetString().ShouldBe("2026-10-12");
        JsonElement previous = json.RootElement.GetProperty("previousDay");
        previous.PropertyNames().ShouldBe(
        [
            "day", "takenAt", "filesBefore", "filesAfter", "everySessionTokensBefore", "everySessionTokensAfter", "added", "removed", "resized",
            "newlyBroken", "noLongerBroken",
        ], ignoreOrder: true);
        previous.GetProperty("day").GetString().ShouldBe("2026-10-12");
        previous.GetProperty("takenAt").GetDateTimeOffset().ShouldBe(Morning);
        previous.GetProperty("filesBefore").GetInt32().ShouldBe(SyntheticRoot.IndexedFiles.Count);
        previous.GetProperty("filesAfter").GetInt32().ShouldBe(SyntheticRoot.IndexedFiles.Count);
        previous.GetProperty("everySessionTokensAfter").GetInt32().ShouldBe(previous.GetProperty("everySessionTokensBefore").GetInt32());
        foreach (string list in new[] { "added", "removed", "resized", "newlyBroken", "noLongerBroken" })
        {
            previous.GetProperty(list).GetArrayLength().ShouldBe(0, list);
        }
    }

    // A history file that another run (or another person) made for yesterday: the answer says what is different now.
    [Fact]
    public async Task GetChanges_FileOfYesterday_GivesThePreviousDayWithWhatIsDifferentNow()
    {
        await using var app = new App();
        using JsonDocument tree = await app.Client.GetJsonAsync(app.Api("tree"));
        using JsonDocument overview = await app.Client.GetJsonAsync(app.Api("overview"));
        JsonElement[] files = Flatten(tree.RootElement.GetProperty("nodes"));
        JsonElement claude = files.Single(file => file.GetProperty("path").GetString() == "CLAUDE.md");
        JsonElement reference = files.Single(file => file.GetProperty("path").GetString() == "reference.md");
        int claudeTokens = claude.GetProperty("tokens").GetInt32();
        int claudeEverySession = claude.GetProperty("everySessionTokens").GetInt32();
        claudeEverySession.ShouldBeGreaterThan(10);

        // Yesterday: CLAUDE.md was ten tokens smaller, reference.md was not there, another file was, and a link was broken that no longer is.
        var yesterdayFiles = new Dictionary<string, int[]>(StringComparer.Ordinal);
        foreach (JsonElement file in files.Where(file => file.GetProperty("path").GetString() != "reference.md"))
        {
            yesterdayFiles[file.GetProperty("path").GetString()!] = [file.GetProperty("tokens").GetInt32(), file.GetProperty("everySessionTokens").GetInt32()];
        }

        yesterdayFiles["CLAUDE.md"] = [claudeTokens - 10, claudeEverySession - 10];
        yesterdayFiles["rules/retired.md"] = [300, 300];
        JsonElement[] brokenNow = [.. overview.RootElement.GetProperty("broken").EnumerateArray()];
        brokenNow.ShouldNotBeEmpty();
        string[][] brokenYesterday =
        [
            ["CLAUDE.md", "MarkdownLink", "rules/gone.md"],
            ["rules/style.md", "WikiLink", "fixed-since"],
        ];
        string root = JsonSerializer.Serialize(app.Root.Path);
        string takenAt = JsonSerializer.Serialize(Morning.AddDays(-1));
        Directory.CreateDirectory(app.HistoryFolder);
        await File.WriteAllTextAsync(
            app.StoredFile,
            $$"""{"version":1,"root":{{root}},"profile":"Claude","days":[{"day":"2026-10-11","takenAt":{{takenAt}},"files":{{JsonSerializer.Serialize(yesterdayFiles)}},"broken":{{JsonSerializer.Serialize(brokenYesterday)}}}]}""",
            TestContext.Current.CancellationToken);

        using JsonDocument json = await app.ChangesAsync();

        json.RootElement.GetProperty("history").GetString().ShouldBe("Kept");
        json.RootElement.GetProperty("firstDay").GetString().ShouldBe("2026-10-11");
        json.RootElement.TryGetProperty("previousWeek", out _).ShouldBeFalse();
        JsonElement previous = json.RootElement.GetProperty("previousDay");
        previous.GetProperty("day").GetString().ShouldBe("2026-10-11");
        previous.GetProperty("takenAt").GetDateTimeOffset().ShouldBe(Morning.AddDays(-1));
        previous.GetProperty("filesBefore").GetInt32().ShouldBe(files.Length);
        previous.GetProperty("filesAfter").GetInt32().ShouldBe(files.Length);

        // Added: reference.md, with what it is now.
        JsonElement added = previous.GetProperty("added").EnumerateArray().Single();
        added.GetProperty("path").GetString().ShouldBe("reference.md");
        added.GetProperty("tokens").GetInt32().ShouldBe(reference.GetProperty("tokens").GetInt32());
        added.GetProperty("everySessionTokens").GetInt32().ShouldBe(reference.GetProperty("everySessionTokens").GetInt32());
        added.GetProperty("loadMode").GetString().ShouldBe(reference.GetProperty("loadMode").GetString());

        // Removed: the file that was there yesterday, with what it was, and no load mode, for it is gone.
        JsonElement removed = previous.GetProperty("removed").EnumerateArray().Single();
        removed.PropertyNames().ShouldBe(["path", "tokens", "everySessionTokens"], ignoreOrder: true);
        removed.GetProperty("path").GetString().ShouldBe("rules/retired.md");
        removed.GetProperty("tokens").GetInt32().ShouldBe(300);
        removed.GetProperty("everySessionTokens").GetInt32().ShouldBe(300);

        // Resized: CLAUDE.md, by ten tokens.
        JsonElement resized = previous.GetProperty("resized").EnumerateArray().Single();
        resized.GetProperty("path").GetString().ShouldBe("CLAUDE.md");
        resized.GetProperty("loadMode").GetString().ShouldBe(claude.GetProperty("loadMode").GetString());
        resized.GetProperty("tokensBefore").GetInt32().ShouldBe(claudeTokens - 10);
        resized.GetProperty("tokensAfter").GetInt32().ShouldBe(claudeTokens);
        resized.GetProperty("everySessionTokensBefore").GetInt32().ShouldBe(claudeEverySession - 10);
        resized.GetProperty("everySessionTokensAfter").GetInt32().ShouldBe(claudeEverySession);

        // The totals of every session: yesterday's file leaves out reference.md and CLAUDE.md was smaller, and has the file that is gone.
        int everySessionNow = files.Sum(file => file.GetProperty("everySessionTokens").GetInt32());
        previous.GetProperty("everySessionTokensAfter").GetInt32().ShouldBe(everySessionNow);
        previous.GetProperty("everySessionTokensBefore").GetInt32().ShouldBe(
            everySessionNow - 10 + 300 - reference.GetProperty("everySessionTokens").GetInt32());

        // Broken links: the one that still is broken is not new, the others that are broken now are, with their lines; the one that was fixed is not broken any more.
        (string Source, string Kind, string Raw, int Line)[] expectedNew =
        [
            .. brokenNow
                .Select(link => (Source: link.GetProperty("source").GetString()!, Kind: link.GetProperty("kind").GetString()!, Raw: link.GetProperty("raw").GetString()!, Line: link.GetProperty("line").GetInt32()))
                .Where(link => !(link.Source == "CLAUDE.md" && link.Kind == "MarkdownLink" && link.Raw == "rules/gone.md"))
                .OrderBy(link => link.Source, StringComparer.Ordinal)
                .ThenBy(link => link.Line),
        ];
        expectedNew.ShouldNotBeEmpty();
        previous.GetProperty("newlyBroken").EnumerateArray()
            .Select(link => (link.GetProperty("source").GetString()!, link.GetProperty("kind").GetString()!, link.GetProperty("raw").GetString()!, link.GetProperty("line").GetInt32()))
            .ShouldBe(expectedNew);
        JsonElement fixedLink = previous.GetProperty("noLongerBroken").EnumerateArray().Single();
        fixedLink.PropertyNames().ShouldBe(["source", "kind", "raw"], ignoreOrder: true);
        fixedLink.GetProperty("source").GetString().ShouldBe("rules/style.md");
        fixedLink.GetProperty("kind").GetString().ShouldBe("WikiLink");
        fixedLink.GetProperty("raw").GetString().ShouldBe("fixed-since");
    }

    [Fact]
    public async Task GetChanges_FilesOfYesterdayAndAWeekAgo_GiveBothComparisons()
    {
        await using var app = new App();
        string root = JsonSerializer.Serialize(app.Root.Path);
        string Day(string day, string file) =>
            $$"""{"day":"{{day}}","takenAt":"{{day}}T08:00:00+00:00","files":{"{{file}}":[10,0]},"broken":[]}""";
        Directory.CreateDirectory(app.HistoryFolder);
        await File.WriteAllTextAsync(
            app.StoredFile,
            $$"""{"version":1,"root":{{root}},"profile":"Claude","days":[{{Day("2026-10-04", "from-the-4th.md")}},{{Day("2026-10-05", "from-the-5th.md")}},{{Day("2026-10-10", "from-the-10th.md")}}]}""",
            TestContext.Current.CancellationToken);

        using JsonDocument json = await app.ChangesAsync();

        json.RootElement.GetProperty("firstDay").GetString().ShouldBe("2026-10-05");
        json.RootElement.GetProperty("previousDay").GetProperty("day").GetString().ShouldBe("2026-10-10");
        Paths(json.RootElement.GetProperty("previousDay").GetProperty("removed")).ShouldBe(["from-the-10th.md"]);
        json.RootElement.GetProperty("previousWeek").GetProperty("day").GetString().ShouldBe("2026-10-05");
        Paths(json.RootElement.GetProperty("previousWeek").GetProperty("removed")).ShouldBe(["from-the-5th.md"]);
    }

    [Fact]
    public async Task GetChanges_Vault_HasNoTokensInEverySessionOnEitherSide()
    {
        using var temp = new TempDirectory();
        string vault = temp.CreateDirectory("vault");
        temp.CreateDirectory("vault/.obsidian");
        temp.Write("vault/Home.md", "# Home\nSee [[Plan]] and [gone](gone.md).\n");
        temp.Write("vault/Plan.md", "# Plan\nA note with some words in it.\n");
        string state = temp.Resolve("state");
        var clock = new ManualTimeProvider(Morning, TimeZoneInfo.Utc);
        await using var factory = new PusulaFactory(
            vault,
            directories: new UserDirectories(temp.CreateDirectory("home"), temp.CreateDirectory("data"), state),
            configureServices: services => services.AddSingleton<TimeProvider>(clock));
        using HttpClient client = factory.CreateClient();
        using (await client.GetJsonAsync(factory.Api("changes")))
        {
        }

        clock.Advance(TimeSpan.FromDays(1));
        temp.Write("vault/Later.md", "# Later\n");
        string folder = Path.Join(state, "pusula", "history");
        string file = Directory.GetFiles(folder).Single();
        using (JsonDocument stored = JsonDocument.Parse(await File.ReadAllTextAsync(file, TestContext.Current.CancellationToken)))
        {
            stored.RootElement.GetProperty("profile").GetString().ShouldBe("Vault");
            stored.RootElement.GetProperty("days")[0].GetProperty("files").EnumerateObject().Select(entry => entry.Value[1].GetInt32()).ShouldAllBe(tokens => tokens == 0);
            stored.RootElement.GetProperty("days")[0].GetProperty("broken").EnumerateArray().Select(link => link.EnumerateArray().Select(part => part.GetString()).ToArray())
                .ShouldBe([["Home.md", "MarkdownLink", "gone.md"]]);
        }

        // The new note is picked up by the watcher a moment after it is written.
        await EditingApp.WaitUntilAsync(
            async () =>
            {
                using JsonDocument now = await client.GetJsonAsync(factory.Api("changes"));
                return now.RootElement.TryGetProperty("previousDay", out JsonElement day) && day.GetProperty("added").GetArrayLength() > 0;
            },
            "the new note among the notes that were added");

        using JsonDocument json = await client.GetJsonAsync(factory.Api("changes"));
        JsonElement previous = json.RootElement.GetProperty("previousDay");
        previous.GetProperty("everySessionTokensBefore").GetInt32().ShouldBe(0);
        previous.GetProperty("everySessionTokensAfter").GetInt32().ShouldBe(0);
        JsonElement added = previous.GetProperty("added").EnumerateArray().Single();
        added.GetProperty("path").GetString().ShouldBe("Later.md");
        added.GetProperty("everySessionTokens").GetInt32().ShouldBe(0);
        added.GetProperty("loadMode").GetString().ShouldBe("OnDemand");
    }

    // ---- Where the history is not kept ----------------------------------------------------------------------------

    [Fact]
    public async Task GetChanges_WithoutAStateDirectory_IsOff()
    {
        using JsonDocument json = await fixture.Client.GetJsonAsync(fixture.Api("changes"));

        json.RootElement.PropertyNames().ShouldBe(["history"]);
        json.RootElement.GetProperty("history").GetString().ShouldBe("Off");
    }

    // No test may keep a history in the state folder of whoever runs it: a factory that is not given directories has none.
    [Fact]
    public async Task Factory_WithoutDirectories_HasNoStateDirectoryWhatEverTheEnvironmentSays()
    {
        using var root = new SyntheticRoot();
        using var temp = new TempDirectory();
        string sourcesFile = temp.Write("sources.json", "{ \"sources\": [] }");
        await using var rootFactory = new PusulaFactory(root.Path);
        await using PusulaFactory fileFactory = PusulaFactory.FromSourcesFile(sourcesFile);

        rootFactory.Services.GetRequiredService<UserDirectories>().State.ShouldBeEmpty();
        fileFactory.Services.GetRequiredService<UserDirectories>().State.ShouldBeEmpty();
    }

    [Fact]
    public async Task GetChanges_StateDirectoryInsideTheShownFolder_IsInsideSourceAndNothingIsWritten()
    {
        await using var app = new App(stateInsideTheShownFolder: true);
        string[] before = Listing(app.Root.Path);

        using JsonDocument json = await app.ChangesAsync();
        app.Clock.Advance(TimeSpan.FromDays(1));
        using JsonDocument again = await app.ChangesAsync();

        json.RootElement.PropertyNames().ShouldBe(["history"]);
        json.RootElement.GetProperty("history").GetString().ShouldBe("InsideSource");
        again.RootElement.GetProperty("history").GetString().ShouldBe("InsideSource");
        Listing(app.Root.Path).ShouldBe(before);
        Directory.Exists(app.State).ShouldBeFalse();
    }

    [Fact]
    public async Task GetChanges_StateDirectoryThatCannotBeWrittenTo_IsWriteFailedWithTheErrorAndTheAnswerStillComes()
    {
        await using var app = new App();
        Directory.CreateDirectory(app.State);
        await File.WriteAllTextAsync(Path.Join(app.State, "pusula"), "a file where the folder should be", TestContext.Current.CancellationToken);

        using HttpResponseMessage response = await app.GetChangesAsync();
        using JsonDocument json = await response.ReadJsonAsync();

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        json.RootElement.PropertyNames().ShouldBe(["history", "error"], ignoreOrder: true);
        json.RootElement.GetProperty("history").GetString().ShouldBe("WriteFailed");
        json.RootElement.GetProperty("error").GetString().ShouldNotBeNullOrWhiteSpace();
    }

    // ---- Sources that are not there ---------------------------------------------------------------------------

    [Fact]
    public async Task GetChanges_UnknownSource_Is404WithProblemDetails()
    {
        using HttpResponseMessage response = await fixture.Client.GetResponseAsync("/api/sources/no-such-source/changes");
        using JsonDocument json = await response.ReadJsonAsync();

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        response.Content.Headers.ContentType?.MediaType.ShouldBe("application/problem+json");
        json.RootElement.GetProperty("title").GetString().ShouldBe("Source not found");
    }

    [Fact]
    public async Task GetChanges_WrongMethod_Is405()
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, fixture.Api("changes"));

        using HttpResponseMessage response = await fixture.Client.SendAsync(request, TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.MethodNotAllowed);
    }
}
