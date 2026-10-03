using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Pusula.Sources;
using Shouldly;
using Xunit;

namespace Pusula.UnitTests.Sources;

public sealed class SourceProblemsTests
{
    // The codes and statuses are part of the API: the page that adds and removes sources acts on them.
    [Theory]
    [InlineData("PathRequired", 400)]
    [InlineData("PathNotAbsolute", 400)]
    [InlineData("FolderNotFound", 400)]
    [InlineData("TooBroad", 400)]
    [InlineData("InvalidName", 400)]
    [InlineData("InvalidProfile", 400)]
    [InlineData("Remote", 403)]
    [InlineData("CommandLine", 403)]
    [InlineData("CrossOrigin", 403)]
    [InlineData("NotFound", 404)]
    [InlineData("AlreadyListed", 409)]
    [InlineData("FileInvalid", 409)]
    [InlineData("WriteFailed", 500)]
    public void For_Error_HasTheStatusAndTheCodeOfThatName(string code, int status)
    {
        ProblemHttpResult result = SourceProblems.For(Enum.Parse<EditError>(code));

        result.StatusCode.ShouldBe(status);
        result.ProblemDetails.Status.ShouldBe(status);
        result.ProblemDetails.Extensions["code"].ShouldBe(code);
        result.ProblemDetails.Title.ShouldNotBeNullOrWhiteSpace();
        result.ProblemDetails.Detail.ShouldNotBeNullOrWhiteSpace();
        result.ProblemDetails.Detail.ShouldNotContain('\n');
    }

    [Fact]
    public void For_EveryError_IsCovered() =>
        Enum.GetValues<EditError>().ShouldAllBe(error => SourceProblems.For(error, null).ProblemDetails.Status >= StatusCodes.Status400BadRequest);

    [Fact]
    public void For_FileInvalid_SaysWhyTheFileCannotBeUsed()
    {
        SourceProblems.For(EditError.FileInvalid, "invalid JSON: bad").ProblemDetails.Detail
            .ShouldBe("The sources file cannot be used, so it was left as it is: invalid JSON: bad");
        SourceProblems.For(EditError.FileInvalid).ProblemDetails.Detail
            .ShouldBe("The sources file cannot be used, so it was left as it is.");
    }

    [Fact]
    public void For_OtherErrors_IgnoreTheReason() =>
        SourceProblems.For(EditError.TooBroad, "something else").ProblemDetails.Detail.ShouldNotBeNull().ShouldNotContain("something else");

    [Fact]
    public void For_InvalidName_NamesTheLimit() =>
        SourceProblems.For(EditError.InvalidName).ProblemDetails.Detail.ShouldNotBeNull().ShouldContain("80");

    [Fact]
    public void For_ValueThatIsNoError_Throws() =>
        Should.Throw<ArgumentOutOfRangeException>(() => SourceProblems.For((EditError)999));
}
