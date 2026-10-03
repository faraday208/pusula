using Microsoft.Extensions.Configuration;
using Pusula.Startup;
using Pusula.UnitTests.Support;
using Shouldly;
using Xunit;

namespace Pusula.UnitTests.Startup;

public sealed class CommandLineTests
{
    [Fact]
    public void Parse_NoArguments_HasNoRootAndNoHostArguments()
    {
        CommandLine commandLine = CommandLine.Parse([]);

        commandLine.Roots.ShouldBeEmpty();
        commandLine.HostArguments.ShouldBeEmpty();
    }

    [Theory]
    [InlineData("/home/user/.claude")]
    [InlineData("~/.claude")]
    [InlineData("relative/folder")]
    [InlineData("C:\\Users\\me\\.claude")]
    [InlineData("a")]
    public void Parse_FirstArgumentThatIsNotAnOption_IsTheRoot(string root)
    {
        CommandLine commandLine = CommandLine.Parse([root]);

        commandLine.Roots.ShouldBe([root]);
        commandLine.HostArguments.ShouldBeEmpty();
    }

    [Fact]
    public void Parse_RootFollowedByOptions_PassesOnlyTheOptionsToTheHost()
    {
        CommandLine commandLine = CommandLine.Parse(["/home/user/.claude", "--urls", "http://0.0.0.0:5190", "--Pusula:AllowedHosts", "a;b"]);

        commandLine.Roots.ShouldBe(["/home/user/.claude"]);
        commandLine.HostArguments.ShouldBe(["--urls", "http://0.0.0.0:5190", "--Pusula:AllowedHosts", "a;b"]);
    }

    [Theory]
    [InlineData("--urls")]
    [InlineData("-v")]
    [InlineData("--Pusula:Root=/x")]
    public void Parse_FirstArgumentThatIsAnOption_MeansNoRoot(string first)
    {
        string[] args = [first, "http://localhost:1", "/home/user/.claude"];

        CommandLine commandLine = CommandLine.Parse(args);

        commandLine.Roots.ShouldBeEmpty();
        commandLine.HostArguments.ShouldBe(args);
    }

    [Fact]
    public void Parse_EmptyFirstArgument_IsNotARoot()
    {
        CommandLine commandLine = CommandLine.Parse(["", "--urls", "x"]);

        commandLine.Roots.ShouldBeEmpty();
        commandLine.HostArguments.ShouldBe(["", "--urls", "x"]);
    }

    [Fact]
    public void Parse_EveryArgumentAtTheStartThatIsNotAnOption_IsARoot()
    {
        CommandLine commandLine = CommandLine.Parse(["/first", "~/second", "third"]);

        commandLine.Roots.ShouldBe(["/first", "~/second", "third"]);
        commandLine.HostArguments.ShouldBeEmpty();
    }

    // The value of an option is not a folder: only the arguments before the first option are.
    [Fact]
    public void Parse_ArgumentsAfterTheFirstOption_BelongToTheHost()
    {
        CommandLine commandLine = CommandLine.Parse(["/first", "/second", "--urls", "http://localhost:1", "/not-a-root", ""]);

        commandLine.Roots.ShouldBe(["/first", "/second"]);
        commandLine.HostArguments.ShouldBe(["--urls", "http://localhost:1", "/not-a-root", ""]);
    }

    [Fact]
    public void Parse_EmptyArgumentBetweenRoots_EndsTheRoots()
    {
        CommandLine commandLine = CommandLine.Parse(["/first", "", "/second"]);

        commandLine.Roots.ShouldBe(["/first"]);
        commandLine.HostArguments.ShouldBe(["", "/second"]);
    }

    [Fact]
    public void AddRootsTo_RootsGiven_BecomeThePusulaRootsSetting()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Pusula:Root"] = "/from/settings", ["Pusula:AllowedHosts"] = "a" })
            .AddCommandLine(["--Pusula:Root=/from/option"]);

        CommandLine.Parse(["/from/argument", "/second", "--urls", "x"]).AddRootsTo(configuration);
        IConfigurationRoot built = configuration.Build();

        PusulaOptions options = built.GetSection("Pusula").Get<PusulaOptions>()!;
        options.Roots.ShouldBe(["/from/argument", "/second"]);
        options.AllowedHosts.ShouldBe("a");

        // The single-folder setting is still there: it is the sources that let the folders of the command line win.
        options.Root.ShouldBe("/from/option");
    }

    [Fact]
    public void AddRootsTo_NoRootGiven_LeavesTheConfigurationAlone()
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["Pusula:Root"] = "/from/settings" });

        CommandLine.Parse(["--urls", "x"]).AddRootsTo(configuration);

        configuration.Sources.Count.ShouldBe(1);
        configuration.Build()["Pusula:Root"].ShouldBe("/from/settings");
        configuration.Build().GetSection("Pusula").Get<PusulaOptions>()!.Roots.ShouldBeNull();
    }

    // ---- WantsVersion: pusula --version, answered before anything else -----------------------------------------

    [Fact]
    public void WantsVersion_VersionOptionOnly_IsTrue() =>
        CommandLine.Parse(["--version"]).WantsVersion.ShouldBeTrue();

    // Whatever stands before it, the folders (which are not looked at then) or other options.
    [Fact]
    public void WantsVersion_VersionOptionAfterFoldersAndOtherOptions_IsTrue()
    {
        CommandLine.Parse(["/home/user/.claude", "--version"]).WantsVersion.ShouldBeTrue();
        CommandLine.Parse(["--urls", "http://localhost:1", "--version"]).WantsVersion.ShouldBeTrue();
        CommandLine.Parse(["/first", "/second", "--Pusula:AllowedHosts", "a;b", "--version"]).WantsVersion.ShouldBeTrue();
    }

    [Fact]
    public void WantsVersion_NoVersionOption_IsFalse()
    {
        CommandLine.Parse([]).WantsVersion.ShouldBeFalse();
        CommandLine.Parse(["/home/user/.claude"]).WantsVersion.ShouldBeFalse();
        CommandLine.Parse(["--urls", "http://localhost:1"]).WantsVersion.ShouldBeFalse();
    }

    // Only the option itself asks: a folder or an option that merely has the word in it does not.
    [Fact]
    public void WantsVersion_OtherArgumentsWithTheWordInThem_IsFalse()
    {
        CommandLine.Parse(["version"]).WantsVersion.ShouldBeFalse();
        CommandLine.Parse(["--versions"]).WantsVersion.ShouldBeFalse();
        CommandLine.Parse(["-version"]).WantsVersion.ShouldBeFalse();
        CommandLine.Parse(["--Pusula:Root=/version"]).WantsVersion.ShouldBeFalse();
    }

    // ---- FindRootError: the folder given as the first argument, checked before the host is built ---------------

    private const string Home = "/home/test";

    [Fact]
    public void FindRootError_NoRootGiven_ReturnsNullWithoutLookingAtTheFileSystem() =>
        CommandLine.Parse(["--urls", "http://localhost:1"])
            .FindRootError(Home, static _ => throw new InvalidOperationException("The file system must not be probed."))
            .ShouldBeNull();

    // A root that is configured (instead of given as the first argument) is reported later, by the index host.
    [Fact]
    public void FindRootError_RootGivenAsAnOption_IsNotChecked() =>
        CommandLine.Parse(["--Pusula:Root=/does/not/exist"]).FindRootError(Home, static _ => false).ShouldBeNull();

    [Fact]
    public void FindRootError_FolderExists_ReturnsNull() =>
        CommandLine.Parse(["/some/folder"]).FindRootError(Home, static _ => true).ShouldBeNull();

    [Fact]
    public void FindRootError_SeveralFolders_ReportsTheFirstOneThatDoesNotExist()
    {
        string? error = CommandLine.Parse(["/exists", "/missing-one", "/missing-two"])
            .FindRootError(Home, path => path.EndsWith("exists", StringComparison.Ordinal));

        error.ShouldBe("pusula: root folder not found: " + Path.GetFullPath("/missing-one"));
    }

    [Fact]
    public void FindRootError_FolderDoesNotExist_ReturnsOneLineWithTheFullPath()
    {
        string? error = CommandLine.Parse(["/some/missing/folder", "--urls", "http://localhost:1"]).FindRootError(Home, static _ => false);

        error.ShouldBe("pusula: root folder not found: " + Path.GetFullPath("/some/missing/folder"));
        error.ShouldNotBeNull().ShouldNotContain('\n');
    }

    [Fact]
    public void FindRootError_RelativeRoot_ReportsTheFullPath() =>
        CommandLine.Parse(["relative/missing"]).FindRootError(Home, static _ => false)
            .ShouldBe("pusula: root folder not found: " + Path.GetFullPath("relative/missing"));

    [Fact]
    public void FindRootError_TildeRoot_IsExpandedBeforeTheFolderIsLookedUp()
    {
        string home = Path.GetFullPath(Home);
        string expected = Path.GetFullPath(Path.Join(home, "missing"));
        var probed = new List<string>();

        string? error = CommandLine.Parse(["~/missing"]).FindRootError(home, path =>
        {
            probed.Add(path);
            return false;
        });

        probed.ShouldBe([expected]);
        error.ShouldBe("pusula: root folder not found: " + expected);
    }

    // The command line counts a blank first argument as a root; the options turn it into the default folder.
    // The check has to look at the folder the host would serve.
    [Fact]
    public void FindRootError_BlankRoot_ChecksTheDefaultFolder()
    {
        string home = Path.GetFullPath(Home);
        string expected = Path.GetFullPath(Path.Join(home, ".claude"));
        var probed = new List<string>();

        string? error = CommandLine.Parse([" "]).FindRootError(home, path =>
        {
            probed.Add(path);
            return false;
        });

        probed.ShouldBe([expected]);
        error.ShouldBe("pusula: root folder not found: " + expected);
    }

    [Fact]
    public void FindRootError_TildeRootWithoutAHomeDirectory_Throws() =>
        Should.Throw<InvalidOperationException>(() => CommandLine.Parse(["~/x"]).FindRootError(string.Empty, static _ => true));

    [Fact]
    public void FindRootError_ExistingFolderOnDisk_ReturnsNull()
    {
        using var temp = new TempDirectory();

        CommandLine.Parse([temp.Path]).FindRootError(Home, Directory.Exists).ShouldBeNull();
    }

    [Fact]
    public void FindRootError_MissingFolderOnDisk_ReturnsTheLine()
    {
        using var temp = new TempDirectory();
        string missing = temp.Resolve("does-not-exist");

        CommandLine.Parse([missing]).FindRootError(Home, Directory.Exists).ShouldBe("pusula: root folder not found: " + missing);
    }

    [Fact]
    public void FindRootError_RootIsAFile_IsReportedLikeAMissingFolder()
    {
        using var temp = new TempDirectory();
        string file = temp.Write("not-a-folder.md", "x");

        CommandLine.Parse([file]).FindRootError(Home, Directory.Exists).ShouldBe("pusula: root folder not found: " + file);
    }

    private const string MisplacedFolder = "pusula: folders go before the options (pusula [folder ...] [options]): ";

    [Fact]
    public void FindMisplacedFolderError_FolderAfterAnOptionAndItsValue_IsReported() =>
        CommandLine.Parse(["--urls", "http://localhost:1", "/notes"]).FindMisplacedFolderError().ShouldBe(MisplacedFolder + "/notes");

    [Fact]
    public void FindMisplacedFolderError_FolderAfterAnOptionWithItsValueAfterAnEqualsSign_IsReported() =>
        CommandLine.Parse(["--urls=http://localhost:1", "~/notes"]).FindMisplacedFolderError().ShouldBe(MisplacedFolder + "~/notes");

    [Fact]
    public void FindMisplacedFolderError_FolderBetweenOptions_IsReported() =>
        CommandLine.Parse(["--Pusula:AllowRemoteEdit", "true", "notes", "--urls", "http://localhost:1"]).FindMisplacedFolderError().ShouldBe(MisplacedFolder + "notes");

    [Fact]
    public void FindMisplacedFolderError_NoArguments_IsNull() =>
        CommandLine.Parse([]).FindMisplacedFolderError().ShouldBeNull();

    [Fact]
    public void FindMisplacedFolderError_FoldersOnly_IsNull() =>
        CommandLine.Parse(["/a", "/b"]).FindMisplacedFolderError().ShouldBeNull();

    [Fact]
    public void FindMisplacedFolderError_FoldersFirstThenOptionsWithTheirValues_IsNull() =>
        CommandLine.Parse(["/a", "--urls", "http://localhost:1", "--Pusula:AllowedHosts", "a;b"]).FindMisplacedFolderError().ShouldBeNull();

    [Fact]
    public void FindMisplacedFolderError_SettingsWrittenAsKeyEqualsValue_IsNull() =>
        CommandLine.Parse(["--urls=http://localhost:1", "Pusula:AllowRemoteEdit=true"]).FindMisplacedFolderError().ShouldBeNull();

    [Fact]
    public void FindMisplacedFolderError_VersionOptionAlone_IsNull() =>
        CommandLine.Parse(["--version"]).FindMisplacedFolderError().ShouldBeNull();

    [Fact]
    public void FindMisplacedFolderError_EmptyArgumentAfterTheOptions_IsNotAFolder() =>
        CommandLine.Parse(["--urls", "http://localhost:1", ""]).FindMisplacedFolderError().ShouldBeNull();

    [Fact]
    public void Parse_NullArguments_Throws() =>
        Should.Throw<ArgumentNullException>(() => CommandLine.Parse(null!));

    // Documents why the root is kept away from the host: its command-line provider reads "/path" as a switch.
    [Fact]
    public void Parse_AbsoluteRoot_WouldBeMisreadAsAnOptionByTheHostCommandLineProvider()
    {
        string[] args = ["/home/user/.claude", "--urls", "http://localhost:1"];

        IConfigurationRoot withRoot = new ConfigurationBuilder().AddCommandLine(args).Build();
        IConfigurationRoot withoutRoot = new ConfigurationBuilder().AddCommandLine(CommandLine.Parse(args).HostArguments).Build();

        // The path becomes a bogus key and swallows the next option as its value.
        withRoot["home/user/.claude"].ShouldBe("--urls");
        withRoot["urls"].ShouldBeNull();

        withoutRoot["urls"].ShouldBe("http://localhost:1");
        withoutRoot.AsEnumerable().Count(pair => pair.Value is not null).ShouldBe(1);
    }
}
