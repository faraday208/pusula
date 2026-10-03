using System.Net;
using Pusula.IntegrationTests.Support;
using Shouldly;
using Xunit;

namespace Pusula.IntegrationTests.Security;

public sealed class SecurityHeadersTests(ApiFixture fixture) : IClassFixture<ApiFixture>
{
    private const string ExpectedPolicy =
        "default-src 'self'; img-src 'self' data:; style-src 'self'; script-src 'self'; connect-src 'self'; "
        + "object-src 'none'; base-uri 'none'; frame-ancestors 'none'; form-action 'none'";

    private static void ShouldCarrySecurityHeaders(HttpResponseMessage response, string what)
    {
        response.Headers.GetValues("Content-Security-Policy").ShouldBe([ExpectedPolicy], what);
        response.Headers.GetValues("X-Content-Type-Options").ShouldBe(["nosniff"], what);
        response.Headers.GetValues("Referrer-Policy").ShouldBe(["no-referrer"], what);
        response.Headers.GetValues("X-Frame-Options").ShouldBe(["DENY"], what);
    }

    [Theory]
    [InlineData("/api/sources/root/tree", HttpStatusCode.OK)]
    [InlineData("/api/sources/root/overview", HttpStatusCode.OK)]
    [InlineData("/api/sources/root/file?path=CLAUDE.md", HttpStatusCode.OK)]
    [InlineData("/api/sources/root/file", HttpStatusCode.BadRequest)]
    [InlineData("/api/sources/root/file?path=missing.md", HttpStatusCode.NotFound)]
    [InlineData("/api/sources/no-such-source/tree", HttpStatusCode.NotFound)]
    [InlineData("/api/sources", HttpStatusCode.OK)]
    [InlineData("/api/unknown", HttpStatusCode.NotFound)]
    [InlineData("/", HttpStatusCode.OK)]
    [InlineData("/index.html", HttpStatusCode.OK)]
    [InlineData("/js/app.js", HttpStatusCode.OK)]
    [InlineData("/not-a-file.txt", HttpStatusCode.NotFound)]
    [InlineData("/health", HttpStatusCode.OK)]
    [InlineData("/openapi/v1.json", HttpStatusCode.OK)]
    public async Task Response_Always_CarriesTheSecurityHeaders(string url, HttpStatusCode expectedStatus)
    {
        using HttpResponseMessage response = await fixture.Client.GetResponseAsync(url);

        response.StatusCode.ShouldBe(expectedStatus);
        ShouldCarrySecurityHeaders(response, url);
    }

    [Fact]
    public async Task Response_ToAWrongMethod_CarriesTheSecurityHeaders()
    {
        using var request = new HttpRequestMessage(HttpMethod.Delete, fixture.Api("tree"));

        using HttpResponseMessage response = await fixture.Client.SendAsync(request, TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.MethodNotAllowed);
        ShouldCarrySecurityHeaders(response, "DELETE tree");
    }

    [Fact]
    public async Task Response_RejectedForItsHost_CarriesTheSecurityHeaders()
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, fixture.Api("tree"));
        request.Headers.Host = "evil.example";

        using HttpResponseMessage response = await fixture.Client.SendAsync(request, TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        ShouldCarrySecurityHeaders(response, "rejected host");
    }

    [Fact]
    public async Task Response_ToACorsPreflight_AllowsNoOtherOrigin()
    {
        using var request = new HttpRequestMessage(HttpMethod.Options, fixture.Api("tree"));
        request.Headers.Add("Origin", "https://evil.example");
        request.Headers.Add("Access-Control-Request-Method", "GET");

        using HttpResponseMessage response = await fixture.Client.SendAsync(request, TestContext.Current.CancellationToken);

        response.Headers.Contains("Access-Control-Allow-Origin").ShouldBeFalse();
        response.Headers.Contains("Access-Control-Allow-Methods").ShouldBeFalse();
    }

    [Fact]
    public async Task Response_ToACrossOriginRequest_HasNoCorsHeaders()
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, fixture.Api("tree"));
        request.Headers.Add("Origin", "https://evil.example");

        using HttpResponseMessage response = await fixture.Client.SendAsync(request, TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        response.Headers.Contains("Access-Control-Allow-Origin").ShouldBeFalse();
    }
}
