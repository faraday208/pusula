using System.Net.ServerSentEvents;
using System.Runtime.CompilerServices;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Http.HttpResults;
using Pusula.Indexing;

namespace Pusula.LiveReload;

/// <summary>The <c>/api/sources/{source}/events</c> endpoint: a server-sent event stream that announces changes to the index of a source.</summary>
internal static class EventsEndpoints
{
    internal const string ReadyEventType = "ready";
    internal const string ChangedEventType = "changed";

    /// <summary>Maps <c>GET events</c> onto the route group of a source (see <c>SourceEndpoints.MapSources</c>).</summary>
    /// <param name="routes">The route group <c>/api/sources/{source}</c>.</param>
    public static IEndpointRouteBuilder MapEvents(this IEndpointRouteBuilder routes)
    {
        routes.MapGet("/events", (HttpContext http, IHostApplicationLifetime lifetime) => GetEvents(http.Features.GetRequiredFeature<IIndexProvider>(), lifetime))
            .WithName("GetEvents")
            .WithSummary("Stream index changes")
            .WithDescription(
                "A server-sent event stream (text/event-stream) about one source. The first event, 'ready', carries the "
                + "current index version; every later 'changed' event carries the new version and the paths that were "
                + "added, removed or changed. The data of each event is an IndexEvent object as JSON. The stream ends "
                + "when the client disconnects or the server stops.")
            .WithTags("Events");
        return routes;
    }

    /// <summary>
    /// The events of one client: <c>ready</c> with the current version first, then one <c>changed</c> event per new
    /// version. Ends when <paramref name="requestAborted"/> or <paramref name="stopping"/> is cancelled.
    /// </summary>
    /// <param name="provider">The index.</param>
    /// <param name="stopping">Cancelled when the application is stopping.</param>
    /// <param name="requestAborted">Cancelled when the client disconnects.</param>
    internal static async IAsyncEnumerable<SseItem<IndexEvent>> StreamAsync(
        IIndexProvider provider,
        CancellationToken stopping,
        [EnumeratorCancellation] CancellationToken requestAborted = default)
    {
        using CancellationTokenSource linked = CancellationTokenSource.CreateLinkedTokenSource(requestAborted, stopping);

        // Subscribe first, then read the version: a change published in between is delivered by the subscription
        // (and skipped below when 'ready' already reports it) instead of being lost.
        IAsyncEnumerable<IndexChange> changes = provider.WatchAsync(linked.Token);
        long readyVersion = provider.Current.Version;
        yield return new SseItem<IndexEvent>(new IndexEvent(readyVersion, [], [], []), ReadyEventType);

        await foreach (IndexChange change in changes.WithCancellation(linked.Token).ConfigureAwait(false))
        {
            if (change.Version > readyVersion)
            {
                yield return new SseItem<IndexEvent>(new IndexEvent(change.Version, change.Added, change.Removed, change.Changed), ChangedEventType);
            }
        }
    }

    private static ServerSentEventsResult<IndexEvent> GetEvents(IIndexProvider provider, IHostApplicationLifetime lifetime) =>
        TypedResults.ServerSentEvents(StreamAsync(provider, lifetime.ApplicationStopping));
}
