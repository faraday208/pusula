using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Pusula.Browse;
using Pusula.Indexing;
using Pusula.Sources;
using Pusula.Startup;

namespace Pusula.IntegrationTests.Support;

/// <summary>The whole application on an in-memory server, serving a given folder (or the sources of a given sources file).</summary>
internal sealed class PusulaFactory : WebApplicationFactory<Program>
{
    private readonly string? _root;
    private readonly string? _sourcesFile;
    private readonly string? _allowedHosts;
    private readonly string? _environment;
    private readonly UserDirectories? _directories;
    private readonly bool _simulateConnection;
    private readonly ScanLimits? _scanLimits;
    private readonly bool _allowRemoteEdit;
    private readonly Action<IServiceCollection>? _configureServices;
    private readonly Action<IWebHostBuilder>? _configureHost;

    /// <summary>
    /// Serves one folder, the way <c>Pusula:Root</c> does. <paramref name="simulateConnection"/> gives the requests the
    /// connection of a real server (see <see cref="SimulatedConnection"/>); <paramref name="allowRemoteEdit"/> sets
    /// <c>Pusula:AllowRemoteEdit</c>; <paramref name="configureServices"/> changes the services of the application last.
    /// </summary>
    public PusulaFactory(string root, string? allowedHosts = null, string? environment = null, bool simulateConnection = false, UserDirectories? directories = null, ScanLimits? scanLimits = null, bool allowRemoteEdit = false, Action<IServiceCollection>? configureServices = null)
        : this(root, sourcesFile: null, allowedHosts, environment, directories, simulateConnection, scanLimits, allowRemoteEdit, configureServices, configureHost: null)
    {
    }

    private PusulaFactory(string? root, string? sourcesFile, string? allowedHosts, string? environment, UserDirectories? directories, bool simulateConnection, ScanLimits? scanLimits, bool allowRemoteEdit, Action<IServiceCollection>? configureServices, Action<IWebHostBuilder>? configureHost)
    {
        _root = root;
        _sourcesFile = sourcesFile;
        _allowedHosts = allowedHosts;
        _environment = environment;
        _directories = directories;
        _simulateConnection = simulateConnection;
        _scanLimits = scanLimits;
        _allowRemoteEdit = allowRemoteEdit;
        _configureServices = configureServices;
        _configureHost = configureHost;
    }

    /// <summary>
    /// Serves the sources of a sources file (and never reads the one in the home directory of the user).
    /// <paramref name="directories"/> are the home and application data directories to use instead of the user's,
    /// <paramref name="simulateConnection"/> gives the requests the connection of a real server (see <see cref="SimulatedConnection"/>),
    /// <paramref name="allowRemoteEdit"/> sets <c>Pusula:AllowRemoteEdit</c>, <paramref name="configureServices"/> changes the
    /// services of the application last and <paramref name="configureHost"/> the host (its settings, its logging) after all the rest.
    /// </summary>
    public static PusulaFactory FromSourcesFile(string sourcesFile, UserDirectories? directories = null, bool simulateConnection = false, ScanLimits? scanLimits = null, bool allowRemoteEdit = false, Action<IServiceCollection>? configureServices = null, Action<IWebHostBuilder>? configureHost = null) =>
        new(root: null, sourcesFile, allowedHosts: null, environment: null, directories, simulateConnection, scanLimits, allowRemoteEdit, configureServices, configureHost);

    /// <summary>The id the application gives to the folder of <c>Pusula:Root</c>: derived from the name of the folder.</summary>
    public string SourceId =>
        SourceIds.Derive(SourceIds.NameOf(Path.GetFullPath(_root ?? throw new InvalidOperationException("This factory serves a sources file: it has no single folder."))));

    /// <summary>The URL of an endpoint of the source of <c>Pusula:Root</c>: <c>Api("tree")</c> is <c>/api/sources/{id}/tree</c>.</summary>
    /// <param name="endpoint">What comes after the id, query string included.</param>
    public string Api(string endpoint) => $"/api/sources/{SourceId}/{endpoint}";

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        if (_root is not null)
        {
            builder.UseSetting("Pusula:Root", _root);
        }

        if (_sourcesFile is not null)
        {
            builder.UseSetting("Pusula:SourcesFile", _sourcesFile);
        }

        if (_allowedHosts is not null)
        {
            builder.UseSetting("Pusula:AllowedHosts", _allowedHosts);
        }

        if (_allowRemoteEdit)
        {
            builder.UseSetting("Pusula:AllowRemoteEdit", "true");
        }

        if (_environment is not null)
        {
            builder.UseEnvironment(_environment);
        }

        if (_directories is not null || _simulateConnection || _scanLimits is not null)
        {
            builder.ConfigureTestServices(services =>
            {
                if (_directories is not null)
                {
                    services.AddSingleton(_directories);
                }

                if (_scanLimits is not null)
                {
                    services.AddSingleton(new IndexBuilder(NullLogger<IndexBuilder>.Instance) { Limits = _scanLimits });
                }

                if (_simulateConnection)
                {
                    services.AddSingleton<IStartupFilter, SimulatedConnection>();
                }
            });
        }

        builder.ConfigureTestServices(services =>
        {
            // No test searches the drives of the machine it runs on (/mnt, /media): a test that wants some gives its own.
            services.AddSingleton(DriveFolders.None);
            _configureServices?.Invoke(services);
        });

        builder.ConfigureLogging(logging => logging.ClearProviders());
        _configureHost?.Invoke(builder);
    }
}
