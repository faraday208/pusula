using Pusula.Sources;
using Shouldly;
using Xunit;

namespace Pusula.UnitTests.Sources;

public sealed class SourceErrorCodeTests
{
    // The names are part of the API: the page acts on them, and so does the 'code' of the 503 problem details.
    [Fact]
    public void SourceErrorCode_Names_AreTheOnesTheApiPromises() =>
        Enum.GetNames<SourceErrorCode>().ShouldBe(["FolderMissing", "NotReadable", "TooLarge"]);

    [Fact]
    public void ErrorCodeOf_FolderThatVanishedDuringTheScan_IsFolderMissing() =>
        SourceRegistry.ErrorCodeOf(new DirectoryNotFoundException("gone")).ShouldBe(SourceErrorCode.FolderMissing);

    [Fact]
    public void ErrorCodeOf_AnyOtherFailureOfTheScan_IsNotReadable()
    {
        Exception[] failures =
        [
            new UnauthorizedAccessException("denied"),
            new IOException("input/output error"),
            new FileNotFoundException("a file, not a folder"),
            new PathTooLongException(),
            new InvalidOperationException("something unexpected"),
        ];

        foreach (Exception failure in failures)
        {
            SourceRegistry.ErrorCodeOf(failure).ShouldBe(SourceErrorCode.NotReadable, failure.GetType().Name);
        }
    }
}
