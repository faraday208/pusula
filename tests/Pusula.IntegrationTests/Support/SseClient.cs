using System.Net.ServerSentEvents;
using System.Text.Json;
using Xunit;

namespace Pusula.IntegrationTests.Support;

/// <summary>Reads a server-sent event stream, one event at a time.</summary>
internal sealed class SseClient : IAsyncDisposable
{
    public static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(10);

    private readonly HttpResponseMessage _response;
    private readonly Stream _stream;
    private readonly IAsyncEnumerator<SseItem<string>> _events;

    private SseClient(HttpResponseMessage response, Stream stream)
    {
        _response = response;
        _stream = stream;
        _events = SseParser.Create(stream).EnumerateAsync(TestContext.Current.CancellationToken).GetAsyncEnumerator(TestContext.Current.CancellationToken);
    }

    public HttpResponseMessage Response => _response;

    public static async Task<SseClient> ConnectAsync(HttpClient client, string url)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Accept.ParseAdd("text/event-stream");
        HttpResponseMessage response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, TestContext.Current.CancellationToken);
        response.EnsureSuccessStatusCode();
        Stream stream = await response.Content.ReadAsStreamAsync(TestContext.Current.CancellationToken);
        return new SseClient(response, stream);
    }

    /// <summary>The next event: its type and its JSON data. Throws when none arrives within the timeout or the stream has ended.</summary>
    public async Task<(string Type, JsonDocument Data)> NextAsync(TimeSpan? timeout = null)
    {
        (string Type, JsonDocument Data)? next = await TryNextAsync(timeout);
        return next ?? throw new InvalidOperationException("The event stream ended.");
    }

    /// <summary>The next event, or null when the stream ended. Throws when no event arrives within the timeout.</summary>
    public async Task<(string Type, JsonDocument Data)?> TryNextAsync(TimeSpan? timeout = null)
    {
        if (!await _events.MoveNextAsync().AsTask().WaitAsync(timeout ?? DefaultTimeout, TestContext.Current.CancellationToken))
        {
            return null;
        }

        SseItem<string> item = _events.Current;
        return (item.EventType, JsonDocument.Parse(item.Data));
    }

    public async ValueTask DisposeAsync()
    {
        _response.Dispose();
        await _stream.DisposeAsync();
    }
}
