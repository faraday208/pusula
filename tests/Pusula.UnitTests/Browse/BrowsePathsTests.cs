using Pusula.Browse;
using Pusula.Startup;
using Pusula.UnitTests.Support;
using Shouldly;
using Xunit;

namespace Pusula.UnitTests.Browse;

// The home directory here is a scratch directory: nothing reads or depends on the one of whoever runs the tests.
public sealed class BrowsePathsTests
{
    private static readonly string Separator = Path.DirectorySeparatorChar.ToString();

    private static UserDirectories HomeOf(TempDirectory temp) => new(temp.CreateDirectory("home"), temp.CreateDirectory("data"));

    private static string Resolve(string? typed, UserDirectories directories)
    {
        BrowsePaths.TryResolve(typed, directories, out string? fullPath, out BrowseError? error).ShouldBeTrue(error?.ToString());
        error.ShouldBeNull();
        return fullPath.ShouldNotBeNull();
    }

    private static BrowseError Reject(string? typed, UserDirectories directories)
    {
        BrowsePaths.TryResolve(typed, directories, out string? fullPath, out BrowseError? error).ShouldBeFalse();
        fullPath.ShouldBeNull();
        return error.ShouldNotBeNull();
    }

    // ---- The home directory -------------------------------------------------------------------------------------

    [Fact]
    public void HomeFolder_AbsolutePath_IsItWithoutASeparatorAtTheEnd()
    {
        using var temp = new TempDirectory();
        string home = temp.CreateDirectory("home");

        BrowsePaths.HomeFolder(new UserDirectories(home + Separator, string.Empty)).ShouldBe(home);
        BrowsePaths.HomeFolder(new UserDirectories(home, string.Empty)).ShouldBe(home);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("relative/home")]
    [InlineData("~")]
    [InlineData("/home/user\0")]
    public void HomeFolder_NoHomeOrOneThatIsNoAbsolutePath_IsNull(string home) =>
        BrowsePaths.HomeFolder(new UserDirectories(home, string.Empty)).ShouldBeNull();

    [Fact]
    public void StartFolder_HomeThatIsKnown_IsTheHome()
    {
        using var temp = new TempDirectory();
        UserDirectories directories = HomeOf(temp);

        BrowsePaths.StartFolder(directories).ShouldBe(directories.Home);
    }

    [Fact]
    public void StartFolder_NoHome_IsTheRootOfTheFileSystem() =>
        BrowsePaths.StartFolder(new UserDirectories(string.Empty, string.Empty)).ShouldBe(Path.GetPathRoot(Path.GetFullPath(Separator)));

    // ---- The folder a request names ----------------------------------------------------------------------------

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\t")]
    public void TryResolve_NoPath_IsTheHomeDirectory(string? typed)
    {
        using var temp = new TempDirectory();
        UserDirectories directories = HomeOf(temp);

        Resolve(typed, directories).ShouldBe(directories.Home);
    }

    [Fact]
    public void TryResolve_NoPathAndNoHome_IsTheRootOfTheFileSystem()
    {
        string root = Resolve(null, new UserDirectories(string.Empty, string.Empty));

        root.ShouldBe(Path.GetPathRoot(Path.GetFullPath(Separator)));
    }

    [Fact]
    public void TryResolve_Tilde_IsTheHomeDirectory()
    {
        using var temp = new TempDirectory();
        UserDirectories directories = HomeOf(temp);
        string documents = temp.CreateDirectory("home/Documents");

        Resolve("~", directories).ShouldBe(directories.Home);
        Resolve("~/", directories).ShouldBe(directories.Home);
        Resolve("~/Documents", directories).ShouldBe(documents);
        Resolve("~/Documents/", directories).ShouldBe(documents);
    }

    [Fact]
    public void TryResolve_AbsolutePath_IsResolvedWithoutDotsAndWithoutASeparatorAtTheEnd()
    {
        using var temp = new TempDirectory();
        UserDirectories directories = HomeOf(temp);
        string notes = temp.CreateDirectory("home/Documents/notes");
        temp.CreateDirectory("home/other");

        Resolve(notes, directories).ShouldBe(notes);
        Resolve(notes + Separator, directories).ShouldBe(notes);
        Resolve(Path.Join(directories.Home, "other", "..", "Documents", "notes"), directories).ShouldBe(notes);
        Resolve(Path.Join(notes, "."), directories).ShouldBe(notes);
    }

    [Fact]
    public void TryResolve_RootOfTheFileSystem_IsKeptAsItIs()
    {
        using var temp = new TempDirectory();
        string root = Path.GetPathRoot(temp.Path)!;

        Resolve(root, HomeOf(temp)).ShouldBe(root);
    }

    [Fact]
    public void TryResolve_PathIsUsedAsItIs_NotTrimmed()
    {
        using var temp = new TempDirectory();
        UserDirectories directories = HomeOf(temp);

        // Windows does not keep a space at the end of a folder name: there the folder is called Notes, and only the space in front is left to check.
        string spaced = temp.CreateDirectory(OperatingSystem.IsWindows() ? "home/Notes" : "home/Notes ");

        Resolve(spaced, directories).ShouldBe(spaced);
        Reject(" " + spaced, directories).ShouldBe(BrowseError.PathNotAbsolute);
    }

    [Theory]
    [InlineData("notes/work")]
    [InlineData("./notes")]
    [InlineData("../notes")]
    [InlineData("notes")]
    [InlineData("~other/notes")]
    [InlineData("~~")]
    [InlineData("a:b")]
    public void TryResolve_PathThatIsNotAbsolute_IsPathNotAbsolute(string typed)
    {
        using var temp = new TempDirectory();

        Reject(typed, HomeOf(temp)).ShouldBe(BrowseError.PathNotAbsolute);
    }

    [Theory]
    [InlineData("/tmp/a\0b")]
    [InlineData("\0")]
    [InlineData("relative\0path")]
    [InlineData("~/x\0")]
    public void TryResolve_PathWithACharacterNoPathHas_IsPathNotAbsolute(string typed)
    {
        using var temp = new TempDirectory();

        Reject(typed, HomeOf(temp)).ShouldBe(BrowseError.PathNotAbsolute);
    }

    [Fact]
    public void TryResolve_TildeWhereThereIsNoHome_IsPathNotAbsolute()
    {
        var directories = new UserDirectories(string.Empty, string.Empty);

        Reject("~", directories).ShouldBe(BrowseError.PathNotAbsolute);
        Reject("~/notes", directories).ShouldBe(BrowseError.PathNotAbsolute);
    }

    [Fact]
    public void TryResolve_FolderThatDoesNotExistOrIsAFile_IsFolderNotFound()
    {
        using var temp = new TempDirectory();
        UserDirectories directories = HomeOf(temp);
        string file = temp.Write("home/readme.md", "x");

        Reject(temp.Resolve("home/missing"), directories).ShouldBe(BrowseError.FolderNotFound);
        Reject("~/missing", directories).ShouldBe(BrowseError.FolderNotFound);
        Reject(file, directories).ShouldBe(BrowseError.FolderNotFound);
    }

    [Fact]
    public void TryResolve_HomeThatDoesNotExist_IsFolderNotFound()
    {
        using var temp = new TempDirectory();
        var directories = new UserDirectories(temp.Resolve("gone"), string.Empty);

        Reject(null, directories).ShouldBe(BrowseError.FolderNotFound);
    }

    // A home directory is not what a request says, so it is not checked up front: the framework refuses what no path is.
    [Fact]
    public void TryResolve_TildeWhereTheHomeDirectoryHasACharacterNoPathHas_IsFolderNotFoundAndNeverAnError()
    {
        var directories = new UserDirectories(Path.GetFullPath("/home/user") + "\0", string.Empty);

        Reject("~/notes", directories).ShouldBe(BrowseError.FolderNotFound);
        Reject("~", directories).ShouldBe(BrowseError.FolderNotFound);
    }

    [Fact]
    public void TryResolve_PathThatIsTooLong_IsFolderNotFoundAndNeverAnError()
    {
        using var temp = new TempDirectory();
        string tooLong = Path.Join(temp.Path, new string('x', 5000));

        Reject(tooLong, HomeOf(temp)).ShouldBe(BrowseError.FolderNotFound);
    }

    // ---- How a path is written for a person ---------------------------------------------------------------------

    [Theory]
    [InlineData("/home/test", "/home/test", "~")]
    [InlineData("/home/test/", "/home/test", "~")]
    [InlineData("/home/test/Documents", "/home/test", "~/Documents")]
    [InlineData("/home/test/Documents/notes", "/home/test/", "~/Documents/notes")]
    [InlineData("/home/test/.claude", "/home/test", "~/.claude")]
    [InlineData("/home/test2/Documents", "/home/test", "/home/test2/Documents")]
    [InlineData("/home/tes", "/home/test", "/home/tes")]
    [InlineData("/home", "/home/test", "/home")]
    [InlineData("/mnt/storage/Vault", "/home/test", "/mnt/storage/Vault")]
    [InlineData("/", "/home/test", "/")]
    [InlineData("/etc", "/", "/etc")]
    [InlineData("/", "/", "~")]
    public void Display_Path_IsRelativeToTheHomeDirectoryOnlyBelowIt(string fullPath, string home, string expected) =>
        BrowsePaths.Display(fullPath, home).ShouldBe(expected);

    [Fact]
    public void Display_NoHome_IsTheFullPath() =>
        BrowsePaths.Display("/home/test/Documents", home: null).ShouldBe("/home/test/Documents");

    [Fact]
    public void Display_CaseOfTheHome_IsComparedTheWayThePlatformDoes() =>
        BrowsePaths.Display("/home/TEST/Documents", "/home/test").ShouldBe(OperatingSystem.IsWindows() ? "~/Documents" : "/home/TEST/Documents");

    [Theory]
    [InlineData("/home/test/Documents", "/home/test")]
    [InlineData("/home/test", "/home")]
    [InlineData("/home", "/")]
    public void Parent_Folder_IsTheFolderAboveIt(string fullPath, string expected) =>
        BrowsePaths.Parent(Path.GetFullPath(fullPath)).ShouldBe(Path.GetFullPath(expected));

    [Fact]
    public void Parent_RootOfTheFileSystem_IsNull() =>
        BrowsePaths.Parent(Path.GetPathRoot(Path.GetFullPath(Separator))!).ShouldBeNull();
}
