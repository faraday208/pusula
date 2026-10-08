using Microsoft.AspNetCore.Connections;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
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

// The default settings are built into the program and come under everything else: the single-file download has no
// appsettings.json next to it. One in the content root (dotnet run), the environment and the command line still win.
builder.Configuration.Sources.Insert(0, BuiltInContent.Settings());

commandLine.AddRootsTo(builder.Configuration);

// A start that fails comes out of app.Run below, which says what went wrong in one line (or the runtime prints the
// exception); the host's own error line about it would put the stack trace on the console first. Its critical lines stay.
builder.Logging.AddFilter("Microsoft.Extensions.Hosting.Internal.Host", LogLevel.Critical);

builder.Services.AddOptions<PusulaOptions>().BindConfiguration(PusulaOptions.SectionName);
builder.Services.ConfigureHttpJsonOptions(options => ApiJson.Configure(options.SerializerOptions));
builder.Services.AddOpenApi(options => options.AddSourceParameter().AddNullableEnumSchemas());
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

// Once the server listens, an address that other devices can reach is said on the error stream: there is no login.
app.Lifetime.ApplicationStarted.Register(() =>
{
    ICollection<string>? addresses = app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()?.Addresses;
    if (addresses is not null && NetworkWarning.For(addresses) is { } warning)
    {
        Console.Error.WriteLine(warning);
    }
});

// Outside Development the page is the one built into the program, never a wwwroot of the folder it was started in.
// Development (dotnet run) keeps the files on disk, so that a change to them shows on the next reload.
if (!app.Environment.IsDevelopment())
{
    app.Environment.WebRootFileProvider = BuiltInContent.WebRoot();
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
catch (IOException exception) when (exception.InnerException is AddressInUseException)
{
    // The address is taken, often by a pusula that is already running: one line and exit code 1 as well. The message of
    // the server names the address.
    Console.Error.WriteLine(
        $"pusula: {exception.Message} Another program (perhaps another pusula) is using that port: stop it, or choose another port with --urls, for example --urls http://localhost:5191");
    return 1;
}

return 0;
