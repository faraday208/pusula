using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Pusula.IntegrationTests.Support;
using Shouldly;
using Xunit;

namespace Pusula.IntegrationTests.Security;

public sealed class HostGuardEndpointTests(ApiFixture fixture) : IClassFixture<ApiFixture>
{
    private static async Task<HttpResponseMessage> GetWithHostAsync(HttpClient client, string url, string host)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Host = host;
        return await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, TestContext.Current.CancellationToken);
    }

    [Theory]
    [InlineData("evil.example")]
    [InlineData("evil.example:5190")]
    [InlineData("localhost.evil.example")]
    [InlineData("127.0.0.1.evil.example")]
    [InlineData("rebind.attacker.test:80")]
    [InlineData("0x7f.1")]
    public async Task Request_WithAForeignHost_Returns400OnEveryEndpoint(string host)
    {
        foreach (string url in new[] { "/api/sources", fixture.Api("tree"), fixture.Api("overview"), fixture.Api("file?path=CLAUDE.md"), fixture.Api("events"), "/", "/index.html", "/health", "/openapi/v1.json" })
        {
            using HttpResponseMessage response = await GetWithHostAsync(fixture.Client, url, host);

            response.StatusCode.ShouldBe(HttpStatusCode.BadRequest, $"{host} {url}");
        }
    }

    [Fact]
    public async Task Request_WithAForeignHost_GetsProblemDetailsThatDoNotLeakAnything()
    {
        using HttpResponseMessage response = await GetWithHostAsync(fixture.Client, fixture.Api("file?path=CLAUDE.md"), "evil.example");
        string body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        using JsonDocument json = JsonDocument.Parse(body);

        response.Content.Headers.ContentType?.MediaType.ShouldBe("application/problem+json");
        json.RootElement.GetProperty("status").GetInt32().ShouldBe(400);
        json.RootElement.GetProperty("title").GetString().ShouldBe("Invalid Host header");
        body.ShouldNotContain("Synthetic config");
        body.ShouldNotContain("evil.example");
    }

    [Theory]
    [InlineData("localhost")]
    [InlineData("localhost:5190")]
    [InlineData("LOCALHOST")]
    [InlineData("app.localhost:5190")]
    [InlineData("127.0.0.1")]
    [InlineData("127.0.0.1:5190")]
    [InlineData("192.168.0.10:5190")]
    [InlineData("[::1]")]
    [InlineData("[::1]:5190")]
    [InlineData("node.tailnet-example.ts.net")]
    public async Task Request_WithLocalhostAnIpAddressOrATailnetName_Returns200(string host)
    {
        foreach (string url in new[] { "/api/sources", fixture.Api("tree"), fixture.Api("overview"), fixture.Api("file?path=CLAUDE.md"), "/", "/health", "/openapi/v1.json" })
        {
            using HttpResponseMessage response = await GetWithHostAsync(fixture.Client, url, host);

            response.StatusCode.ShouldBe(HttpStatusCode.OK, $"{host} {url}");
        }
    }

    [Fact]
    public async Task Request_WithTheNameOfThisMachine_Returns200()
    {
        string machine = System.Net.Dns.GetHostName();

        using HttpResponseMessage response = await GetWithHostAsync(fixture.Client, fixture.Api("tree"), machine.ToUpperInvariant() + ":5190");

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public async Task Request_WithoutAHost_Returns400(string? host)
    {
        await using var factory = new PusulaFactory(fixture.Root.Path);

        // The in-memory client always fills the Host header from the address, so the request is built by hand.
        HttpContext context = await factory.Server.SendAsync(
            http =>
            {
                http.Request.Method = HttpMethods.Get;
                http.Request.Path = fixture.Api("tree");
                if (host is null)
                {
                    http.Request.Headers.Remove("Host");
                }
                else
                {
                    http.Request.Headers.Host = host;
                }
            },
            TestContext.Current.CancellationToken);

        context.Response.StatusCode.ShouldBe(StatusCodes.Status400BadRequest);
        context.Response.Headers.ContainsKey("Content-Security-Policy").ShouldBeTrue();
    }

    [Fact]
    public async Task Request_WithAHostFromTheAllowedList_Returns200AndOthersStayRejected()
    {
        await using var factory = new PusulaFactory(fixture.Root.Path, allowedHosts: "pusula.example.com; Other.Example ;");
        using HttpClient client = factory.CreateClient();

        using HttpResponseMessage first = await GetWithHostAsync(client, fixture.Api("tree"), "pusula.example.com");
        using HttpResponseMessage second = await GetWithHostAsync(client, fixture.Api("tree"), "OTHER.example:8443");
        using HttpResponseMessage evil = await GetWithHostAsync(client, fixture.Api("tree"), "evil.example");
        using HttpResponseMessage suffix = await GetWithHostAsync(client, fixture.Api("tree"), "pusula.example.com.evil.example");

        first.StatusCode.ShouldBe(HttpStatusCode.OK);
        second.StatusCode.ShouldBe(HttpStatusCode.OK);
        evil.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        suffix.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Request_WithAHostThatIsNotAllowedByDefault_IsRejectedUntilItIsConfigured()
    {
        using HttpResponseMessage before = await GetWithHostAsync(fixture.Client, fixture.Api("tree"), "pusula.example.com");

        before.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }
}
