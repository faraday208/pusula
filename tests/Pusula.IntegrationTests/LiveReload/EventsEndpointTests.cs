using System.Diagnostics;
using System.Net;
using System.Runtime.CompilerServices;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Pusula.Indexing;
using Pusula.IntegrationTests.Support;
using Pusula.Sources;
using Shouldly;
using Xunit;

namespace Pusula.IntegrationTests.LiveReload;

public sealed class EventsEndpointTests
{
    private static string[] Paths(JsonElement list) => [.. list.EnumerateArray().Select(path => path.GetString()!)];

    // Each test gets its own root: these tests change the folder.
    private static async Task<(PusulaFactory Factory, HttpClient Client)> StartAsync(SyntheticRoot root)
    {
        var factory = new PusulaFactory(root.Path);
        HttpClient client = factory.CreateClient();
        using HttpResponseMessage started = await client.GetResponseAsync("/health");
        return (factory, client);
    }

    [Fact]
    public async Task GetEvents_Connect_AnswersWithAnEventStreamThatStartsWithReady()
    {
        using var root = new SyntheticRoot();
        (PusulaFactory factory, HttpClient client) = await StartAsync(root);
        using (factory)
        using (client)
        await using (SseClient events = await SseClient.ConnectAsync(client, factory.Api("events")))
        {
            (string type, JsonDocument data) = await events.NextAsync();

            events.Response.StatusCode.ShouldBe(HttpStatusCode.OK);
            events.Response.Content.Headers.ContentType?.MediaType.ShouldBe("text/event-stream");
            events.Response.Headers.Contains("Content-Security-Policy").ShouldBeTrue();
            type.ShouldBe("ready");
            data.RootElement.PropertyNames().ShouldBe(["version", "added", "removed", "changed"], ignoreOrder: true);
            data.RootElement.GetProperty("version").GetInt64().ShouldBe(1);
            data.RootElement.GetProperty("added").GetArrayLength().ShouldBe(0);
            data.RootElement.GetProperty("removed").GetArrayLength().ShouldBe(0);
            data.RootElement.GetProperty("changed").GetArrayLength().ShouldBe(0);
        }
    }

    [Fact]
    public async Task GetEvents_FileEdited_SendsChangedWithThePathWithinFiveSeconds()
    {
        using var root = new SyntheticRoot();
        (PusulaFactory factory, HttpClient client) = await StartAsync(root);
        using (factory)
        using (client)
        await using (SseClient events = await SseClient.ConnectAsync(client, factory.Api("events")))
        {
            (await events.NextAsync()).Type.ShouldBe("ready");

            var clock = Stopwatch.StartNew();
            root.Write("rules/style.md", "---\ndescription: Style rule\n---\n# Style, edited\n");
            (string type, JsonDocument data) = await events.NextAsync(TimeSpan.FromSeconds(5));
            clock.Stop();

            type.ShouldBe("changed");
            data.RootElement.GetProperty("version").GetInt64().ShouldBe(2);
            Paths(data.RootElement.GetProperty("changed")).ShouldBe(["rules/style.md"]);
            Paths(data.RootElement.GetProperty("added")).ShouldBeEmpty();
            Paths(data.RootElement.GetProperty("removed")).ShouldBeEmpty();
            clock.Elapsed.ShouldBeLessThan(TimeSpan.FromSeconds(5));
        }
    }

    [Fact]
    public async Task GetEvents_FileCreatedAndThenDeleted_IsAddedAndThenRemoved()
    {
        using var root = new SyntheticRoot();
        (PusulaFactory factory, HttpClient client) = await StartAsync(root);
        using (factory)
        using (client)
        await using (SseClient events = await SseClient.ConnectAsync(client, factory.Api("events")))
        {
            (await events.NextAsync()).Type.ShouldBe("ready");

            string created = root.Write("rules/brand-new.md", "# New\n");
            (string addedType, JsonDocument added) = await events.NextAsync();
            File.Delete(created);
            (string removedType, JsonDocument removed) = await events.NextAsync();

            addedType.ShouldBe("changed");
            added.RootElement.GetProperty("version").GetInt64().ShouldBe(2);
            Paths(added.RootElement.GetProperty("added")).ShouldBe(["rules/brand-new.md"]);
            removedType.ShouldBe("changed");
            removed.RootElement.GetProperty("version").GetInt64().ShouldBe(3);
            Paths(removed.RootElement.GetProperty("removed")).ShouldBe(["rules/brand-new.md"]);
        }
    }

    [Fact]
    public async Task GetEvents_ChangesTheIndexIgnores_AreNotAnnounced()
    {
        using var root = new SyntheticRoot();
        (PusulaFactory factory, HttpClient client) = await StartAsync(root);
        using (factory)
        using (client)
        await using (SseClient events = await SseClient.ConnectAsync(client, factory.Api("events")))
        {
            (await events.NextAsync()).Type.ShouldBe("ready");

            root.Write(".hidden/more.md", "# hidden\n");
            root.Write("plugins/cache/more.md", "# runtime directory\n");
            root.Write("projects/demo/transcript2.jsonl", "{}");
            root.Write("notes.txt", "not markdown");
            root.Write("hooks/run.sh", "echo");
            await Task.Delay(TimeSpan.FromMilliseconds(900), TestContext.Current.CancellationToken);
            root.Write("rules/relevant.md", "# relevant\n");
            (string type, JsonDocument data) = await events.NextAsync();

            type.ShouldBe("changed");
            data.RootElement.GetProperty("version").GetInt64().ShouldBe(2);
            Paths(data.RootElement.GetProperty("added")).ShouldBe(["rules/relevant.md"]);
        }
    }

    [Fact]
    public async Task GetEvents_ChangeIsVisibleThroughTheOtherEndpointsAtTheAnnouncedVersion()
    {
        using var root = new SyntheticRoot();
        (PusulaFactory factory, HttpClient client) = await StartAsync(root);
        using (factory)
        using (client)
        await using (SseClient events = await SseClient.ConnectAsync(client, factory.Api("events")))
        {
            (await events.NextAsync()).Type.ShouldBe("ready");

            root.Write("shared/shared-note.md", "# Shared, rewritten\n");
            (_, JsonDocument data) = await events.NextAsync();
            long version = data.RootElement.GetProperty("version").GetInt64();

            using JsonDocument tree = await client.GetJsonAsync(factory.Api("tree"));
            using JsonDocument file = await client.GetJsonAsync(factory.Api("file?path=shared%2Fshared-note.md"));
            using JsonDocument overview = await client.GetJsonAsync(factory.Api("overview"));

            tree.RootElement.GetProperty("version").GetInt64().ShouldBe(version);
            overview.RootElement.GetProperty("version").GetInt64().ShouldBe(version);
            file.RootElement.GetProperty("version").GetInt64().ShouldBe(version);
            file.RootElement.GetProperty("body").GetString().ShouldBe("# Shared, rewritten\n");
        }
    }

    [Fact]
    public async Task GetEvents_TwoClients_BothReceiveEveryChange()
    {
        using var root = new SyntheticRoot();
        (PusulaFactory factory, HttpClient client) = await StartAsync(root);
        using (factory)
        using (client)
        await using (SseClient first = await SseClient.ConnectAsync(client, factory.Api("events")))
        await using (SseClient second = await SseClient.ConnectAsync(client, factory.Api("events")))
        {
            (await first.NextAsync()).Type.ShouldBe("ready");
            (await second.NextAsync()).Type.ShouldBe("ready");

            root.Write("rules/one.md", "# one\n");
            (_, JsonDocument firstOne) = await first.NextAsync();
            root.Write("rules/two.md", "# two\n");
            (_, JsonDocument firstTwo) = await first.NextAsync();

            // The second client did not read while the changes happened; it still gets both, in order.
            (_, JsonDocument secondOne) = await second.NextAsync();
            (_, JsonDocument secondTwo) = await second.NextAsync();

            Paths(firstOne.RootElement.GetProperty("added")).ShouldBe(["rules/one.md"]);
            Paths(firstTwo.RootElement.GetProperty("added")).ShouldBe(["rules/two.md"]);
            secondOne.RootElement.GetRawText().ShouldBe(firstOne.RootElement.GetRawText());
            secondTwo.RootElement.GetRawText().ShouldBe(firstTwo.RootElement.GetRawText());
        }
    }

    [Fact]
    public async Task GetEvents_ClientThatConnectsAfterAChange_StartsAtTheCurrentVersion()
    {
        using var root = new SyntheticRoot();
        (PusulaFactory factory, HttpClient client) = await StartAsync(root);
        using (factory)
        using (client)
        {
            await using (SseClient early = await SseClient.ConnectAsync(client, factory.Api("events")))
            {
                (await early.NextAsync()).Type.ShouldBe("ready");
                root.Write("rules/early.md", "# early\n");
                (await early.NextAsync()).Type.ShouldBe("changed");
            }

            await using SseClient late = await SseClient.ConnectAsync(client, factory.Api("events"));
            (string type, JsonDocument data) = await late.NextAsync();

            type.ShouldBe("ready");
            data.RootElement.GetProperty("version").GetInt64().ShouldBe(2);
        }
    }

    [Fact]
    public async Task GetEvents_ServerStopping_EndsTheStream()
    {
        using var root = new SyntheticRoot();
        (PusulaFactory factory, HttpClient client) = await StartAsync(root);
        using (client)
        await using (SseClient events = await SseClient.ConnectAsync(client, factory.Api("events")))
        {
            (await events.NextAsync()).Type.ShouldBe("ready");

            Task<(string Type, JsonDocument Data)?> next = events.TryNextAsync(TimeSpan.FromSeconds(20));
            Task stopped = factory.DisposeAsync().AsTask();

            (await next).ShouldBeNull();
            await stopped.WaitAsync(TimeSpan.FromSeconds(20), TestContext.Current.CancellationToken);
        }
    }

    [Fact]
    public async Task GetEvents_ClientDisconnects_EndsTheSubscription()
    {
        using var root = new SyntheticRoot();
        await using var factory = new PusulaFactory(root.Path);
        var counting = new CountingProvider();
        await using WebApplicationFactory<Program> counted = factory.WithWebHostBuilder(builder =>
            builder.ConfigureTestServices(services => services.AddSingleton<ISourceRegistry>(new SingleSourceRegistry("counted", counting))));
        using HttpClient client = counted.CreateClient();

        await using (SseClient events = await SseClient.ConnectAsync(client, "/api/sources/counted/events"))
        {
            (await events.NextAsync()).Type.ShouldBe("ready");
            await WaitForActiveAsync(counting, 1);
        }

        await WaitForActiveAsync(counting, 0);
    }

    private static async Task WaitForActiveAsync(CountingProvider provider, int expected)
    {
        DateTime deadline = DateTime.UtcNow + SseClient.DefaultTimeout;
        while (provider.Active != expected && DateTime.UtcNow < deadline)
        {
            await Task.Delay(20, TestContext.Current.CancellationToken);
        }

        provider.Active.ShouldBe(expected);
    }

    // A provider whose subscriptions can be counted; it never announces a change.
    private sealed class CountingProvider : IIndexProvider
    {
        private int _active;

        public int Active => Volatile.Read(ref _active);

        public ConfigIndex Current { get; } = new(
            "/root",
            DateTimeOffset.UnixEpoch,
            null,
            System.Collections.Immutable.ImmutableSortedDictionary<string, ConfigFile>.Empty,
            System.Collections.Immutable.ImmutableSortedDictionary<string, IReadOnlyList<Backlink>>.Empty,
            Version: 1);

        public IAsyncEnumerable<IndexChange> WatchAsync(CancellationToken cancellationToken) => Subscription(cancellationToken);

        private async IAsyncEnumerable<IndexChange> Subscription([EnumeratorCancellation] CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _active);
            try
            {
                await Task.Delay(Timeout.Infinite, cancellationToken);
                yield break;
            }
            finally
            {
                Interlocked.Decrement(ref _active);
            }
        }
    }
}
