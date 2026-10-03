using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.Extensions.Logging.Abstractions;
using Pusula.Browse;
using Pusula.Startup;
using Pusula.UnitTests.Support;
using Shouldly;
using Xunit;

namespace Pusula.UnitTests.Browse;

// The handlers on their own, over made-up directories. What the requests to them are allowed (the filter) and what the answers look like
// as JSON is for the tests of the whole application.
public sealed class BrowseEndpointsTests
{
    private static BrowseResponse Browse(string? path, string? hidden, UserDirectories directories, FakeSourceRegistry? registry = null)
    {
        Results<Ok<BrowseResponse>, ProblemHttpResult> result = BrowseEndpoints.GetBrowse(
            path, hidden, registry ?? new FakeSourceRegistry(), directories, BrowseLimits.Default, TestContext.Current.CancellationToken);

        return result.Result.ShouldBeOfType<Ok<BrowseResponse>>().Value.ShouldNotBeNull();
    }

    private static ProblemHttpResult Problem(string? path, UserDirectories directories) =>
        BrowseEndpoints.GetBrowse(path, hidden: null, new FakeSourceRegistry(), directories, BrowseLimits.Default, TestContext.Current.CancellationToken)
            .Result.ShouldBeOfType<ProblemHttpResult>();

    [Fact]
    public void GetBrowse_NoPath_IsTheHomeDirectoryWithItsParentAndTheFoldersInIt()
    {
        using var temp = new TempDirectory();
        var directories = new UserDirectories(temp.CreateDirectory("people/me"), string.Empty);
        temp.CreateDirectory("people/me/Documents");

        BrowseResponse response = Browse(path: null, hidden: null, directories);

        response.Path.ShouldBe(directories.Home);
        response.Display.ShouldBe("~");
        response.Home.ShouldBe(directories.Home);
        response.Parent.ShouldBe(temp.Resolve("people"));
        response.Folders.Select(folder => folder.Name).ShouldBe(["Documents"]);
        response.Truncated.ShouldBeFalse();
    }

    [Fact]
    public void GetBrowse_NoHomeDirectory_StartsAtTheRootOfTheFileSystemWhichIsTheHomeThen()
    {
        using var temp = new TempDirectory();
        temp.CreateDirectory("Documents");
        var directories = new UserDirectories(string.Empty, string.Empty);

        BrowseResponse response = Browse(temp.Path, hidden: null, directories);

        response.Path.ShouldBe(temp.Path);
        response.Display.ShouldBe(temp.Path);
        response.Home.ShouldBe(Path.GetPathRoot(Path.GetFullPath(Path.DirectorySeparatorChar.ToString())));
        response.Folders.Single().Display.ShouldBe(temp.Resolve("Documents"));
    }

    [Theory]
    [InlineData("1", true)]
    [InlineData("true", true)]
    [InlineData("TRUE", true)]
    [InlineData("True", true)]
    [InlineData("0", false)]
    [InlineData("false", false)]
    [InlineData("yes", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void GetBrowse_Hidden_IsOneOrTrueAndNothingElse(string? hidden, bool listed)
    {
        using var temp = new TempDirectory();
        var directories = new UserDirectories(temp.CreateDirectory("home"), string.Empty);
        temp.CreateDirectory("home/.hidden");
        temp.CreateDirectory("home/shown");

        BrowseResponse response = Browse(path: null, hidden, directories);

        response.Folders.Select(folder => folder.Name).ShouldBe(listed ? [".hidden", "shown"] : ["shown"]);
    }

    [Fact]
    public void GetBrowse_PathThatCannotBeListed_IsAProblemWithItsCode()
    {
        using var temp = new TempDirectory();
        var directories = new UserDirectories(temp.CreateDirectory("home"), string.Empty);

        ProblemHttpResult relative = Problem("relative", directories);
        ProblemHttpResult missing = Problem(temp.Resolve("home/missing"), directories);

        relative.StatusCode.ShouldBe(StatusCodes.Status400BadRequest);
        relative.ProblemDetails.Extensions["code"].ShouldBe("PathNotAbsolute");
        missing.StatusCode.ShouldBe(StatusCodes.Status404NotFound);
        missing.ProblemDetails.Extensions["code"].ShouldBe("FolderNotFound");
    }

    [Fact]
    public void GetFound_Folders_AreTheOnesThatWereFoundAndListedIsToldAtTheTimeOfTheAnswer()
    {
        using var temp = new TempDirectory();
        temp.CreateDirectory("home/Vault/.obsidian");
        temp.CreateDirectory("home/Other/.obsidian");
        string vault = temp.Resolve("home/Vault");
        var finder = new FolderFinder(
            new UserDirectories(temp.Resolve("home"), string.Empty),
            DriveFolders.None,
            BrowseLimits.Default,
            TimeProvider.System,
            NullLogger<FolderFinder>.Instance);

        Ok<FoundResponse> before = BrowseEndpoints.GetFound(new FakeSourceRegistry(), finder, TestContext.Current.CancellationToken);
        Ok<FoundResponse> after = BrowseEndpoints.GetFound(new FakeSourceRegistry(vault), finder, TestContext.Current.CancellationToken);

        before.Value.ShouldNotBeNull().Complete.ShouldBeTrue();
        before.Value.Folders.Select(folder => (folder.Name, folder.Listed)).ShouldBe([("Other", false), ("Vault", false)]);
        after.Value.ShouldNotBeNull().Folders.Select(folder => (folder.Name, folder.Listed)).ShouldBe([("Other", false), ("Vault", true)]);
    }
}
