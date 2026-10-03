using System.Net.ServerSentEvents;
using System.Runtime.CompilerServices;
using Pusula.Indexing;
using Pusula.LiveReload;
using Pusula.UnitTests.Support;
using Shouldly;
using Xunit;

namespace Pusula.UnitTests.LiveReload;

public sealed class EventsStreamTests
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);

    private static IndexChange Change(long version, string[]? added = null, string[]? removed = null, string[]? changed = null) =>
        new(version, added ?? [], removed ?? [], changed ?? []);

    private static async Task<List<SseItem<IndexEvent>>> ReadAllAsync(IAsyncEnumerable<SseItem<IndexEvent>> stream)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        timeout.CancelAfter(Timeout);

        var items = new List<SseItem<IndexEvent>>();
        await foreach (SseItem<IndexEvent> item in stream.WithCancellation(timeout.Token))
        {
            items.Add(item);
        }

        return items;
    }

    [Fact]
    public async Task StreamAsync_SendsReadyWithTheCurrentVersionFirstThenOneChangedEventPerChange()
    {
        var provider = new ScriptedProvider(version: 4, Change(5, added: ["a.md"]), Change(6, removed: ["b.md"], changed: ["c.md"]));

        List<SseItem<IndexEvent>> items = await ReadAllAsync(EventsEndpoints.StreamAsync(provider, CancellationToken.None));

        items.Select(item => item.EventType).ShouldBe(["ready", "changed", "changed"]);
        items[0].Data.ShouldBe(new IndexEvent(4, [], [], []), IndexEventComparer.Instance);
        items[1].Data.ShouldBe(new IndexEvent(5, ["a.md"], [], []), IndexEventComparer.Instance);
        items[2].Data.ShouldBe(new IndexEvent(6, [], ["b.md"], ["c.md"]), IndexEventComparer.Instance);
    }

    [Fact]
    public async Task StreamAsync_SubscribesBeforeItReadsTheVersion()
    {
        var provider = new ScriptedProvider(version: 1);

        await ReadAllAsync(EventsEndpoints.StreamAsync(provider, CancellationToken.None));

        provider.Calls.ShouldBe(["WatchAsync", "Current"]);
    }

    [Fact]
    public async Task StreamAsync_ChangesAlreadyReportedByTheReadyVersion_AreNotRepeated()
    {
        // A change that was published between subscribing and reading the version shows up in both places.
        var provider = new ScriptedProvider(version: 3, Change(2, changed: ["old.md"]), Change(3, changed: ["seen.md"]), Change(4, changed: ["new.md"]));

        List<SseItem<IndexEvent>> items = await ReadAllAsync(EventsEndpoints.StreamAsync(provider, CancellationToken.None));

        items.Select(item => item.EventType).ShouldBe(["ready", "changed"]);
        items[0].Data.Version.ShouldBe(3);
        items[1].Data.Version.ShouldBe(4);
        items[1].Data.Changed.ShouldBe(["new.md"]);
    }

    [Fact]
    public async Task StreamAsync_ApplicationStopping_EndsTheStream()
    {
        using var stopping = new CancellationTokenSource();
        var provider = new ScriptedProvider(version: 1, waitForCancellation: true);

        await using IAsyncEnumerator<SseItem<IndexEvent>> stream = EventsEndpoints.StreamAsync(provider, stopping.Token).GetAsyncEnumerator(TestContext.Current.CancellationToken);
        (await stream.MoveNextAsync().AsTask().WaitAsync(Timeout, TestContext.Current.CancellationToken)).ShouldBeTrue();
        stream.Current.EventType.ShouldBe("ready");

        Task<bool> next = stream.MoveNextAsync().AsTask();
        await stopping.CancelAsync();

        (await next.WaitAsync(Timeout, TestContext.Current.CancellationToken)).ShouldBeFalse();
    }

    [Fact]
    public async Task StreamAsync_RequestAborted_EndsTheStream()
    {
        using var requestAborted = new CancellationTokenSource();
        var provider = new ScriptedProvider(version: 1, waitForCancellation: true);

        await using IAsyncEnumerator<SseItem<IndexEvent>> stream =
            EventsEndpoints.StreamAsync(provider, CancellationToken.None).GetAsyncEnumerator(requestAborted.Token);
        (await stream.MoveNextAsync().AsTask().WaitAsync(Timeout, TestContext.Current.CancellationToken)).ShouldBeTrue();

        Task<bool> next = stream.MoveNextAsync().AsTask();
        await requestAborted.CancelAsync();

        (await next.WaitAsync(Timeout, TestContext.Current.CancellationToken)).ShouldBeFalse();
    }

    // Serves a fixed version and a scripted list of changes; optionally keeps the sequence open until it is cancelled.
    private sealed class ScriptedProvider(long version, params IndexChange[] changes) : IIndexProvider
    {
        private readonly bool _waitForCancellation;
        private readonly ConfigIndex _index = IndexFactory.Index([], version: version);

        public ScriptedProvider(long version, bool waitForCancellation)
            : this(version)
        {
            _waitForCancellation = waitForCancellation;
        }

        public List<string> Calls { get; } = [];

        public ConfigIndex Current
        {
            get
            {
                Calls.Add("Current");
                return _index;
            }
        }

        public IAsyncEnumerable<IndexChange> WatchAsync(CancellationToken cancellationToken)
        {
            Calls.Add("WatchAsync");
            return Changes(cancellationToken);
        }

        private async IAsyncEnumerable<IndexChange> Changes([EnumeratorCancellation] CancellationToken cancellationToken)
        {
            foreach (IndexChange change in changes)
            {
                await Task.Yield();
                yield return change;
            }

            if (_waitForCancellation)
            {
                try
                {
                    await Task.Delay(System.Threading.Timeout.Infinite, cancellationToken);
                }
                catch (OperationCanceledException)
                {
                    yield break;
                }
            }
        }
    }

    private sealed class IndexEventComparer : IEqualityComparer<IndexEvent>
    {
        public static IndexEventComparer Instance { get; } = new();

        public bool Equals(IndexEvent? x, IndexEvent? y) =>
            x is not null
            && y is not null
            && x.Version == y.Version
            && x.Added.SequenceEqual(y.Added)
            && x.Removed.SequenceEqual(y.Removed)
            && x.Changed.SequenceEqual(y.Changed);

        public int GetHashCode(IndexEvent obj) => obj.Version.GetHashCode();
    }
}
