using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Pusula.IntegrationTests.Support;
using Pusula.Indexing;
using Pusula.Links;
using Pusula.LiveReload;
using Shouldly;
using Xunit;

namespace Pusula.IntegrationTests.LiveReload;

/// <summary>The index host on its own (real folder, real file watcher), without the web server.</summary>
public sealed class IndexHostTests : IDisposable
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);

    private readonly TempDirectory _root = new();
    private readonly CapturingLogger<IndexHost> _hostLog = new();
    private readonly CapturingLogger<IndexBuilder> _builderLog = new();
    private readonly List<IndexHost> _hosts = [];

    public void Dispose()
    {
        foreach (IndexHost host in _hosts)
        {
            host.Dispose();
        }

        _root.Dispose();
    }

    private IndexHost CreateHost(string? root = null, SourceProfile profile = SourceProfile.Claude, ILogger<IndexBuilder>? builderLog = null, TimeSpan? disposeTimeout = null)
    {
        var host = new IndexHost(new IndexBuilder(builderLog ?? _builderLog), root ?? _root.Path, profile, _hostLog)
        {
            WatcherRetryDelay = TimeSpan.FromMilliseconds(200),
            DisposeTimeout = disposeTimeout ?? Timeout,
        };
        _hosts.Add(host);
        return host;
    }

    private async Task<IndexHost> StartHostAsync()
    {
        IndexHost host = CreateHost();
        await host.StartAsync(TestContext.Current.CancellationToken);
        return host;
    }

    private int BuildCount => _builderLog.Entries.Count(entry => entry.Message.StartsWith("Indexed ", StringComparison.Ordinal));

    private static async Task<IndexChange> NextAsync(IAsyncEnumerator<IndexChange> changes)
    {
        bool hasNext = await changes.MoveNextAsync().AsTask().WaitAsync(Timeout, TestContext.Current.CancellationToken);
        hasNext.ShouldBeTrue("The change sequence ended.");
        return changes.Current;
    }

    private static async Task WaitUntilAsync(Func<bool> condition, string what)
    {
        DateTime deadline = DateTime.UtcNow + Timeout;
        while (!condition())
        {
            if (DateTime.UtcNow > deadline)
            {
                throw new TimeoutException($"Timed out waiting for: {what}");
            }

            await Task.Delay(20, TestContext.Current.CancellationToken);
        }
    }

    // ---- Start and stop ---------------------------------------------------------------------------------------

    [Fact]
    public async Task Current_BeforeStart_Throws()
    {
        IndexHost host = CreateHost();

        Should.Throw<InvalidOperationException>(() => host.Current);
        Should.Throw<InvalidOperationException>(() => host.WatchAsync(TestContext.Current.CancellationToken));
        await host.StopAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task StartAsync_BuildsTheIndexAtVersionOne()
    {
        _root.Write("CLAUDE.md", "# Hello\n");
        _root.Write("rules/a.md", "# A\n");

        IndexHost host = await StartHostAsync();

        host.Current.Version.ShouldBe(1);
        host.Current.Root.ShouldBe(_root.Path);
        host.Current.Files.Keys.ShouldBe(["CLAUDE.md", "rules/a.md"]);
    }

    [Fact]
    public async Task StartAsync_RootThatDoesNotExist_ThrowsDirectoryNotFoundNamingIt()
    {
        string missing = _root.Resolve("missing");
        IndexHost host = CreateHost(missing);

        DirectoryNotFoundException exception = await Should.ThrowAsync<DirectoryNotFoundException>(() => host.StartAsync(TestContext.Current.CancellationToken));

        exception.Message.ShouldContain(missing);
    }

    [Fact]
    public async Task StopAsync_EndsEverySubscription()
    {
        IndexHost host = await StartHostAsync();
        await using IAsyncEnumerator<IndexChange> changes = host.WatchAsync(TestContext.Current.CancellationToken).GetAsyncEnumerator(TestContext.Current.CancellationToken);
        Task<bool> pending = changes.MoveNextAsync().AsTask();

        await host.StopAsync(TestContext.Current.CancellationToken);

        (await pending.WaitAsync(Timeout, TestContext.Current.CancellationToken)).ShouldBeFalse();
    }

    [Fact]
    public async Task Dispose_CalledTwiceOrAfterChanges_IsHarmless()
    {
        IndexHost host = await StartHostAsync();

        host.Dispose();
        host.Dispose();
        _root.Write("rules/after.md", "# written after the host was disposed\n");
        await Task.Delay(TimeSpan.FromMilliseconds(500), TestContext.Current.CancellationToken);

        _hostLog.Entries.ShouldNotContain(entry => entry.Level >= LogLevel.Error);
    }

    // ---- Closing, in whatever order ---------------------------------------------------------------------------

    private const int Rounds = 100;

    [Fact]
    public async Task StopAsync_AfterTheHostWasDisposed_IsHarmless()
    {
        IndexHost host = await StartHostAsync();

        host.Dispose();
        await host.StopAsync(TestContext.Current.CancellationToken);
        await host.StopAsync(TestContext.Current.CancellationToken);

        _hostLog.Entries.ShouldNotContain(entry => entry.Level >= LogLevel.Warning);
    }

    [Fact]
    public async Task RefreshAsync_AfterTheHostWasDisposed_DoesNothing()
    {
        _root.Write("rules/a.md", "# A\n");
        IndexHost host = await StartHostAsync();
        host.Dispose();
        _root.Write("rules/b.md", "# B\n");

        await host.RefreshAsync(TestContext.Current.CancellationToken);
        await host.RefreshSafelyAsync();

        host.Current.Version.ShouldBe(1);
        host.Current.Files.Keys.ShouldBe(["rules/a.md"]);
        _hostLog.Entries.ShouldNotContain(entry => entry.Level >= LogLevel.Warning);
    }

    // What the host's own timer and the watcher do, and what the registry does when a source goes, can come at any
    // moment while the host is being taken down: none of it may throw, and no watcher may be left running.
    [Fact]
    public async Task StopAsyncAndDispose_AtTheSameTimeAndManyTimes_NeverThrowAndLeaveNoWatcherBehind()
    {
        int watchers = OpenWatchers.Count();

        for (int round = 0; round < Rounds; round++)
        {
            using var folder = new TempDirectory();
            folder.Write("rules/a.md", "# A\n");
            var host = new IndexHost(new IndexBuilder(NullLogger<IndexBuilder>.Instance), folder.Path, SourceProfile.Claude, _hostLog);
            await host.StartAsync(TestContext.Current.CancellationToken);
            Task ended = RegistryHarness.StreamEndsAsync(host);

            await Race.RunAsync(
                () => host.StopAsync(TestContext.Current.CancellationToken),
                () => Dispose(host),
                () => host.StopAsync(TestContext.Current.CancellationToken),
                () => Dispose(host),
                () => host.RefreshAsync(TestContext.Current.CancellationToken),
                () => WriteAsync(folder, $"rules/round{round}.md"));

            await ended.WaitAsync(Timeout, TestContext.Current.CancellationToken);
        }

        _hostLog.Entries.ShouldNotContain(entry => entry.Level >= LogLevel.Warning);
        await OpenWatchers.WaitUntilNoMoreThanAsync(watchers);

        static Task Dispose(IndexHost host)
        {
            host.Dispose();
            return Task.CompletedTask;
        }

        static Task WriteAsync(TempDirectory folder, string path)
        {
            folder.Write(path, "# written while the host closes\n");
            return Task.CompletedTask;
        }
    }

    [Fact]
    public async Task StopAsyncAndDispose_WhileARebuildIsUnderWay_WaitForItAndThenTakeTheHostDown()
    {
        var latch = new Latch();
        IndexHost host = CreateHost(builderLog: new BlockingLogger<IndexBuilder>(_builderLog, latch, "Indexed "));
        await host.StartAsync(TestContext.Current.CancellationToken);
        await WaitUntilAsync(() => BuildCount >= 2, "the catch-up rebuild after the start");
        await using IAsyncEnumerator<IndexChange> changes = host.WatchAsync(TestContext.Current.CancellationToken).GetAsyncEnumerator(TestContext.Current.CancellationToken);
        Task<bool> first = changes.MoveNextAsync().AsTask();

        // The rebuild has scanned the folder and is held at the end of the scan, with the gate.
        _root.Write("rules/a.md", "# A\n");
        latch.Arm();
        Task rebuild = Task.Run(() => host.RefreshAsync(TestContext.Current.CancellationToken), TestContext.Current.CancellationToken);
        await latch.ParkedAsync();

        Task stopping = host.StopAsync(TestContext.Current.CancellationToken);
        Task disposing = Task.Factory.StartNew(host.Dispose, CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default);
        await Task.Delay(TimeSpan.FromMilliseconds(300), TestContext.Current.CancellationToken);

        stopping.IsCompleted.ShouldBeFalse();
        disposing.IsCompleted.ShouldBeFalse();
        first.IsCompleted.ShouldBeFalse();

        latch.Release();
        await Task.WhenAll(rebuild, stopping, disposing).WaitAsync(Timeout, TestContext.Current.CancellationToken);

        // The rebuild that was under way was finished and its index kept; then the sequence ended. (The host ends it right
        // after the last announcement, so a subscriber that has not read that one yet may not get it.)
        host.Current.Version.ShouldBe(2);
        host.Current.Files.Keys.ShouldBe(["rules/a.md"]);
        bool more = await first.WaitAsync(Timeout, TestContext.Current.CancellationToken);
        while (more)
        {
            more = await changes.MoveNextAsync().AsTask().WaitAsync(Timeout, TestContext.Current.CancellationToken);
        }

        _hostLog.Entries.ShouldNotContain(entry => entry.Level >= LogLevel.Warning);
    }

    // A rebuild that is not done by the time Dispose has waited as long as it is told to does not hold it up: the rebuild is
    // the last one in the gate, and takes the host down when it leaves.
    [Fact]
    public async Task Dispose_WhileARebuildTakesLongerThanTheTimeout_ReturnsAndTheRebuildTakesTheHostDownWhenItIsDone()
    {
        int watchers = OpenWatchers.Count();
        var latch = new Latch();
        IndexHost host = CreateHost(builderLog: new BlockingLogger<IndexBuilder>(_builderLog, latch, "Indexed "), disposeTimeout: TimeSpan.FromMilliseconds(200));
        await host.StartAsync(TestContext.Current.CancellationToken);
        await WaitUntilAsync(() => BuildCount >= 2, "the catch-up rebuild after the start");
        await using IAsyncEnumerator<IndexChange> changes = host.WatchAsync(TestContext.Current.CancellationToken).GetAsyncEnumerator(TestContext.Current.CancellationToken);
        Task<bool> first = changes.MoveNextAsync().AsTask();
        latch.Arm();
        Task rebuild = Task.Run(() => host.RefreshAsync(TestContext.Current.CancellationToken), TestContext.Current.CancellationToken);
        await latch.ParkedAsync();

        host.Dispose();

        rebuild.IsCompleted.ShouldBeFalse();
        first.IsCompleted.ShouldBeFalse();
        _hostLog.Entries.ShouldContain(entry => entry.Level == LogLevel.Warning && entry.Message.Contains("still under way", StringComparison.Ordinal));

        latch.Release();
        await rebuild.WaitAsync(Timeout, TestContext.Current.CancellationToken);
        bool more = await first.WaitAsync(Timeout, TestContext.Current.CancellationToken);
        while (more)
        {
            more = await changes.MoveNextAsync().AsTask().WaitAsync(Timeout, TestContext.Current.CancellationToken);
        }

        await OpenWatchers.WaitUntilNoMoreThanAsync(watchers);
    }

    // ---- Rebuilds and announcements ---------------------------------------------------------------------------

    [Fact]
    public async Task RefreshAsync_FileChanged_PublishesTheNextVersionToSubscribers()
    {
        _root.Write("rules/a.md", "# A\n");
        IndexHost host = await StartHostAsync();
        await using IAsyncEnumerator<IndexChange> changes = host.WatchAsync(TestContext.Current.CancellationToken).GetAsyncEnumerator(TestContext.Current.CancellationToken);

        _root.Write("rules/a.md", "# A, edited\n");
        _root.Write("rules/b.md", "# B\n");
        await host.RefreshAsync(TestContext.Current.CancellationToken);
        IndexChange change = await NextAsync(changes);

        change.Version.ShouldBe(2);
        change.Changed.ShouldBe(["rules/a.md"]);
        change.Added.ShouldBe(["rules/b.md"]);
        change.Removed.ShouldBeEmpty();
        host.Current.Version.ShouldBe(2);
        host.Current.Files["rules/a.md"].Content.ShouldBe("# A, edited\n");
    }

    [Fact]
    public async Task RefreshAsync_NothingChanged_KeepsTheVersionAndAnnouncesNothing()
    {
        _root.Write("rules/a.md", "# A\n");
        IndexHost host = await StartHostAsync();
        await using IAsyncEnumerator<IndexChange> changes = host.WatchAsync(TestContext.Current.CancellationToken).GetAsyncEnumerator(TestContext.Current.CancellationToken);

        await host.RefreshAsync(TestContext.Current.CancellationToken);
        await host.RefreshAsync(TestContext.Current.CancellationToken);
        _root.Write("rules/a.md", "# A, edited\n");
        await host.RefreshAsync(TestContext.Current.CancellationToken);

        // The first announcement is the edit, at version 2: the refreshes before it announced nothing.
        IndexChange change = await NextAsync(changes);
        change.Version.ShouldBe(2);
        change.Changed.ShouldBe(["rules/a.md"]);
    }

    [Fact]
    public async Task RefreshAsync_OnlyALinkTargetAppeared_ServesTheFreshSnapshotAtTheSameVersion()
    {
        _root.Write("CLAUDE.md", "See [the assets](assets/).\n");
        IndexHost host = await StartHostAsync();
        host.Current.Files["CLAUDE.md"].Links.Single().Status.ShouldBe(LinkStatus.Broken);

        Directory.CreateDirectory(_root.Resolve("assets"));
        await host.RefreshAsync(TestContext.Current.CancellationToken);

        host.Current.Version.ShouldBe(1);
        host.Current.Files["CLAUDE.md"].Links.Single().Status.ShouldBe(LinkStatus.NonMarkdown);
    }

    [Fact]
    public async Task RefreshAsync_OnlyTheTimeOfAFileChanged_AnnouncesNothingButServesTheNewTime()
    {
        string note = _root.Write("rules/a.md", "# A\n");
        File.SetLastWriteTimeUtc(note, new DateTime(2025, 1, 2, 3, 4, 5, DateTimeKind.Utc));
        IndexHost host = await StartHostAsync();
        await using IAsyncEnumerator<IndexChange> changes = host.WatchAsync(TestContext.Current.CancellationToken).GetAsyncEnumerator(TestContext.Current.CancellationToken);
        host.Current.Files["rules/a.md"].ModifiedAt.ShouldBe(new DateTimeOffset(2025, 1, 2, 3, 4, 5, TimeSpan.Zero));
        var touched = new DateTime(2026, 6, 7, 8, 9, 10, DateTimeKind.Utc);

        File.SetLastWriteTimeUtc(note, touched);
        await host.RefreshAsync(TestContext.Current.CancellationToken);

        host.Current.Version.ShouldBe(1);
        host.Current.Files["rules/a.md"].ModifiedAt.ShouldBe(new DateTimeOffset(touched));

        // The first announcement is the edit that follows, at version 2: touching the file announced nothing.
        _root.Write("rules/a.md", "# A, edited\n");
        await host.RefreshAsync(TestContext.Current.CancellationToken);
        IndexChange change = await NextAsync(changes);
        change.Version.ShouldBe(2);
        change.Changed.ShouldBe(["rules/a.md"]);
    }

    [Fact]
    public async Task WatchAsync_SubscriberThatIsSlow_StillReceivesEveryChangeInOrder()
    {
        IndexHost host = await StartHostAsync();
        await using IAsyncEnumerator<IndexChange> changes = host.WatchAsync(TestContext.Current.CancellationToken).GetAsyncEnumerator(TestContext.Current.CancellationToken);

        // Nobody reads while the changes are published, and the subscriber has not even asked for its first element.
        for (int i = 1; i <= 5; i++)
        {
            _root.Write($"rules/r{i}.md", $"# {i}\n");
            await host.RefreshAsync(TestContext.Current.CancellationToken);
        }

        var received = new List<IndexChange>();
        for (int i = 1; i <= 5; i++)
        {
            received.Add(await NextAsync(changes));
        }

        received.Select(change => change.Version).ShouldBe([2, 3, 4, 5, 6]);
        received.Select(change => change.Added.Single()).ShouldBe(["rules/r1.md", "rules/r2.md", "rules/r3.md", "rules/r4.md", "rules/r5.md"]);
    }

    [Fact]
    public async Task WatchAsync_SubscriptionStartsWhenItIsCreated_NotWhenTheFirstElementIsRequested()
    {
        IndexHost host = await StartHostAsync();
        IAsyncEnumerable<IndexChange> subscription = host.WatchAsync(TestContext.Current.CancellationToken);

        _root.Write("rules/early.md", "# early\n");
        await host.RefreshAsync(TestContext.Current.CancellationToken);

        await using IAsyncEnumerator<IndexChange> changes = subscription.GetAsyncEnumerator(TestContext.Current.CancellationToken);
        (await NextAsync(changes)).Added.ShouldBe(["rules/early.md"]);
    }

    [Fact]
    public async Task WatchAsync_SubscriberThatJoinsLater_ReceivesOnlyLaterChanges()
    {
        IndexHost host = await StartHostAsync();
        _root.Write("rules/before.md", "# before\n");
        await host.RefreshAsync(TestContext.Current.CancellationToken);

        await using IAsyncEnumerator<IndexChange> changes = host.WatchAsync(TestContext.Current.CancellationToken).GetAsyncEnumerator(TestContext.Current.CancellationToken);
        _root.Write("rules/after.md", "# after\n");
        await host.RefreshAsync(TestContext.Current.CancellationToken);

        IndexChange change = await NextAsync(changes);
        change.Version.ShouldBe(3);
        change.Added.ShouldBe(["rules/after.md"]);
    }

    [Fact]
    public async Task WatchAsync_Cancelled_EndsWithoutAnException()
    {
        IndexHost host = await StartHostAsync();
        using var cts = new CancellationTokenSource();
        await using IAsyncEnumerator<IndexChange> changes = host.WatchAsync(cts.Token).GetAsyncEnumerator(cts.Token);
        Task<bool> pending = changes.MoveNextAsync().AsTask();

        await cts.CancelAsync();

        (await pending.WaitAsync(Timeout, TestContext.Current.CancellationToken)).ShouldBeFalse();
    }

    // A source that is removed while the server runs has its host disposed; a client that began to follow it just
    // then must find the sequence over, not an exception.
    [Fact]
    public async Task WatchAsync_SequenceThatBeginsAfterTheHostWasDisposed_EndsWithoutAnException()
    {
        IndexHost host = await StartHostAsync();
        IAsyncEnumerable<IndexChange> changes = host.WatchAsync(TestContext.Current.CancellationToken);

        host.Dispose();
        await using IAsyncEnumerator<IndexChange> enumerator = changes.GetAsyncEnumerator(TestContext.Current.CancellationToken);

        (await enumerator.MoveNextAsync().AsTask().WaitAsync(Timeout, TestContext.Current.CancellationToken)).ShouldBeFalse();
    }

    [Fact]
    public async Task WatchAsync_EnumeratorTokenAndMethodToken_BothEndTheSequence()
    {
        IndexHost host = await StartHostAsync();
        using var enumerationToken = new CancellationTokenSource();
        await using IAsyncEnumerator<IndexChange> changes = host.WatchAsync(CancellationToken.None).GetAsyncEnumerator(enumerationToken.Token);
        Task<bool> pending = changes.MoveNextAsync().AsTask();

        await enumerationToken.CancelAsync();

        (await pending.WaitAsync(Timeout, TestContext.Current.CancellationToken)).ShouldBeFalse();
    }

    // ---- The file watcher -------------------------------------------------------------------------------------

    [Fact]
    public async Task Watcher_EditedFile_IsPublishedAfterTheDebounceWithoutAnyManualRefresh()
    {
        _root.Write("rules/a.md", "# A\n");
        IndexHost host = await StartHostAsync();
        await using IAsyncEnumerator<IndexChange> changes = host.WatchAsync(TestContext.Current.CancellationToken).GetAsyncEnumerator(TestContext.Current.CancellationToken);

        _root.Write("rules/a.md", "# A, edited by someone else\n");
        IndexChange change = await NextAsync(changes);

        change.Version.ShouldBe(2);
        change.Changed.ShouldBe(["rules/a.md"]);

        // The host announces the change first and logs it after that, so the entry may come a moment later than the change.
        await WaitUntilAsync(
            () => _hostLog.Entries.Any(entry => entry.Level == LogLevel.Information && entry.Message.Contains("version 2", StringComparison.Ordinal)),
            "the host to log the new version");
    }

    [Fact]
    public async Task Watcher_BurstOfChanges_IsCoalescedIntoOneVersion()
    {
        Directory.CreateDirectory(_root.Resolve("rules"));
        IndexHost host = await StartHostAsync();
        await using IAsyncEnumerator<IndexChange> changes = host.WatchAsync(TestContext.Current.CancellationToken).GetAsyncEnumerator(TestContext.Current.CancellationToken);

        for (int i = 1; i <= 6; i++)
        {
            _root.Write($"rules/burst{i}.md", $"# {i}\n");
        }

        IndexChange change = await NextAsync(changes);
        await Task.Delay(TimeSpan.FromMilliseconds(800), TestContext.Current.CancellationToken);

        change.Version.ShouldBe(2);
        change.Added.Count.ShouldBe(6);
        host.Current.Version.ShouldBe(2);
    }

    [Fact]
    public async Task Watcher_ChangesTheIndexIgnores_DoNotEvenTriggerARebuild()
    {
        // Windows reports a change of the parent directory whenever a file is created inside it, and a name without
        // an extension may be a directory, so on Windows these writes do trigger (silent) rebuilds.
        Assert.SkipWhen(OperatingSystem.IsWindows(), "Directory change events are reported on Windows.");

        _root.Write("CLAUDE.md", "# root\n");
        foreach (string directory in new[] { ".hidden", "plugins/cache", "projects/demo", "hooks" })
        {
            Directory.CreateDirectory(_root.Resolve(directory));
        }

        IndexHost host = await StartHostAsync();
        await WaitUntilAsync(() => BuildCount >= 2, "the catch-up rebuild after the start");
        int builds = BuildCount;

        _root.Write(".hidden/x.md", "# hidden\n");
        _root.Write("plugins/cache/x.md", "# runtime directory\n");
        _root.Write("projects/demo/transcript.jsonl", "{}");
        _root.Write("hooks/run.sh", "echo");
        _root.Write("notes.txt", "text");
        await Task.Delay(TimeSpan.FromMilliseconds(900), TestContext.Current.CancellationToken);

        BuildCount.ShouldBe(builds);
        host.Current.Version.ShouldBe(1);
    }

    [Fact]
    public async Task Watcher_NewProjectDirectory_IsARelevantChange()
    {
        Directory.CreateDirectory(_root.Resolve("projects"));
        IndexHost host = await StartHostAsync();
        await WaitUntilAsync(() => BuildCount >= 2, "the catch-up rebuild after the start");
        int builds = BuildCount;

        Directory.CreateDirectory(_root.Resolve("projects/demo"));
        await WaitUntilAsync(() => BuildCount > builds, "a rebuild after a project directory appeared");

        host.Current.Version.ShouldBe(1);
    }

    [Fact]
    public async Task Watcher_RelevantChangeAfterIgnoredOnes_IsStillPublished()
    {
        IndexHost host = await StartHostAsync();
        await using IAsyncEnumerator<IndexChange> changes = host.WatchAsync(TestContext.Current.CancellationToken).GetAsyncEnumerator(TestContext.Current.CancellationToken);

        _root.Write("notes.txt", "text");
        _root.Write("rules/new.md", "# new\n");
        IndexChange change = await NextAsync(changes);

        change.Added.ShouldBe(["rules/new.md"]);
    }

    [Fact]
    public async Task Watcher_RenamedFile_IsRemovedAndAddedInOneVersion()
    {
        _root.Write("rules/old-name.md", "# renamed\n");
        IndexHost host = await StartHostAsync();
        await using IAsyncEnumerator<IndexChange> changes = host.WatchAsync(TestContext.Current.CancellationToken).GetAsyncEnumerator(TestContext.Current.CancellationToken);

        File.Move(_root.Resolve("rules/old-name.md"), _root.Resolve("rules/new-name.md"));
        IndexChange change = await NextAsync(changes);

        change.Added.ShouldBe(["rules/new-name.md"]);
        change.Removed.ShouldBe(["rules/old-name.md"]);
    }

    [Fact]
    public async Task Watcher_RenamedToAnIgnoredName_IsStillNoticedThroughTheOldName()
    {
        _root.Write("rules/keep.md", "# kept\n");
        IndexHost host = await StartHostAsync();
        await using IAsyncEnumerator<IndexChange> changes = host.WatchAsync(TestContext.Current.CancellationToken).GetAsyncEnumerator(TestContext.Current.CancellationToken);

        File.Move(_root.Resolve("rules/keep.md"), _root.Resolve("rules/keep.md.bak"));
        IndexChange change = await NextAsync(changes);

        change.Removed.ShouldBe(["rules/keep.md"]);
    }

    [Fact]
    public async Task Watcher_DirectoryMovedIntoTheRoot_IsPickedUp()
    {
        using var elsewhere = new TempDirectory();
        elsewhere.Write("moved/inside.md", "# inside\n");
        IndexHost host = await StartHostAsync();
        await using IAsyncEnumerator<IndexChange> changes = host.WatchAsync(TestContext.Current.CancellationToken).GetAsyncEnumerator(TestContext.Current.CancellationToken);

        Directory.Move(elsewhere.Resolve("moved"), _root.Resolve("moved"));
        IndexChange change = await NextAsync(changes);

        change.Added.ShouldBe(["moved/inside.md"]);
    }

    [Fact]
    public async Task Watcher_ChangeInsideASymbolicallyLinkedDirectory_IsPublished()
    {
        using var elsewhere = new TempDirectory();
        elsewhere.Write("skill/SKILL.md", "---\nname: linked\n---\nbody\n");
        Directory.CreateDirectory(_root.Resolve("skills"));
        try
        {
            Directory.CreateSymbolicLink(_root.Resolve("skills/linked"), elsewhere.Resolve("skill"));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or PlatformNotSupportedException)
        {
            Assert.Skip($"Symbolic links are not available here: {exception.Message}");
        }

        IndexHost host = await StartHostAsync();
        await using IAsyncEnumerator<IndexChange> changes = host.WatchAsync(TestContext.Current.CancellationToken).GetAsyncEnumerator(TestContext.Current.CancellationToken);
        host.Current.Files.Keys.ShouldBe(["skills/linked/SKILL.md"]);

        elsewhere.Write("skill/SKILL.md", "---\nname: linked\n---\nbody, changed outside the root\n");
        IndexChange change = await NextAsync(changes);

        change.Changed.ShouldBe(["skills/linked/SKILL.md"]);
    }

    [Fact]
    public async Task Watcher_SettingsOutputStyleChanged_ChangesTheSelectedStyle()
    {
        _root.Write("settings.json", """{"outputStyle":"Terse"}""");
        _root.Write("output-styles/terse.md", "---\nname: Terse\n---\nBe terse.\n");
        _root.Write("output-styles/verbose.md", "---\nname: Verbose\n---\nBe verbose.\n");
        IndexHost host = await StartHostAsync();
        await using IAsyncEnumerator<IndexChange> changes = host.WatchAsync(TestContext.Current.CancellationToken).GetAsyncEnumerator(TestContext.Current.CancellationToken);
        host.Current.Files["output-styles/terse.md"].LoadMode.ShouldBe(LoadMode.EverySession);

        _root.Write("settings.json", """{"outputStyle":"Verbose"}""");
        IndexChange change = await NextAsync(changes);

        change.Changed.ShouldBe(["output-styles/terse.md", "output-styles/verbose.md"]);
        host.Current.OutputStyle.ShouldBe("Verbose");
        host.Current.Files["output-styles/verbose.md"].LoadMode.ShouldBe(LoadMode.EverySession);
        host.Current.Files["output-styles/terse.md"].LoadMode.ShouldBe(LoadMode.Inactive);
    }

    private int WatchingCount => _hostLog.Entries.Count(entry => entry.Message.StartsWith("Watching ", StringComparison.Ordinal));

    [Fact]
    public async Task Watcher_ErrorEvent_RestartsTheWatcherAndTheIndexKeepsFollowingTheFolder()
    {
        IndexHost host = await StartHostAsync();
        await using IAsyncEnumerator<IndexChange> changes = host.WatchAsync(TestContext.Current.CancellationToken).GetAsyncEnumerator(TestContext.Current.CancellationToken);
        WatchingCount.ShouldBe(1);

        // Events were lost while the watcher was broken: the file appears without any event announcing it.
        host.OnWatcherError(sender: null, new ErrorEventArgs(new InternalBufferOverflowException("simulated overflow")));
        await WaitUntilAsync(() => WatchingCount == 2, "the watcher to be started again");
        _root.Write("rules/after-restart.md", "# written after the restart\n");
        IndexChange change = await NextAsync(changes);

        _hostLog.Entries.ShouldContain(entry => entry.Level == LogLevel.Warning && entry.Message.Contains("simulated overflow", StringComparison.Ordinal));
        change.Added.ShouldBe(["rules/after-restart.md"]);
    }

    [Fact]
    public async Task Watcher_CannotBeRestarted_RetriesUntilTheFolderIsBack()
    {
        _root.Write("rules/old.md", "# old\n");
        IndexHost host = await StartHostAsync();
        await using IAsyncEnumerator<IndexChange> changes = host.WatchAsync(TestContext.Current.CancellationToken).GetAsyncEnumerator(TestContext.Current.CancellationToken);

        Directory.Delete(_root.Path, recursive: true);
        host.OnWatcherError(sender: null, new ErrorEventArgs(new IOException("simulated: the watcher is gone")));
        await WaitUntilAsync(() => _hostLog.Entries.Any(entry => entry.Level == LogLevel.Warning && entry.Message.StartsWith("Cannot watch", StringComparison.Ordinal)), "the failed restart to be logged");
        host.Current.Files.Keys.ShouldBe(["rules/old.md"]);

        _root.Write("rules/new.md", "# the folder is back\n");
        IndexChange change = await NextAsync(changes);

        change.Added.ShouldBe(["rules/new.md"]);
        change.Removed.ShouldBe(["rules/old.md"]);
        WatchingCount.ShouldBe(2);
    }

    [Fact]
    public async Task RefreshSafelyAsync_AfterTheHostStopped_IsSilent()
    {
        IndexHost host = await StartHostAsync();
        await host.StopAsync(TestContext.Current.CancellationToken);

        await host.RefreshSafelyAsync();

        _hostLog.Entries.ShouldNotContain(entry => entry.Level >= LogLevel.Warning);
    }

    [Fact]
    public async Task RefreshSafelyAsync_WhenTheRebuildFails_LogsAndKeepsThePreviousIndex()
    {
        _root.Write("rules/a.md", "# A\n");
        IndexHost host = await StartHostAsync();
        Directory.Delete(_root.Path, recursive: true);

        await host.RefreshSafelyAsync();

        _hostLog.Entries.ShouldContain(entry => entry.Level == LogLevel.Error && entry.Message.Contains("previous index stays", StringComparison.Ordinal));
        host.Current.Files.Keys.ShouldBe(["rules/a.md"]);
    }

    [Fact]
    public async Task Watcher_RootDeletedWhileRunning_KeepsTheLastIndexAndLogsTheFailure()
    {
        _root.Write("rules/a.md", "# A\n");
        IndexHost host = await StartHostAsync();

        Directory.Delete(_root.Path, recursive: true);
        await WaitUntilAsync(() => _hostLog.Entries.Any(entry => entry.Level >= LogLevel.Warning), "a warning or error about the deleted folder");
        _hostLog.Entries.ShouldContain(entry => entry.Level == LogLevel.Error && entry.Message.Contains("does not exist", StringComparison.Ordinal));

        host.Current.Version.ShouldBe(1);
        host.Current.Files.Keys.ShouldBe(["rules/a.md"]);
    }
}
