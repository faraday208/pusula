using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Pusula.Indexing;
using Pusula.Links;
using Pusula.Sources;
using Pusula.Startup;
using Xunit;

namespace Pusula.IntegrationTests.Support;

/// <summary>
/// A <see cref="SourceRegistry"/> on its own (real folders, real file watchers, no web server) over a made-up home
/// directory, application data directory and sources file in a scratch directory. Nothing of the real ones is read or
/// written: the home directory has a <c>.claude</c> folder, which is what is shown while there is no sources file.
/// </summary>
internal sealed class RegistryHarness : IAsyncDisposable
{
    public static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);

    private static readonly JsonDocumentOptions FileOptions = new() { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true };

    private readonly TempDirectory _sandbox = new();
    private readonly List<SourceRegistry> _registries = [];
    private SourceRegistry? _registry;

    /// <summary>Makes the scratch directories; the registry is made by <see cref="StartAsync"/>.</summary>
    public RegistryHarness()
    {
        Home = _sandbox.CreateDirectory("home");
        _sandbox.Write("home/.claude/CLAUDE.md", "# Claude\n");
        Directories = new UserDirectories(Home, _sandbox.CreateDirectory("data"));
        SourcesFile = _sandbox.Resolve("data/pusula/sources.json");
    }

    public string Home { get; }

    /// <summary>Folders to give the registry as if they were on the command line; null (the default) for the sources file.</summary>
    public string[]? Roots { get; set; }

    public UserDirectories Directories { get; }

    /// <summary>Where the sources file is looked for: its folder does not exist until something creates it.</summary>
    public string SourcesFile { get; }

    /// <summary>How much of a folder the registry scans before it gives up; the limits of the application by default, small ones to see what a folder that is too large does.</summary>
    internal ScanLimits Limits { get; set; } = ScanLimits.Default;

    /// <summary>The expressions that the links of every note are found with; null (the default) for the application's own. A set with an expression that does not work makes the scan of any folder with a note in it fail.</summary>
    internal LinkExtractor.PatternSet? LinkPatterns { get; set; }

    /// <summary>The factory that makes the loggers of the sources the registry starts (and of its file watcher); one that logs nothing by default.</summary>
    public ILoggerFactory LoggerFactory { get; set; } = NullLoggerFactory.Instance;

    public CapturingLogger<SourceRegistry> Log { get; private set; } = new();

    public SourceRegistry Registry => _registry ?? throw new InvalidOperationException("The registry is not started.");

    public string[] Ids => [.. Registry.Sources.Select(source => source.Definition.Id)];

    /// <summary>Makes a folder with Markdown files below the scratch directory (not in the home directory) and gives its path.</summary>
    public string Folder(string name, params string[] files)
    {
        string folder = _sandbox.CreateDirectory("folders/" + name);
        foreach (string file in files)
        {
            _sandbox.Write("folders/" + name + "/" + file, "# " + file + "\n");
        }

        return folder;
    }

    /// <summary>Makes a folder with Markdown files below the home directory and gives its path.</summary>
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

    /// <summary>Writes the sources file in place, the way an editor does, creating its folder.</summary>
    public void WriteSourcesFile(string text) => _sandbox.Write("data/pusula/sources.json", text);

    /// <summary>Replaces the sources file the way many editors save: a new file next to it that is then renamed over it.</summary>
    public void ReplaceSourcesFile(string text)
    {
        string temporary = _sandbox.Write("data/pusula/.sources.json.tmp", text);
        File.Move(temporary, SourcesFile, overwrite: true);
    }

    public string ReadSourcesFile() => File.ReadAllText(SourcesFile);

    /// <summary>The items of the sources file as JSON (comments and a comma after the last item are allowed, as for the reader).</summary>
    public JsonElement[] ReadItems()
    {
        using JsonDocument document = JsonDocument.Parse(ReadSourcesFile(), FileOptions);
        return [.. document.RootElement.GetProperty("sources").EnumerateArray().Select(item => item.Clone())];
    }

    /// <summary>Makes the registry: resolves the list, starts every source and, for a sources file, watches it.</summary>
    public async Task StartAsync()
    {
        Log = new CapturingLogger<SourceRegistry>();
        var options = new PusulaOptions { SourcesFile = Roots is null ? SourcesFile : null, Roots = Roots };
        var registry = new SourceRegistry(
            new IndexBuilder(NullLogger<IndexBuilder>.Instance) { Limits = Limits, LinkPatterns = LinkPatterns },
            Options.Create(options),
            Directories,
            LoggerFactory,
            Log);

        string? error = registry.Load();
        if (error is not null)
        {
            registry.Dispose();
            throw new InvalidOperationException(error);
        }

        _registries.Add(registry);
        _registry = registry;
        await registry.StartAsync(TestContext.Current.CancellationToken);
    }

    /// <summary>Stops the registry and starts a new one over the same scratch directories: what a restart of the server does.</summary>
    public async Task RestartAsync()
    {
        SourceRegistry old = Registry;
        await old.StopAsync(TestContext.Current.CancellationToken);
        old.Dispose();
        _registries.Remove(old);
        await StartAsync();
    }

    /// <summary>Disposes the registry without stopping it first (so that a test can call it after that); there is none until the next <see cref="StartAsync"/>.</summary>
    public void DisposeRegistry()
    {
        SourceRegistry old = Registry;
        old.Dispose();
        _registries.Remove(old);
    }

    /// <summary>Adds a folder the way the endpoint does: the request is checked first, then the registry adds it.</summary>
    public async Task<SourceEditResult> AddAsync(string path, string? name = null, string? profile = null)
    {
        if (!SourceRequestValidator.TryValidate(new CreateSourceRequest { Path = path, Name = name, Profile = profile }, Home, out NewSource? source, out EditError? error))
        {
            throw new InvalidOperationException($"The request is not valid: {error}");
        }

        return await Registry.AddAsync(source, TestContext.Current.CancellationToken);
    }

    public Task<SourceEditResult> RemoveAsync(string id) => Registry.RemoveAsync(id, TestContext.Current.CancellationToken);

    public static async Task WaitUntilAsync(Func<bool> condition, string what)
    {
        DateTime deadline = DateTime.UtcNow + Timeout;
        while (!condition())
        {
            if (DateTime.UtcNow > deadline)
            {
                throw new TimeoutException($"Timed out waiting for: {what}");
            }

            await Task.Delay(20, TestContext.Current.CancellationToken);
        }
    }

    /// <summary>
    /// Follows the changes of a source in the background: the task ends when the stream of changes ends (the source
    /// stopped). The subscription is in place when this returns.
    /// </summary>
    public static Task StreamEndsAsync(SourceEntry entry)
    {
        IAsyncEnumerator<IndexChange> changes = entry.Index!.WatchAsync(CancellationToken.None).GetAsyncEnumerator(CancellationToken.None);
        ValueTask<bool> first = changes.MoveNextAsync();
        return Drain(changes, first);

        static async Task Drain(IAsyncEnumerator<IndexChange> changes, ValueTask<bool> first)
        {
            bool more = await first;
            while (more)
            {
                more = await changes.MoveNextAsync();
            }

            await changes.DisposeAsync();
        }
    }

    public async ValueTask DisposeAsync()
    {
        foreach (SourceRegistry registry in _registries)
        {
            await registry.StopAsync(CancellationToken.None);
            registry.Dispose();
        }

        _sandbox.Dispose();
    }
}
