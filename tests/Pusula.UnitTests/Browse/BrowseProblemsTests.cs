using Microsoft.AspNetCore.Http.HttpResults;
using Pusula.Browse;
using Shouldly;
using Xunit;

namespace Pusula.UnitTests.Browse;

public sealed class BrowseProblemsTests
{
    // The codes and statuses are part of the API: the page that picks a folder acts on them.
    [Theory]
    [InlineData("PathNotAbsolute", 400)]
    [InlineData("FolderNotFound", 404)]
    public void For_Error_HasTheStatusAndTheCodeOfThatName(string code, int status)
    {
        ProblemHttpResult result = BrowseProblems.For(Enum.Parse<BrowseError>(code));

        result.StatusCode.ShouldBe(status);
        result.ProblemDetails.Status.ShouldBe(status);
        result.ProblemDetails.Extensions["code"].ShouldBe(code);
        result.ProblemDetails.Title.ShouldNotBeNullOrWhiteSpace();
        result.ProblemDetails.Detail.ShouldNotBeNullOrWhiteSpace();
        result.ProblemDetails.Detail.ShouldNotContain('\n');
    }

    [Fact]
    public void For_EveryError_IsCovered() =>
        Enum.GetValues<BrowseError>().ShouldAllBe(error => BrowseProblems.For(error).ProblemDetails.Status >= 400);

    [Fact]
    public void For_ValueThatIsNoError_Throws() =>
        Should.Throw<ArgumentOutOfRangeException>(() => BrowseProblems.For((BrowseError)999));
}
