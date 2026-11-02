using System.Net;

namespace Pusula.IntegrationTests.Support;

/// <summary>
/// The stand-in for api.github.com: answers from a function, counts the requests and remembers them. A test that does not give one
/// gets the real handler, which is why <see cref="PusulaFactory"/> turns the check off.
/// </summary>
internal sealed class ReleaseHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> answer) : HttpMessageHandler
{
    private int _count;

    /// <summary>How many requests were sent.</summary>
    public int Count => Volatile.Read(ref _count);

    /// <summary>The address of the last request.</summary>
    public Uri? LastAddress { get; private set; }

    /// <summary>Answers with a release that has this tag (and a page of its own that must never be used).</summary>
    /// <param name="tag">The value of <c>tag_name</c>.</param>
    public static ReleaseHandler Release(string tag) => new((_, _) => Task.FromResult(Reply(HttpStatusCode.OK, $$"""{"tag_name":"{{tag}}","html_url":"https://example.invalid/not-this-page"}""")));

    /// <summary>Answers with a status and a JSON body.</summary>
    public static HttpResponseMessage Reply(HttpStatusCode status, string body) =>
        new(status) { Content = new StringContent(body, System.Text.Encoding.UTF8, "application/json") };

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        Interlocked.Increment(ref _count);
        LastAddress = request.RequestUri;
        return answer(request, cancellationToken);
    }
}
