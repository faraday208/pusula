using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Pusula.IntegrationTests.Support;
using Pusula.Indexing;
using Pusula.Sources;
using Shouldly;
using Xunit;

namespace Pusula.IntegrationTests.Api;

public sealed class ErrorHandlingTests
{
    private const string InternalDetail = "internal-detail-that-must-not-leak";

    [Theory]
    [InlineData("Development")]
    [InlineData("Production")]
    public async Task Get_EndpointThatThrows_Answers500WithProblemDetailsAndNoInternals(string environment)
    {
        using var root = new SyntheticRoot();
        await using var factory = new PusulaFactory(root.Path, environment: environment);
        await using WebApplicationFactory<Program> broken = factory.WithWebHostBuilder(builder =>
            builder.ConfigureTestServices(services => services.AddSingleton<ISourceRegistry>(new SingleSourceRegistry(SyntheticRoot.SourceId, new ThrowingProvider()))));
        using HttpClient client = broken.CreateClient();

        using HttpResponseMessage response = await client.GetResponseAsync(SyntheticRoot.Api("tree"));
        string body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        using JsonDocument json = JsonDocument.Parse(body);

        response.StatusCode.ShouldBe(HttpStatusCode.InternalServerError);
        response.Content.Headers.ContentType?.MediaType.ShouldBe("application/problem+json");
        json.RootElement.GetProperty("status").GetInt32().ShouldBe(500);
        body.ShouldNotContain(InternalDetail);
        body.ShouldNotContain("at Pusula.");
        response.Headers.GetValues("X-Frame-Options").ShouldBe(["DENY"]);
        response.Headers.GetValues("Content-Security-Policy").Single().ShouldContain("default-src 'self'");
    }

    private sealed class ThrowingProvider : IIndexProvider
    {
        public ConfigIndex Current => throw new InvalidOperationException(InternalDetail);

        public IAsyncEnumerable<IndexChange> WatchAsync(CancellationToken cancellationToken) =>
            throw new InvalidOperationException(InternalDetail);
    }
}
