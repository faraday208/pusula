using System.Net;
using System.Text.Json;
using Pusula.Startup;

namespace Pusula.Updates;

/// <summary>What one request for the latest release came to: the tag, or why there is none.</summary>
/// <param name="Tag">The tag of the latest release; null when the request failed.</param>
/// <param name="Problem">Why it failed, in a few words; null when it did not.</param>
internal readonly record struct LookupResult(ReleaseTag? Tag, string? Problem);

/// <summary>
/// The one request pusula makes to the internet: <c>GET /repos/faraday208/pusula/releases/latest</c> of GitHub, which leaves out drafts and
/// pre-releases. Nothing is sent but the request itself (no cookie, no folder, no setting), and only <c>tag_name</c> is read from the answer.
/// </summary>
internal static class ReleaseLookup
{
    /// <summary>The name of the <see cref="HttpClient"/> that makes the request (a test gives it a handler of its own).</summary>
    internal const string ClientName = "pusula-updates";

    /// <summary>The address that is asked.</summary>
    internal const string Address = "https://api.github.com/repos/faraday208/pusula/releases/latest";

    /// <summary>How long the whole request has to be answered.</summary>
    internal static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);

    /// <summary>How much of the answer is read: the answer of GitHub is a few kilobytes, whatever else answers may send more.</summary>
    internal const int MaxBytes = 1024 * 1024;

    /// <summary>Asks for the latest release and reads its tag. Any failure (no network, a timeout, another status, a body that is too long or no JSON, a tag that is not a version) is a result with a problem, not an exception.</summary>
    /// <param name="client">Sends the request; it should not time out by itself (<paramref name="timeout"/> does).</param>
    /// <param name="timeout">The time the whole request has.</param>
    /// <param name="cancellationToken">Ends the request when pusula stops: the only case in which this throws.</param>
    public static async Task<LookupResult> LatestTagAsync(HttpClient client, TimeSpan timeout, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(client);

        using var request = new HttpRequestMessage(HttpMethod.Get, Address);
        request.Headers.UserAgent.TryParseAdd($"pusula/{AppVersion.Current}");
        request.Headers.Accept.ParseAdd("application/vnd.github+json");
        request.Headers.Add("X-GitHub-Api-Version", "2022-11-28");

        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(timeout);
        try
        {
            using HttpResponseMessage response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, deadline.Token).ConfigureAwait(false);
            if (response.StatusCode != HttpStatusCode.OK)
            {
                return new LookupResult(null, $"GitHub answered {(int)response.StatusCode}");
            }

            // At most MaxBytes, within the same time.
            await using Stream body = await response.Content.ReadAsStreamAsync(deadline.Token).ConfigureAwait(false);
            byte[] buffer = new byte[MaxBytes + 1];
            int read = 0;
            int count;
            while (read < buffer.Length && (count = await body.ReadAsync(buffer.AsMemory(read), deadline.Token).ConfigureAwait(false)) > 0)
            {
                read += count;
            }

            return read > MaxBytes ? new LookupResult(null, "the answer is longer than 1 MiB") : ReadTag(buffer.AsMemory(0, read));
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return new LookupResult(null, $"no answer within {timeout.TotalSeconds:0} seconds");
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            // Whatever goes wrong on the way (DNS, TLS, a proxy, a closed connection) is one thing for the person: no news this time.
            return new LookupResult(null, exception is HttpRequestException ? exception.Message : $"{exception.GetType().Name}: {exception.Message}");
        }
    }

    /// <summary>Takes <c>tag_name</c> out of the body of the answer; every other member is ignored, <c>html_url</c> too.</summary>
    internal static LookupResult ReadTag(ReadOnlyMemory<byte> body)
    {
        try
        {
            using JsonDocument json = JsonDocument.Parse(body);
            if (json.RootElement.ValueKind != JsonValueKind.Object
                || !json.RootElement.TryGetProperty("tag_name", out JsonElement name) || name.ValueKind != JsonValueKind.String)
            {
                return new LookupResult(null, "the answer has no tag_name");
            }

            return ReleaseTag.TryParse(name.GetString(), out ReleaseTag? tag)
                ? new LookupResult(tag, null)
                : new LookupResult(null, "the tag_name is not a version");
        }
        catch (JsonException)
        {
            return new LookupResult(null, "the answer is not JSON");
        }
    }
}
