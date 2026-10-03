using System.Text;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Pusula.Browse;
using Pusula.Indexing;
using Pusula.Sources;
using Pusula.Startup;
using Xunit;

namespace Pusula.IntegrationTests.Support;

/// <summary>
/// The whole application on an in-memory server over made-up directories, for the tests of adding and removing
/// sources: a home directory (with a <c>.claude</c> folder, which is shown while there is no sources file), a
/// sources file of its own and folders to add. Requests come from the machine the server runs on unless a test says
/// otherwise (see <see cref="From"/> and <see cref="SimulatedConnection"/>). Nothing of the real home directory is read
/// or written.
/// </summary>
internal sealed class EditingApp : IAsyncDisposable
{
    /// <summary>An address that is not this machine's.</summary>
    public const string AnotherMachine = "203.0.113.9";

    /// <summary>The address of this machine, as a browser on it would reach the server by it.</summary>
    public const string ThisMachine = "192.0.2.10";

    private static readonly JsonDocumentOptions FileOptions = new() { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true };

    private readonly TempDirectory _sandbox = new();
    private PusulaFactory? _factory;
    private HttpClient? _client;

    public EditingApp()
    {
        Home = _sandbox.CreateDirectory("home");
        _sandbox.Write("home/.claude/CLAUDE.md", "# Claude\n");
        Directories = new UserDirectories(Home, _sandbox.CreateDirectory("data"));
        SourcesFile = _sandbox.Resolve("data/pusula/sources.json");
    }

    public string Home { get; }

    public UserDirectories Directories { get; }

    /// <summary>Where the sources file is looked for: its folder does not exist until something creates it.</summary>
    public string SourcesFile { get; }

    /// <summary>How much of a folder the server scans before it gives up; null (the default) for the limits of the application, small ones to see what a folder that is too large does.</summary>
    internal ScanLimits? Limits { get; set; }

    /// <summary>A folder to show as <c>Pusula:Root</c> would, which is a list that does not come from a sources file; null (the default) for the sources file.</summary>
    public string? Root { get; set; }

    /// <summary>The setting <c>Pusula:AllowRemoteEdit</c>: whether a request from another machine may add and remove sources; off by default.</summary>
    public bool AllowRemoteEdit { get; set; }

    /// <summary>What the folder browser lists and reads; null (the default) for the limits of the application, small ones to see what a folder that is more than it lists or reads does.</summary>
    internal BrowseLimits? BrowseLimits { get; set; }

    /// <summary>The folders under which the search for vaults looks besides the home directory; none (the default): no test searches the drives of the machine it runs on.</summary>
    internal DriveFolders? Drives { get; set; }

    /// <summary>The clock that tells how old the result of a search is; null (the default) for the real one.</summary>
    internal TimeProvider? Time { get; set; }

    public HttpClient Client => _client ?? throw new InvalidOperationException("The application is not started.");

    public ISourceRegistry Registry => (_factory ?? throw new InvalidOperationException("The application is not started.")).Services.GetRequiredService<ISourceRegistry>();

    public string[] Ids => [.. Registry.Sources.Select(source => source.Definition.Id)];

    /// <summary>The headers of a request that comes from another machine (or from this machine's own address).</summary>
    /// <param name="remote">The address of the client.</param>
    /// <param name="local">The address the client connected to; the loopback address by default.</param>
    public static (string Name, string Value)[] From(string remote, string local = "127.0.0.1") =>
        [(SimulatedConnection.RemoteHeader, remote), (SimulatedConnection.LocalHeader, local)];

    public string Folder(string name, params string[] files)
    {
        string folder = _sandbox.CreateDirectory("folders/" + name);
        foreach (string file in files)
        {
            _sandbox.Write("folders/" + name + "/" + file, "# " + file + "\n");
        }

        return folder;
    }

    public string HomeFolder(string name, params string[] files)
    {
        string folder = _sandbox.CreateDirectory("home/" + name);
        foreach (string file in files)
        {
            _sandbox.Write("home/" + name + "/" + file, "# " + file + "\n");
        }

        return folder;
    }

    /// <summary>The full path of a folder below the scratch directory, which is not made.</summary>
    public string Missing(string name) => _sandbox.Resolve("folders/" + name);

    public string WriteFile(string relativePath, string text) => _sandbox.Write(relativePath, text);

    public void WriteSourcesFile(string text) => _sandbox.Write("data/pusula/sources.json", text);

    public string ReadSourcesFile() => File.ReadAllText(SourcesFile);

    public JsonElement[] ReadItems()
    {
        using JsonDocument document = JsonDocument.Parse(ReadSourcesFile(), FileOptions);
        return [.. document.RootElement.GetProperty("sources").EnumerateArray().Select(item => item.Clone())];
    }

    public async Task StartAsync()
    {
        _factory = Root is null
            ? PusulaFactory.FromSourcesFile(SourcesFile, Directories, simulateConnection: true, scanLimits: Limits, allowRemoteEdit: AllowRemoteEdit, configureServices: ConfigureBrowser)
            : new PusulaFactory(Root, simulateConnection: true, directories: Directories, scanLimits: Limits, allowRemoteEdit: AllowRemoteEdit, configureServices: ConfigureBrowser);
        _client = _factory.CreateClient();
        using HttpResponseMessage started = await _client.GetAsync("/health", TestContext.Current.CancellationToken);
        started.EnsureSuccessStatusCode();
    }

    public Task<HttpResponseMessage> SendAsync(HttpMethod method, string url, string? body = null, string? contentType = "application/json", (string Name, string Value)[]? headers = null)
    {
        var request = new HttpRequestMessage(method, url);
        if (body is not null)
        {
            if (contentType is null)
            {
                request.Content = new ByteArrayContent(Encoding.UTF8.GetBytes(body));
            }
            else
            {
                request.Content = new StringContent(body, Encoding.UTF8);
                request.Content.Headers.ContentType = System.Net.Http.Headers.MediaTypeHeaderValue.Parse(contentType);
            }
        }

        foreach ((string name, string value) in headers ?? [])
        {
            request.Headers.TryAddWithoutValidation(name, value);
        }

        return Client.SendAsync(request, TestContext.Current.CancellationToken);
    }

    /// <summary>Adds a source: <c>POST /api/sources</c> with the request as JSON.</summary>
    public Task<HttpResponseMessage> PostAsync(string path, string? name = null, string? profile = null, (string Name, string Value)[]? headers = null) =>
        PostJsonAsync(JsonSerializer.Serialize(new { path, name, profile }), headers);

    /// <summary>Adds a source: <c>POST /api/sources</c> with the text as the body.</summary>
    public Task<HttpResponseMessage> PostJsonAsync(string json, (string Name, string Value)[]? headers = null) =>
        SendAsync(HttpMethod.Post, "/api/sources", json, "application/json", headers);

    /// <summary>Removes a source: <c>DELETE /api/sources/{id}</c>.</summary>
    public Task<HttpResponseMessage> DeleteAsync(string id, (string Name, string Value)[]? headers = null) =>
        SendAsync(HttpMethod.Delete, "/api/sources/" + id, headers: headers);

    /// <summary>Lists the folders inside a folder: <c>GET /api/browse</c>.</summary>
    /// <param name="path">The folder; left out, it is the home directory.</param>
    /// <param name="hidden">Whether to ask for the hidden folders too (<c>hidden=1</c>).</param>
    /// <param name="headers">The headers of the request.</param>
    public Task<HttpResponseMessage> GetBrowseAsync(string? path = null, bool hidden = false, (string Name, string Value)[]? headers = null)
    {
        var query = new List<string>();
        if (path is not null)
        {
            query.Add("path=" + Uri.EscapeDataString(path));
        }

        if (hidden)
        {
            query.Add("hidden=1");
        }

        return SendAsync(HttpMethod.Get, "/api/browse" + (query.Count == 0 ? string.Empty : "?" + string.Join('&', query)), headers: headers);
    }

    /// <summary>Looks for the vaults of the machine: <c>GET /api/browse/found</c>.</summary>
    public Task<HttpResponseMessage> GetFoundAsync((string Name, string Value)[]? headers = null) =>
        SendAsync(HttpMethod.Get, "/api/browse/found", headers: headers);

    public async Task<JsonDocument> GetSourcesAsync((string Name, string Value)[]? headers = null)
    {
        using HttpResponseMessage response = await SendAsync(HttpMethod.Get, "/api/sources", headers: headers);
        response.EnsureSuccessStatusCode();
        return await response.ReadJsonAsync();
    }

    public static async Task WaitUntilAsync(Func<Task<bool>> condition, string what)
    {
        DateTime deadline = DateTime.UtcNow + SseClient.DefaultTimeout;
        while (!await condition())
        {
            if (DateTime.UtcNow > deadline)
            {
                throw new TimeoutException($"Timed out waiting for: {what}");
            }

            await Task.Delay(20, TestContext.Current.CancellationToken);
        }
    }

    // The services of the folder browser that a test gave; the application's own are kept for the others.
    private void ConfigureBrowser(IServiceCollection services)
    {
        if (BrowseLimits is not null)
        {
            services.AddSingleton(BrowseLimits);
        }

        if (Drives is not null)
        {
            services.AddSingleton(Drives);
        }

        if (Time is not null)
        {
            services.AddSingleton(Time);
        }
    }

    public async ValueTask DisposeAsync()
    {
        _client?.Dispose();
        if (_factory is not null)
        {
            await _factory.DisposeAsync();
        }

        _sandbox.Dispose();
    }
}
