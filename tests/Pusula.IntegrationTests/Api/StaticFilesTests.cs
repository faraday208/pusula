using System.Net;
using Pusula.IntegrationTests.Support;
using Shouldly;
using Xunit;

namespace Pusula.IntegrationTests.Api;

public sealed class StaticFilesTests(ApiFixture fixture) : IClassFixture<ApiFixture>
{
    [Theory]
    [InlineData("/")]
    [InlineData("/index.html")]
    public async Task Get_RootAndIndexHtml_ReturnTheHtmlPage(string url)
    {
        using HttpResponseMessage response = await fixture.Client.GetResponseAsync(url);
        string html = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        response.Content.Headers.ContentType?.MediaType.ShouldBe("text/html");
        html.ShouldContain("<html");
        html.ShouldContain("</html>");
    }

    [Theory]
    [InlineData("/")]
    [InlineData("/index.html")]
    [InlineData("/js/app.js")]
    [InlineData("/app.css")]
    [InlineData("/favicon.svg")]
    public async Task Get_StaticFile_IsRevalidatedOnEveryUse(string url)
    {
        using HttpResponseMessage response = await fixture.Client.GetResponseAsync(url);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        response.Headers.CacheControl.ShouldNotBeNull();
        response.Headers.CacheControl.NoCache.ShouldBeTrue();
    }

    [Theory]
    [InlineData("/js/app.js", "text/javascript")]
    [InlineData("/app.css", "text/css")]
    [InlineData("/favicon.svg", "image/svg+xml")]
    public async Task Get_StaticFile_HasItsContentType(string url, string contentType)
    {
        using HttpResponseMessage response = await fixture.Client.GetResponseAsync(url);

        response.Content.Headers.ContentType?.MediaType.ShouldBe(contentType);
    }

    [Fact]
    public async Task Get_UnchangedStaticFileWithItsEntityTag_Returns304()
    {
        using HttpResponseMessage first = await fixture.Client.GetResponseAsync("/app.css");
        using var request = new HttpRequestMessage(HttpMethod.Get, "/app.css");
        request.Headers.IfNoneMatch.Add(first.Headers.ETag!);

        using HttpResponseMessage second = await fixture.Client.SendAsync(request, TestContext.Current.CancellationToken);

        second.StatusCode.ShouldBe(HttpStatusCode.NotModified);
    }

    [Fact]
    public async Task Get_UnknownFile_Returns404WithProblemDetails()
    {
        using HttpResponseMessage response = await fixture.Client.GetResponseAsync("/does-not-exist.js");

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        response.Content.Headers.ContentType?.MediaType.ShouldBe("application/problem+json");
    }

    [Fact]
    public async Task Get_Health_ReturnsHealthy()
    {
        using HttpResponseMessage response = await fixture.Client.GetResponseAsync("/health");

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken)).ShouldBe("Healthy");
    }
}
