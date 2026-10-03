using Microsoft.Extensions.DependencyInjection.Extensions;
using Pusula.Browse;
using Pusula.Files;
using Pusula.Indexing;
using Pusula.LiveReload;
using Pusula.Overview;
using Pusula.Security;
using Pusula.Sources;
using Pusula.Startup;
using Pusula.Tree;

// The folders at the start of the command line are the folders to show. They are not passed on to the host: the
// host's command-line provider would read an absolute path (which starts with '/') as an option.
CommandLine commandLine = CommandLine.Parse(args);

// `pusula --version` is answered first: one line and exit code 0, with no folder looked at and no host built.
if (commandLine.WantsVersion)
{
    Console.WriteLine(AppVersion.Line);
    return 0;
}

// A folder typed after the options would be lost without a word (the host takes it for an option, or for nothing) and the
// server would show the sources of the list instead: a usage error, one line and exit code 1.
string? misplacedFolder = commandLine.FindMisplacedFolderError();
if (misplacedFolder is not null)
{
    Console.Error.WriteLine(misplacedFolder);
    return 1;
}

// A folder typed on the command line that does not exist is a usage error: one line and exit code 1, no stack trace.
string? rootError = commandLine.FindRootError(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), Directory.Exists);
if (rootError is not null)
{
    Console.Error.WriteLine(rootError);
    return 1;
}

WebApplicationBuilder builder = WebApplication.CreateBuilder(commandLine.HostArguments);

commandLine.AddRootsTo(builder.Configuration);

builder.Services.AddOptions<PusulaOptions>().BindConfiguration(PusulaOptions.SectionName);
builder.Services.ConfigureHttpJsonOptions(options => ApiJson.Configure(options.SerializerOptions));
builder.Services.AddOpenApi(options => options.AddSourceParameter());
builder.Services.AddProblemDetails();
builder.Services.AddHealthChecks();

// A request body that cannot be read (not JSON, or JSON of the wrong shape) is a 400 or 415 in every environment. By
// default only outside Development does the endpoint answer it itself; in Development it throws, which comes out as a 500.
builder.Services.Configure<RouteHandlerOptions>(options => options.ThrowOnBadRequest = false);

builder.Services.AddSingleton(UserDirectories.FromEnvironment());
builder.Services.AddSingleton<IndexBuilder>();

// The folder browser: what it lists and reads, the drives it searches, and the clock that tells how old a search is.
builder.Services.TryAddSingleton(TimeProvider.System);
builder.Services.AddSingleton(BrowseLimits.Default);
builder.Services.AddSingleton(DriveFolders.ForThisMachine());
builder.Services.AddSingleton<FolderFinder>();

builder.Services.AddSingleton<SourceRegistry>();
builder.Services.AddSingleton<ISourceRegistry>(services => services.GetRequiredService<SourceRegistry>());
builder.Services.AddSingleton<ISourceEditor>(services => services.GetRequiredService<SourceRegistry>());
builder.Services.AddHostedService(services => services.GetRequiredService<SourceRegistry>());

WebApplication app = builder.Build();

// The list of sources is read now, before the server starts: a sources file that cannot be used is a usage error
// like a folder that does not exist (one line and exit code 1), and the settings are complete once the host is built.
string? sourcesError = app.Services.GetRequiredService<SourceRegistry>().Load();
if (sourcesError is not null)
{
    Console.Error.WriteLine(sourcesError);
    return 1;
}

// The guard comes first: a request that is not meant for this server goes no further.
app.UseMiddleware<HostGuardMiddleware>();
app.UseMiddleware<SecurityHeadersMiddleware>();
app.UseExceptionHandler();
app.UseStatusCodePages();
app.UseDefaultFiles();
app.UseStaticFiles(new StaticFileOptions
{
    OnPrepareResponse = static context => context.Context.Response.Headers.CacheControl = "no-cache",
});
app.UseRouting();

app.MapOpenApi();
app.MapHealthChecks("/health");

// Everything about one source lives below /api/sources/{source}; the group finds the source for its endpoints.
RouteGroupBuilder source = app.MapSources();
source.MapTree();
source.MapFiles();
source.MapOverview();
source.MapEvents();

// The folder browser that adding a source is done with: /api/browse
app.MapBrowse();

try
{
    app.Run();
}
catch (FolderTooLargeException exception)
{
    // A folder that was named on the command line (or in Pusula:Root) is more than is shown: a usage error like one that
    // does not exist, one line and exit code 1. A folder of a sources file only makes its source not available.
    Console.Error.WriteLine($"pusula: {exception.Folder}: {exception.Message}");
    return 1;
}

return 0;
