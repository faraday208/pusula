using System.Text.Json;
using Pusula.Indexing;
using Pusula.IntegrationTests.Support;
using Pusula.Startup;
using Shouldly;
using Xunit;

namespace Pusula.IntegrationTests.Startup;

public sealed class StartupTests
{
    private static string Json(string path) => JsonSerializer.Serialize(path);

    [Fact]
    public void Start_RootDoesNotExist_FailsAndNamesTheFolder()
    {
        using var temp = new TempDirectory();
        string missing = temp.Resolve("does-not-exist");
        using var factory = new PusulaFactory(missing);

        DirectoryNotFoundException exception = Should.Throw<DirectoryNotFoundException>(() => factory.CreateClient());

        exception.Message.ShouldContain(missing);
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
    public void Start_RootIsAFile_Fails()
    {
        using var temp = new TempDirectory();
        string file = temp.Write("not-a-folder.md", "x");
        using var factory = new PusulaFactory(file);

        Should.Throw<DirectoryNotFoundException>(() => factory.CreateClient()).Message.ShouldContain(file);
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
}
