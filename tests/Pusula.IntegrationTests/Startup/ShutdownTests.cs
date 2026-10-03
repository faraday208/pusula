using Pusula.IntegrationTests.Support;
using Shouldly;
using Xunit;

namespace Pusula.IntegrationTests.Startup;

/// <summary>The whole application taken down: the host stops it and disposes it, and the two overlap.</summary>
public sealed class ShutdownTests
{
    // Disposing the factory stops the host on the thread of the test; the entry point, which was woken up by that, disposes
    // the host on a thread of its own at the same time. Every test that starts the application depends on this not failing.
    [Fact]
    public async Task Factory_StartedAndDisposedAgainAndAgain_NeverFailsToTakeTheApplicationDown()
    {
        using var folder = new TempDirectory();
        folder.Write("CLAUDE.md", "# Hello\n");

        for (int round = 0; round < 40; round++)
        {
            await using var factory = new PusulaFactory(folder.Path);
            using HttpClient client = factory.CreateClient();
            using HttpResponseMessage response = await client.GetAsync(factory.Api("tree"), TestContext.Current.CancellationToken);

            response.EnsureSuccessStatusCode();
        }
    }
}
