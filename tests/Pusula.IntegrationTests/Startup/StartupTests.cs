using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Pusula.Indexing;
using Pusula.IntegrationTests.Support;
using Pusula.Startup;
using Shouldly;
using Xunit;

namespace Pusula.IntegrationTests.Startup;

public sealed class StartupTests
{
    private static string Json(string path) => JsonSerializer.Serialize(path);

    // The real entry point, with the root given as a setting (the way Pusula:Root comes from the settings file or the
    // environment, which the early check of the command line does not see): the index host reports the folder when it
    // starts, and no server runs. A host built by the test factory is not used for this: when its start fails, it can
    // report that its services are disposed already instead of the failure (a race of the test host, not of pusula).
    [Fact]
    public void Main_RootSettingThatDoesNotExist_FailsAndNamesTheFolder()
    {
        using var temp = new TempDirectory();
        string missing = temp.Resolve("does-not-exist");

        StartFailure(missing).Message.ShouldContain(missing);
    }

    private static DirectoryNotFoundException StartFailure(string root)
    {
        TargetInvocationException thrown = Should.Throw<TargetInvocationException>(() =>
            typeof(Program).Assembly.EntryPoint!.Invoke(null, [new[] { "--Pusula:Root", root, "--urls", "http://127.0.0.1:0" }]));
        return thrown.InnerException.ShouldBeOfType<DirectoryNotFoundException>();
    }

    // The real entry point and the real limit: a folder of 20,001 Markdown files. One line on the error stream and exit
    // code 1, as for a folder that does not exist; it has to come before a server is running (a running host never returns).
    [Fact]
    public void Main_PositionalRootThatIsTooLargeToShow_WritesOneErrorLineAndReturnsExitCode1()
    {
        using var temp = new TempDirectory();
        for (int i = 0; i <= ScanLimits.Default.MaxFiles; i++)
        {
            File.WriteAllText(Path.Join(temp.Path, $"note{i}.md"), "x");
        }

        TextWriter originalError = Console.Error;
        using var capturedError = new StringWriter();
        object? exitCode;

        Console.SetError(capturedError);
        try
        {
            exitCode = typeof(Program).Assembly.EntryPoint!.Invoke(null, [new[] { temp.Path }]);
        }
        finally
        {
            Console.SetError(originalError);
        }

        exitCode.ShouldBe(1);
        capturedError.ToString().ShouldBe($"pusula: {temp.Path}: The folder is too large to show: more than 20,000 Markdown files.{Environment.NewLine}");
    }

    [Fact]
    public void Main_RootSettingThatIsAFile_FailsAndNamesTheFile()
    {
        using var temp = new TempDirectory();
        string file = temp.Write("not-a-folder.md", "x");

        StartFailure(file).Message.ShouldContain(file);
    }

    // The real entry point, called in-process. A root that does not exist has to stop it before a host is built (a
    // running host would never return): one line on the error stream, exit code 1, no exception.
    [Fact]
    public void Main_PositionalRootDoesNotExist_WritesOneErrorLineAndReturnsExitCode1()
    {
        using var temp = new TempDirectory();
        string missing = temp.Resolve("does-not-exist");
        TextWriter originalError = Console.Error;
        using var capturedError = new StringWriter();
        object? exitCode;

        Console.SetError(capturedError);
        try
        {
            exitCode = typeof(Program).Assembly.EntryPoint!.Invoke(null, [new[] { missing }]);
        }
        finally
        {
            Console.SetError(originalError);
        }

        exitCode.ShouldBe(1);
        capturedError.ToString().ShouldBe($"pusula: root folder not found: {missing}{Environment.NewLine}");
    }

    // A folder written after the options would otherwise be lost without a word, and the server would show the sources of the
    // list instead: the entry point refuses it before any host is built (a running host would never return), with one line on
    // the error stream and exit code 1.
    [Fact]
    public void Main_FolderAfterTheOptions_WritesOneErrorLineAndReturnsExitCode1()
    {
        using var temp = new TempDirectory();
        string folder = temp.Path;
        TextWriter originalError = Console.Error;
        using var capturedError = new StringWriter();
        object? exitCode;

        Console.SetError(capturedError);
        try
        {
            exitCode = typeof(Program).Assembly.EntryPoint!.Invoke(null, [new[] { "--urls", "http://127.0.0.1:0", folder }]);
        }
        finally
        {
            Console.SetError(originalError);
        }

        exitCode.ShouldBe(1);
        capturedError.ToString().ShouldBe($"pusula: folders go before the options (pusula [folder ...] [options]): {folder}{Environment.NewLine}");
    }

    // The same entry point, asked for the version. It is answered before anything else: the folder that is named first does not
    // exist, and it is not looked at; no host is built (a running host would never return). One line on the standard output, exit
    // code 0, nothing on the error stream.
    [Fact]
    public void Main_VersionOption_WritesTheVersionAndReturnsExitCode0WithoutLookingAtTheFolders()
    {
        using var temp = new TempDirectory();
        string missing = temp.Resolve("does-not-exist");
        TextWriter originalOut = Console.Out;
        TextWriter originalError = Console.Error;
        using var capturedOut = new StringWriter();
        using var capturedError = new StringWriter();
        object? exitCode;

        Console.SetOut(capturedOut);
        Console.SetError(capturedError);
        try
        {
            exitCode = typeof(Program).Assembly.EntryPoint!.Invoke(null, [new[] { missing, "--version" }]);
        }
        finally
        {
            Console.SetOut(originalOut);
            Console.SetError(originalError);
        }

        exitCode.ShouldBe(0);
        capturedOut.ToString().ShouldBe($"{AppVersion.Line}{Environment.NewLine}");
        capturedError.ToString().ShouldBeEmpty();
    }

    // The same entry point, with a sources file that cannot be used: the settings are read once the host is built, and
    // the answer is still one line and exit code 1, before anything is served.
    [Fact]
    public void Main_SourcesFileThatCannotBeUsed_WritesOneErrorLineAndReturnsExitCode1()
    {
        using var temp = new TempDirectory();
        string file = temp.Write("sources.json", """{ "sources": [ { "path": "/a/x", "profile": "obsidian" } ] }""");
        TextWriter originalError = Console.Error;
        using var capturedError = new StringWriter();
        object? exitCode;

        Console.SetError(capturedError);
        try
        {
            exitCode = typeof(Program).Assembly.EntryPoint!.Invoke(null, [new[] { "--Pusula:SourcesFile", file }]);
        }
        finally
        {
            Console.SetError(originalError);
        }

        exitCode.ShouldBe(1);
        capturedError.ToString().ShouldBe(
            $"pusula: sources file {file}: source 1: unknown profile \"obsidian\" (use auto, claude, vault or markdown){Environment.NewLine}");
    }

    // A path that cannot be a path is a file that cannot be used, like any other: one line and exit code 1, not a stack trace.
    [Fact]
    public void Main_SourcesFileWithAPathThatCannotBeAPath_WritesOneErrorLineAndReturnsExitCode1()
    {
        using var temp = new TempDirectory();
        string file = temp.Write("sources.json", "{ \"sources\": [ { \"path\": \"/a/x\\u0000y\" } ] }");
        TextWriter originalError = Console.Error;
        using var capturedError = new StringWriter();
        object? exitCode;

        Console.SetError(capturedError);
        try
        {
            exitCode = typeof(Program).Assembly.EntryPoint!.Invoke(null, [new[] { "--Pusula:SourcesFile", file }]);
        }
        finally
        {
            Console.SetError(originalError);
        }

        exitCode.ShouldBe(1);
        capturedError.ToString().ShouldBe($"pusula: sources file {file}: source 1: \"path\" has a character that no path has{Environment.NewLine}");
    }

    [Fact]
    public async Task Start_SourcesFileWithAFolderThatDoesNotExist_StillStartsAndServesTheOthers()
    {
        using var temp = new TempDirectory();
        temp.Write("notes/Note.md", "# Note\n");
        string file = temp.Write(
            "sources.json",
            $$"""{ "sources": [ { "path": {{Json(temp.Resolve("missing"))}}, "id": "gone" }, { "path": {{Json(temp.Resolve("notes"))}}, "id": "notes" } ] }""");
        await using PusulaFactory factory = PusulaFactory.FromSourcesFile(file);
        using HttpClient client = factory.CreateClient();

        using System.Text.Json.JsonDocument sources = await client.GetJsonAsync("/api/sources");
        using System.Text.Json.JsonDocument tree = await client.GetJsonAsync("/api/sources/notes/tree");

        sources.RootElement.GetProperty("sources").EnumerateArray().Select(source => source.GetProperty("available").GetBoolean()).ShouldBe([false, true]);
        tree.RootElement.GetProperty("nodes")[0].GetProperty("name").GetString().ShouldBe("Note.md");
    }

    [Fact]
    public async Task Start_RootFromConfigurationSetLate_IsServed()
    {
        using var temp = new TempDirectory();
        temp.Write("CLAUDE.md", "# From the test host setting\n");
        await using var factory = new PusulaFactory(temp.Path);
        using HttpClient client = factory.CreateClient();

        using System.Text.Json.JsonDocument tree = await client.GetJsonAsync(factory.Api("tree"));

        tree.RootElement.GetProperty("root").GetString().ShouldBe(temp.Path);
        tree.RootElement.GetProperty("nodes")[0].GetProperty("name").GetString().ShouldBe("CLAUDE.md");
    }

    // A computer without Claude Code, and nothing that says what to show: the server starts with no source (it used to stop
    // with a stack trace, as ~/.claude was a source that had to exist), the page can add one, and the first folder added is shown.
    [Fact]
    public async Task Start_NoClaudeFolderAndNoSourcesFile_StartsEmptyAndShowsTheFirstFolderAdded()
    {
        await using var app = new EditingApp();
        Directory.Delete(Path.Join(app.Home, ".claude"), recursive: true);
        string notes = app.Folder("notes", "Note.md");
        await app.StartAsync();

        using (JsonDocument sources = await app.GetSourcesAsync())
        {
            sources.RootElement.GetProperty("sources").GetArrayLength().ShouldBe(0);
            sources.RootElement.GetProperty("sourcesFile").GetString().ShouldBe(app.SourcesFile);
            sources.RootElement.GetProperty("canEdit").GetBoolean().ShouldBeTrue();
        }

        using HttpResponseMessage response = await app.PostAsync(notes);

        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        app.Ids.ShouldBe(["notes"]);
        File.Exists(app.SourcesFile).ShouldBeTrue();
    }

    // A port that another program holds, often a pusula that is already running: one line on the error stream and exit code 1,
    // as for the other mistakes of the command line, not the stack trace of a start that failed.
    [Fact]
    public void Main_PortThatIsInUse_WritesOneErrorLineAndReturnsExitCode1()
    {
        using var temp = new TempDirectory();
        var taken = new TcpListener(IPAddress.Loopback, 0);
        taken.Start();
        string address = $"http://127.0.0.1:{((IPEndPoint)taken.LocalEndpoint).Port}";
        TextWriter originalError = Console.Error;
        using var capturedError = new StringWriter();
        object? exitCode;

        Console.SetError(capturedError);
        try
        {
            exitCode = typeof(Program).Assembly.EntryPoint!.Invoke(null, [new[] { temp.Path, "--urls", address }]);
        }
        finally
        {
            Console.SetError(originalError);
            taken.Stop();
        }

        exitCode.ShouldBe(1);
        string error = capturedError.ToString();
        error.ShouldStartWith("pusula: ");
        error.ShouldContain(address);
        error.ShouldContain("--urls");
        error.TrimEnd().ShouldNotContain('\n');
    }

    // A start that fails is told by the entry point in one line (or by the runtime, which prints the exception); the host's own
    // error line about it would put the stack trace on the console first. Its critical lines, and the errors of every other
    // category, still come through.
    [Fact]
    public async Task Logging_ErrorsOfTheHostItself_AreLeftOutButItsCriticalLinesAndOtherErrorsAreNot()
    {
        const string HostCategory = "Microsoft.Extensions.Hosting.Internal.Host";
        using var temp = new TempDirectory();
        temp.Write("home/.claude/CLAUDE.md", "# Claude\n");
        var provider = new CapturingLoggerProvider();
        await using PusulaFactory factory = PusulaFactory.FromSourcesFile(
            temp.Resolve("data/pusula/sources.json"),
            new UserDirectories(temp.Resolve("home"), temp.CreateDirectory("data")),
            configureHost: builder => builder.ConfigureLogging(logging => logging.AddProvider(provider)));
        ILoggerFactory loggers = factory.Services.GetRequiredService<ILoggerFactory>();

        // The logger's own method, which the filters apply to as to any other line.
        static void Write(ILogger logger, LogLevel level, string message) => logger.Log(level, default, message, exception: null, static (text, _) => text);

        Write(loggers.CreateLogger(HostCategory), LogLevel.Error, "test: host error");
        Write(loggers.CreateLogger(HostCategory), LogLevel.Critical, "test: host critical");
        Write(loggers.CreateLogger("Pusula.Sources.SourceRegistry"), LogLevel.Error, "test: other error");

        provider.Entries.Select(entry => entry.Message).Where(message => message.StartsWith("test: ", StringComparison.Ordinal))
            .ShouldBe(["test: host critical", "test: other error"]);
    }
}
