using Pusula.Indexing;
using Pusula.Sources;
using Pusula.Startup;
using Pusula.UnitTests.Support;
using Shouldly;
using Xunit;

namespace Pusula.UnitTests.Sources;

// Nothing here reads the real sources file of the user: the application data directory and the home directory are
// scratch directories, and the sources file is given by path.
public sealed class SourceListTests
{
    private static SourceList Resolve(PusulaOptions options, TempDirectory home, TempDirectory applicationData)
    {
        SourceList.TryResolve(options, home.Path, applicationData.Path, out SourceList? list, out string? error).ShouldBeTrue(error);
        return list.ShouldNotBeNull();
    }

    [Fact]
    public void TryResolve_FoldersFromTheCommandLine_WinOverEverythingElse()
    {
        using var home = new TempDirectory();
        using var data = new TempDirectory();
        string file = data.Write("sources.json", """{ "sources": [ { "path": "/from/file" } ] }""");

        SourceList list = Resolve(
            new PusulaOptions { Roots = ["/one/notes", "/two/notes"], Root = "/from/root", SourcesFile = file },
            home,
            data);

        list.SourcesFile.ShouldBeNull();
        list.Sources.Select(source => (source.Id, source.Name, source.IsRequired)).ShouldBe(
        [
            ("notes", "notes", true),
            ("notes-2", "notes", true),
        ]);
    }

    [Fact]
    public void TryResolve_SingleRoot_IsOneRequiredSourceAndSkipsTheSourcesFile()
    {
        using var home = new TempDirectory();
        using var data = new TempDirectory();
        string file = data.Write("sources.json", """{ "sources": [ { "path": "/from/file" } ] }""");

        SourceList list = Resolve(new PusulaOptions { Root = "~/Documents/My Vault", SourcesFile = file }, home, data);

        list.SourcesFile.ShouldBeNull();
        SourceDefinition source = list.Sources.Single();
        source.Path.ShouldBe(Path.Join(home.Path, "Documents", "My Vault"));
        source.Id.ShouldBe("my-vault");
        source.IsRequired.ShouldBeTrue();
    }

    [Fact]
    public void TryResolve_NamedFolders_GetTheProfileTheirContentSuggests()
    {
        using var home = new TempDirectory();
        using var data = new TempDirectory();
        string vault = data.CreateDirectory("vault");
        data.CreateDirectory("vault/.obsidian");

        SourceList list = Resolve(new PusulaOptions { Roots = [vault, "~/.claude"] }, home, data);

        list.Sources.Select(source => source.Profile).ShouldBe([SourceProfile.Vault, SourceProfile.Claude]);
    }

    [Fact]
    public void TryResolve_NothingElse_ReadsTheSourcesFileThatIsConfigured()
    {
        using var home = new TempDirectory();
        using var data = new TempDirectory();
        string file = data.Write("elsewhere/my-sources.json", """{ "sources": [ { "path": "/a/one" }, { "path": "/a/two" } ] }""");

        SourceList list = Resolve(new PusulaOptions { SourcesFile = file }, home, data);

        list.SourcesFile.ShouldBe(file);
        list.Sources.Select(source => (source.Id, source.IsRequired)).ShouldBe([("one", false), ("two", false)]);
    }

    [Fact]
    public void TryResolve_NoSourcesFileConfigured_ReadsPusulaSourcesJsonInTheApplicationDataDirectory()
    {
        using var home = new TempDirectory();
        using var data = new TempDirectory();
        string file = data.Write("pusula/sources.json", """{ "sources": [ { "path": "/a/one" } ] }""");

        SourceList list = Resolve(new PusulaOptions(), home, data);

        list.SourcesFile.ShouldBe(file);
        list.Sources.Single().Id.ShouldBe("one");
    }

    [Fact]
    public void TryResolve_NoFolderAndNoFile_IsTheClaudeFolderOfTheUserAndSaysWhereItLooked()
    {
        using var home = new TempDirectory();
        using var data = new TempDirectory();
        home.CreateDirectory(".claude");

        SourceList list = Resolve(new PusulaOptions(), home, data);

        list.SourcesFile.ShouldBe(Path.Join(data.Path, "pusula", "sources.json"));
        list.Sources.ShouldBe([new SourceDefinition("claude", ".claude", Path.Join(home.Path, ".claude"), SourceProfile.Claude, IsRequired: true)]);
    }

    // A computer without Claude Code: nothing is shown until a folder is added on the Sources page, which writes the file
    // that was looked for. The folder used to be a source that had to exist, and the server stopped with a stack trace.
    [Fact]
    public void TryResolve_NoFolderNoFileAndNoClaudeFolder_IsEmptyAndSaysWhereItLooked()
    {
        using var home = new TempDirectory();
        using var data = new TempDirectory();

        SourceList list = Resolve(new PusulaOptions(), home, data);

        list.SourcesFile.ShouldBe(Path.Join(data.Path, "pusula", "sources.json"));
        list.Sources.ShouldBeEmpty();
    }

    // A file where the folder would be is not a folder to show either.
    [Fact]
    public void TryResolve_ClaudeIsAFileNotAFolder_IsEmpty()
    {
        using var home = new TempDirectory();
        using var data = new TempDirectory();
        home.Write(".claude", "not a folder");

        Resolve(new PusulaOptions(), home, data).Sources.ShouldBeEmpty();
    }

    // The settings say which file to read; a file that is not there is the same as no file.
    [Fact]
    public void TryResolve_SourcesFileThatDoesNotExist_FallsBackToTheClaudeFolder()
    {
        using var home = new TempDirectory();
        using var data = new TempDirectory();
        home.CreateDirectory(".claude");
        string missing = data.Resolve("nope/sources.json");

        SourceList list = Resolve(new PusulaOptions { SourcesFile = missing }, home, data);

        list.SourcesFile.ShouldBe(missing);
        list.Sources.Single().Id.ShouldBe("claude");
    }

    [Fact]
    public void TryResolve_SourcesFileThatCannotBeUsed_GivesOneLineWithTheFileAndTheReason()
    {
        using var home = new TempDirectory();
        using var data = new TempDirectory();
        string file = data.Write("sources.json", """{ "sources": [ { "path": "/a/x", "profile": "obsidian" } ] }""");

        SourceList.TryResolve(new PusulaOptions { SourcesFile = file }, home.Path, data.Path, out SourceList? list, out string? error).ShouldBeFalse();

        list.ShouldBeNull();
        error.ShouldBe($"pusula: sources file {file}: source 1: unknown profile \"obsidian\" (use auto, claude, vault or markdown)");
    }

    [Fact]
    public void TryResolve_SourcesFileWithAPathThatCannotBeAPath_GivesOneLineAndNoException()
    {
        using var home = new TempDirectory();
        using var data = new TempDirectory();
        string file = data.Write("sources.json", "{ \"sources\": [ { \"path\": \"/a/x\\u0000y\" } ] }");

        SourceList.TryResolve(new PusulaOptions { SourcesFile = file }, home.Path, data.Path, out SourceList? list, out string? error).ShouldBeFalse();

        list.ShouldBeNull();
        error.ShouldBe($"pusula: sources file {file}: source 1: \"path\" has a character that no path has");
    }

    [Fact]
    public void TryResolve_WithoutAnApplicationDataDirectory_HasNoFileToLookFor()
    {
        using var home = new TempDirectory();
        home.CreateDirectory(".claude");

        SourceList.TryResolve(new PusulaOptions(), home.Path, string.Empty, out SourceList? list, out string? error).ShouldBeTrue(error);

        list.ShouldNotBeNull().SourcesFile.ShouldBeNull();
        list.Sources.Single().Path.ShouldBe(Path.Join(home.Path, ".claude"));
    }
}
