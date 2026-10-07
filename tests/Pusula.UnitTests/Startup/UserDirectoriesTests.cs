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
        _ => string.Empty,
    };

    // A new Mac, or any computer where nothing has made ~/.config yet: the application data directory does not exist. It is
    // still where the sources file goes (adding the first source creates it); without it no folder could be added on the page.
    [Fact]
    public void FromEnvironment_ApplicationDataThatDoesNotExistYet_IsStillTheDirectoryOfTheSourcesFile() =>
        UserDirectories.FromEnvironment(FolderPath).ShouldBe(new UserDirectories("/home/someone", "/home/someone/.config"));
}
