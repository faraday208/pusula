using Pusula.IntegrationTests.Support;
using Pusula.Sources;
using Shouldly;
using Xunit;

namespace Pusula.IntegrationTests.Sources;

/// <summary>The watcher of the sources file on its own: real folder, real file watcher, a counter instead of the registry.</summary>
public sealed class SourcesFileWatcherTests : IDisposable
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);

    // Long enough for a report that should not come to have come.
    private static readonly TimeSpan Quiet = SourcesFileWatcher.Delay * 4;

    private readonly TempDirectory _temp = new();
    private readonly List<SourcesFileWatcher> _watchers = [];
    private int _reports;

    public void Dispose()
    {
        foreach (SourcesFileWatcher watcher in _watchers)
        {
            watcher.Dispose();
        }

        _temp.Dispose();
    }

    private int Reports => Volatile.Read(ref _reports);

    private SourcesFileWatcher Create(string file)
    {
        var watcher = new SourcesFileWatcher(file, () => Interlocked.Increment(ref _reports), new CapturingLogger<SourcesFileWatcher>());
        _watchers.Add(watcher);
        return watcher;
    }

    // A started watcher that has made its report of the start.
    private async Task<(SourcesFileWatcher Watcher, string File)> StartedAsync()
    {
        string file = _temp.Write("config/sources.json", "{}");
        SourcesFileWatcher watcher = Create(file);
        watcher.TryStart().ShouldBeTrue();
        await WaitForReportsAsync(1);
        return (watcher, file);
    }

    private async Task WaitForReportsAsync(int count)
    {
        DateTime deadline = DateTime.UtcNow + Timeout;
        while (Reports < count)
        {
            if (DateTime.UtcNow > deadline)
            {
                throw new TimeoutException($"Timed out waiting for report {count}; there were {Reports}.");
            }

            await Task.Delay(20, TestContext.Current.CancellationToken);
        }
    }

    private static Task QuietAsync() => Task.Delay(Quiet, TestContext.Current.CancellationToken);

    [Fact]
    public async Task TryStart_FolderThatExists_WatchesItAndReportsOnceBecauseWhatHappenedBeforeIsNotKnown()
    {
        string file = _temp.Write("config/sources.json", "{}");
        SourcesFileWatcher watcher = Create(file);

        watcher.TryStart().ShouldBeTrue();

        await WaitForReportsAsync(1);
        await QuietAsync();
        Reports.ShouldBe(1);
    }

    [Fact]
    public async Task TryStart_CalledAgainWhileWatching_ChangesNothing()
    {
        (SourcesFileWatcher watcher, _) = await StartedAsync();

        watcher.TryStart().ShouldBeTrue();
        watcher.TryStart().ShouldBeTrue();

        await QuietAsync();
        Reports.ShouldBe(1);
    }

    [Fact]
    public async Task TryStart_FolderThatDoesNotExistYet_IsFalseUntilItIsThere()
    {
        string file = _temp.Resolve("later/sources.json");
        SourcesFileWatcher watcher = Create(file);

        watcher.TryStart().ShouldBeFalse();
        await QuietAsync();
        Reports.ShouldBe(0);

        Directory.CreateDirectory(Path.GetDirectoryName(file)!);
        watcher.TryStart().ShouldBeTrue();

        await WaitForReportsAsync(1);
    }

    [Fact]
    public async Task Change_FileWrittenInPlace_IsReported()
    {
        (_, string file) = await StartedAsync();

        File.WriteAllText(file, "{ \"sources\": [] }");

        await WaitForReportsAsync(2);
    }

    [Fact]
    public async Task Change_FileReplacedTheWayEditorsSaveIt_IsReported()
    {
        (_, string file) = await StartedAsync();
        string temporary = _temp.Write("config/.sources.json.swp", "{ \"sources\": [] }");

        File.Move(temporary, file, overwrite: true);

        await WaitForReportsAsync(2);
    }

    [Fact]
    public async Task Change_FileDeleted_IsReported()
    {
        (_, string file) = await StartedAsync();

        File.Delete(file);

        await WaitForReportsAsync(2);
    }

    [Fact]
    public async Task Change_FileCreatedWhereThereWasNone_IsReported()
    {
        string file = _temp.Resolve("config/sources.json");
        Directory.CreateDirectory(Path.GetDirectoryName(file)!);
        Create(file).TryStart().ShouldBeTrue();
        await WaitForReportsAsync(1);

        File.WriteAllText(file, "{}");

        await WaitForReportsAsync(2);
    }

    [Fact]
    public async Task Change_BurstOfWrites_IsReportedOnceWhenTheFolderIsQuiet()
    {
        (_, string file) = await StartedAsync();

        for (int i = 0; i < 20; i++)
        {
            File.WriteAllText(file, $"{{ \"n\": {i} }}");
        }

        await WaitForReportsAsync(2);
        await QuietAsync();
        Reports.ShouldBe(2);
    }

    [Fact]
    public async Task Change_OtherFilesOfTheFolder_AreNotReported()
    {
        (_, string file) = await StartedAsync();
        string folder = Path.GetDirectoryName(file)!;

        _temp.Write("config/other.json", "{}");
        _temp.Write("config/sources.json.bak", "{}");
        _temp.Write("config/.sources.json.tmp", "{}");
        File.Move(Path.Join(folder, "other.json"), Path.Join(folder, "renamed.json"));
        File.Delete(Path.Join(folder, "renamed.json"));
        Directory.CreateDirectory(Path.Join(folder, "sources.json.d"));

        await QuietAsync();
        Reports.ShouldBe(1);
    }

    [Fact]
    public async Task OnError_StartsTheWatcherAgainAndReportsBecauseEventsMayHaveBeenLost()
    {
        (SourcesFileWatcher watcher, string file) = await StartedAsync();

        watcher.OnError(sender: null, new ErrorEventArgs(new IOException("the buffer overflowed")));
        await WaitForReportsAsync(2);

        // The next TryStart sees that the watcher broke and starts a new one, which reports again.
        watcher.TryStart().ShouldBeTrue();
        await WaitForReportsAsync(3);
        File.WriteAllText(file, "{ \"sources\": [] }");
        await WaitForReportsAsync(4);
    }

    [Fact]
    public async Task Dispose_StopsReportingAndAnythingAfterIt()
    {
        (SourcesFileWatcher watcher, string file) = await StartedAsync();

        watcher.Dispose();
        watcher.Dispose();
        File.WriteAllText(file, "{ \"sources\": [] }");
        await QuietAsync();

        Reports.ShouldBe(1);
        watcher.TryStart().ShouldBeFalse();
    }

    // The registry starts the watcher again after a change of the list and takes it down when it stops; both can come at once.
    [Fact]
    public async Task TryStartAndDispose_AtTheSameTimeAndManyTimes_NeverThrowAndLeaveNoWatcherBehind()
    {
        int open = OpenWatchers.Count();

        for (int round = 0; round < 100; round++)
        {
            SourcesFileWatcher watcher = Create(_temp.Write($"round{round}/sources.json", "{}"));

            await Race.RunAsync(
                () => Start(watcher),
                () => Dispose(watcher),
                () => Start(watcher),
                () => Dispose(watcher));

            watcher.TryStart().ShouldBeFalse();
        }

        await OpenWatchers.WaitUntilNoMoreThanAsync(open);

        static Task Start(SourcesFileWatcher watcher)
        {
            watcher.TryStart();
            return Task.CompletedTask;
        }

        static Task Dispose(SourcesFileWatcher watcher)
        {
            watcher.Dispose();
            return Task.CompletedTask;
        }
    }

    [Fact]
    public void Create_FileThatIsNotInAFolder_Throws() =>
        Should.Throw<ArgumentException>(() => new SourcesFileWatcher(Path.GetPathRoot(Path.GetTempPath())!, () => { }, new CapturingLogger<SourcesFileWatcher>()));
}
