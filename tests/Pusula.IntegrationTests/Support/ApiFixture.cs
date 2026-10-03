using Xunit;

namespace Pusula.IntegrationTests.Support;

/// <summary>
/// One application instance over one <see cref="SyntheticRoot"/>, shared by the tests of a class. Tests that use it
/// only read; tests that change the folder create their own.
/// </summary>
public sealed class ApiFixture : IAsyncLifetime
{
    private PusulaFactory? _factory;

    internal SyntheticRoot Root { get; } = new();

    public HttpClient Client { get; private set; } = null!;

    /// <summary>The URL of an endpoint of the synthetic root's source: <c>Api("tree")</c> is <c>/api/sources/root/tree</c>.</summary>
    /// <param name="endpoint">What comes after the id, query string included.</param>
    public string Api(string endpoint) => (_factory ?? throw new InvalidOperationException("The fixture is not initialized.")).Api(endpoint);

    public ValueTask InitializeAsync()
    {
        _factory = new PusulaFactory(Root.Path);
        Client = _factory.CreateClient();
        return ValueTask.CompletedTask;
    }

    public async ValueTask DisposeAsync()
    {
        Client?.Dispose();
        if (_factory is not null)
        {
            await _factory.DisposeAsync();
        }

        Root.Dispose();
    }
}
