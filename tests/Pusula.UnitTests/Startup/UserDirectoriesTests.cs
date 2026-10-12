using Pusula.Startup;
using Shouldly;
using Xunit;

namespace Pusula.UnitTests.Startup;

public sealed class UserDirectoriesTests
{
    // What the runtime gives: a folder that does not exist is an empty string unless it is asked not to verify it.
    private static string FolderPath(Environment.SpecialFolder folder, Environment.SpecialFolderOption option) => (folder, option) switch
    {
        (Environment.SpecialFolder.UserProfile, _) => "/home/someone",
        (Environment.SpecialFolder.ApplicationData, Environment.SpecialFolderOption.DoNotVerify) => "/home/someone/.config",
        (Environment.SpecialFolder.LocalApplicationData, Environment.SpecialFolderOption.DoNotVerify) => @"C:\Users\someone\AppData\Local",
        _ => string.Empty,
    };

    // A computer without a home directory (a service account): the runtime has nothing to say about any folder of the user.
    private static string NoFolders(Environment.SpecialFolder folder, Environment.SpecialFolderOption option) => string.Empty;

    private static string? NoVariables(string name) => null;

    private static Func<string, string?> Variables(string? stateHome) =>
        name => name == "XDG_STATE_HOME" ? stateHome : null;

    private static string LocalState => Path.Join("/home/someone", ".local", "state");

    // A new Mac, or any computer where nothing has made ~/.config yet: the application data directory does not exist. It is
    // still where the sources file goes (adding the first source creates it); without it no folder could be added on the page.
    [Fact]
    public void FromEnvironment_ApplicationDataThatDoesNotExistYet_IsStillTheDirectoryOfTheSourcesFile() =>
        UserDirectories.FromEnvironment(FolderPath).ShouldBe(new UserDirectories("/home/someone", "/home/someone/.config"));

    // ---- The state directory ------------------------------------------------------------------------------------

    [Fact]
    public void Constructor_TwoArguments_HasNoStateDirectory() =>
        new UserDirectories("/home/someone", "/home/someone/.config").State.ShouldBeEmpty();

    [Fact]
    public void FromEnvironment_WithTheEnvironment_AddsTheStateDirectoryToTheHomeAndTheApplicationDataDirectory() =>
        UserDirectories.FromEnvironment(FolderPath, NoVariables, isWindows: false).ShouldBe(new UserDirectories("/home/someone", "/home/someone/.config", LocalState));

    [Fact]
    public void FromEnvironment_StateHomeThatIsAnAbsolutePath_IsTheStateDirectory()
    {
        string stateHome = Path.GetFullPath("/var/lib/someone/state");

        UserDirectories.FromEnvironment(FolderPath, Variables(stateHome), isWindows: false).State.ShouldBe(stateHome);
    }

    [Theory]
    [InlineData("state")]
    [InlineData("./state")]
    [InlineData("../state")]
    [InlineData("~/state")]
    public void FromEnvironment_StateHomeThatIsARelativePath_IsIgnored(string stateHome) =>
        UserDirectories.FromEnvironment(FolderPath, Variables(stateHome), isWindows: false).State.ShouldBe(LocalState);

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void FromEnvironment_StateHomeThatIsNotSet_IsTheLocalStateDirectoryBelowTheHome(string? stateHome) =>
        UserDirectories.FromEnvironment(FolderPath, Variables(stateHome), isWindows: false).State.ShouldBe(LocalState);

    [Fact]
    public void FromEnvironment_NoHomeDirectory_HasNoStateDirectory() =>
        UserDirectories.FromEnvironment(NoFolders, NoVariables, isWindows: false).State.ShouldBeEmpty();

    [Fact]
    public void FromEnvironment_NoHomeDirectoryButAStateHome_UsesTheStateHome()
    {
        string stateHome = Path.GetFullPath("/var/lib/service/state");

        UserDirectories.FromEnvironment(NoFolders, Variables(stateHome), isWindows: false).State.ShouldBe(stateHome);
    }

    [Fact]
    public void FromEnvironment_Windows_IsTheLocalApplicationDataDirectoryWhateverTheStateHomeSays()
    {
        string stateHome = Path.GetFullPath("/var/lib/someone/state");

        UserDirectories.FromEnvironment(FolderPath, Variables(stateHome), isWindows: true).State.ShouldBe(@"C:\Users\someone\AppData\Local");
    }

    [Fact]
    public void FromEnvironment_WindowsWithoutALocalApplicationDataDirectory_HasNoStateDirectory() =>
        UserDirectories.FromEnvironment(NoFolders, NoVariables, isWindows: true).State.ShouldBeEmpty();

    [Fact]
    public void FromEnvironment_Elsewhere_OnlyAsksForTheStateHome()
    {
        var asked = new List<string>();

        UserDirectories.FromEnvironment(FolderPath, name =>
        {
            asked.Add(name);
            return null;
        }, isWindows: false);

        asked.ShouldBe(["XDG_STATE_HOME"]);
    }

    [Fact]
    public void FromEnvironment_Windows_DoesNotAskForTheStateHome()
    {
        var asked = new List<string>();

        UserDirectories.FromEnvironment(FolderPath, name =>
        {
            asked.Add(name);
            return null;
        }, isWindows: true);

        asked.ShouldBeEmpty();
    }
}
