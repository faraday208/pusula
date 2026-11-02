using System.Net;

namespace Pusula.UnitTests.Support;

/// <summary>An <see cref="HttpMessageHandler"/> that answers from a function and counts the requests it got: no test reaches the network.</summary>
internal sealed class StubHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> answer) : HttpMessageHandler
{
    private int _count;

    /// <summary>How many requests were sent.</summary>
    public int Count => Volatile.Read(ref _count);

    /// <summary>The last request, as it was sent.</summary>
    public HttpRequestMessage? Last { get; private set; }

    /// <summary>A handler that answers 200 with a JSON body.</summary>
    /// <param name="json">The body.</param>
    public static StubHandler Json(string json) => new((_, _) => Task.FromResult(Reply(HttpStatusCode.OK, json)));

    /// <summary>A handler that answers with a status.</summary>
    /// <param name="status">The status.</param>
    /// <param name="body">The body.</param>
    public static StubHandler Status(HttpStatusCode status, string body = "{}") => new((_, _) => Task.FromResult(Reply(status, body)));

    /// <summary>The answer of GitHub for a release with a tag.</summary>
    /// <param name="tag">The value of <c>tag_name</c>.</param>
    public static string Release(string tag) => $$"""{"tag_name":"{{tag}}","html_url":"https://github.com/faraday208/pusula/releases/tag/{{tag}}","draft":false,"prerelease":false}""";

    /// <summary>An answer with a JSON body.</summary>
    public static HttpResponseMessage Reply(HttpStatusCode status, string body) =>
        new(status) { Content = new StringContent(body, System.Text.Encoding.UTF8, "application/json") };

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        Interlocked.Increment(ref _count);
        Last = request;
        return answer(request, cancellationToken);
    }
}
