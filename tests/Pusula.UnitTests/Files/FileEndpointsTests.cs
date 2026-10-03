using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Pusula.Files;
using Pusula.Indexing;
using Pusula.Links;
using Pusula.UnitTests.Support;
using Shouldly;
using Xunit;

namespace Pusula.UnitTests.Files;

public sealed class FileEndpointsTests
{
    private static readonly FakeIndexProvider Provider = new(IndexFactory.Index(
    [
        IndexFactory.File("CLAUDE.md", Layer.ClaudeMd),
        IndexFactory.File("rules/a.md"),
    ]));

    private static ProblemHttpResult Problem(Microsoft.AspNetCore.Http.HttpResults.Results<Ok<FileResponse>, ProblemHttpResult> result) =>
        result.Result.ShouldBeOfType<ProblemHttpResult>();

    [Fact]
    public void GetFile_IndexedPath_ReturnsTheFile()
    {
        var result = FileEndpoints.GetFile("rules/a.md", Provider);

        Ok<FileResponse> ok = result.Result.ShouldBeOfType<Ok<FileResponse>>();
        ok.Value.ShouldNotBeNull().Path.ShouldBe("rules/a.md");
        ok.StatusCode.ShouldBe(StatusCodes.Status200OK);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    [InlineData("\t")]
    public void GetFile_MissingOrBlankPath_Is400(string? path)
    {
        ProblemHttpResult problem = Problem(FileEndpoints.GetFile(path, Provider));

        problem.StatusCode.ShouldBe(StatusCodes.Status400BadRequest);
        problem.ProblemDetails.Title.ShouldBe("Missing path");
        problem.ProblemDetails.Detail.ShouldBe("The 'path' query parameter is required.");
    }

    [Theory]
    [InlineData("nope.md")]
    [InlineData("../CLAUDE.md")]
    [InlineData("/CLAUDE.md")]
    [InlineData("./CLAUDE.md")]
    [InlineData("rules")]
    [InlineData("claude.md")]
    public void GetFile_PathThatIsNotAnIndexKey_Is404(string path)
    {
        ProblemHttpResult problem = Problem(FileEndpoints.GetFile(path, Provider));

        problem.StatusCode.ShouldBe(StatusCodes.Status404NotFound);
        problem.ProblemDetails.ShouldBeOfType<ProblemDetails>().Title.ShouldBe("File not found");
    }

    [Fact]
    public void GetFile_LooksOnlyAtTheIndex_NeverAtTheFileSystem()
    {
        // The root of this index does not exist: nothing could be read from it, and nothing is tried.
        var provider = new FakeIndexProvider(IndexFactory.Index([IndexFactory.File("only-in-memory.md")], root: "/definitely/not/a/folder"));

        FileEndpoints.GetFile("only-in-memory.md", provider).Result.ShouldBeOfType<Ok<FileResponse>>();
        Problem(FileEndpoints.GetFile("/definitely/not/a/folder/only-in-memory.md", provider)).StatusCode.ShouldBe(StatusCodes.Status404NotFound);
    }

    [Fact]
    public void GetFile_Backlink_CarriesTheTextOfTheLinkingLine()
    {
        var provider = new FakeIndexProvider(IndexFactory.Index(
        [
            IndexFactory.File("rules/a.md"),
            IndexFactory.File(
                "CLAUDE.md",
                Layer.ClaudeMd,
                links: [IndexFactory.Link(LinkKind.MarkdownLink, "rules/a.md", 2, LinkStatus.Resolved, "rules/a.md")],
                content: "# Title\n  See [a](rules/a.md).  \n"),
        ]));

        FileResponse file = FileEndpoints.GetFile("rules/a.md", provider).Result.ShouldBeOfType<Ok<FileResponse>>().Value.ShouldNotBeNull();

        file.Backlinks.ShouldBe([new FileBacklink("CLAUDE.md", LinkKind.MarkdownLink, 2, "See [a](rules/a.md).")]);
    }

    [Fact]
    public void GetFile_MalformedFrontmatter_CarriesTheLineOfTheErrorAndItsText()
    {
        var provider = new FakeIndexProvider(IndexFactory.Index(
        [
            IndexFactory.File(
                "rules/broken.md",
                frontmatterError: "Line 3, column 20: While scanning a plain scalar value, found invalid mapping.",
                frontmatterErrorLine: 3,
                body: "body\n",
                bodyStartLine: 5,
                content: "---\nname: broken\n  description: Does X: then Y  \n---\nbody\n"),
        ]));

        FileResponse file = FileEndpoints.GetFile("rules/broken.md", provider).Result.ShouldBeOfType<Ok<FileResponse>>().Value.ShouldNotBeNull();

        file.FrontmatterError.ShouldNotBeNull().ShouldStartWith("Line 3, column ");
        file.FrontmatterErrorLine.ShouldBe(3);
        file.FrontmatterErrorText.ShouldBe("description: Does X: then Y");
    }
}
