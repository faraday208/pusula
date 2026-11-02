namespace Pusula.UnitTests.Support;

/// <summary>An <see cref="IHttpClientFactory"/> whose clients all send through one handler.</summary>
internal sealed class FakeHttpClientFactory(HttpMessageHandler handler) : IHttpClientFactory
{
    /// <summary>The names that clients were asked for.</summary>
    public List<string> Names { get; } = [];

    public HttpClient CreateClient(string name)
    {
        Names.Add(name);
        return new HttpClient(handler, disposeHandler: false);
    }
}
