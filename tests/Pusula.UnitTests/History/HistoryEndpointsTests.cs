using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.Extensions.Logging.Abstractions;
using Pusula.History;
using Pusula.Indexing;
using Pusula.Startup;
using Pusula.UnitTests.Support;
using Shouldly;
using Xunit;

namespace Pusula.UnitTests.History;

public sealed class HistoryEndpointsTests
{
    private static readonly ManualTimeProvider Clock = new(new DateTimeOffset(2026, 10, 12, 9, 14, 3, TimeSpan.Zero), TimeZoneInfo.Utc);

    private static HistoryStore Store(string state) =>
        new(new UserDirectories(string.Empty, string.Empty, state), Clock, NullLogger<HistoryStore>.Instance);

    private static FakeIndexProvider Index(string root) =>
        new(IndexFactory.Index([IndexFactory.File("a.md", tokens: new TokenCounts(5, 0, 0))], root: root));

    [Fact]
    public void GetChanges_StateFolderNotInsideAnySource_KeepsTheSummaryAndWritesNothingToTheSources()
    {
        using var temp = new TempDirectory();
        string claude = temp.CreateDirectory("claude");
        string vault = temp.CreateDirectory("vault");

        Ok<ChangesResponse> result = HistoryEndpoints.GetChanges(Index(claude), Store(temp.Resolve("state")), new FakeSourceRegistry(claude, vault));

        result.Value.ShouldBe(new ChangesResponse(HistoryState.Kept, new DateOnly(2026, 10, 12)));
        Directory.GetFiles(temp.Resolve("state/pusula/history")).Length.ShouldBe(1);
        Directory.GetFileSystemEntries(claude).ShouldBeEmpty();
        Directory.GetFileSystemEntries(vault).ShouldBeEmpty();
    }

    // The folders of all the sources are the folders that pusula does not write into, and not only the folder of the source that is asked for.
    [Fact]
    public void GetChanges_StateFolderInsideAnotherSourceThanTheOneAskedFor_IsInsideSourceAndNothingIsWritten()
    {
        using var temp = new TempDirectory();
        string claude = temp.CreateDirectory("claude");
        string vault = temp.CreateDirectory("vault");

        Ok<ChangesResponse> result = HistoryEndpoints.GetChanges(Index(claude), Store(Path.Join(vault, "state")), new FakeSourceRegistry(claude, vault));

        result.Value.ShouldBe(new ChangesResponse(HistoryState.InsideSource));
        Directory.GetFileSystemEntries(vault).ShouldBeEmpty();
        Directory.GetFileSystemEntries(claude).ShouldBeEmpty();
    }
}
