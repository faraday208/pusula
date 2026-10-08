using System.Net;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Pusula.IntegrationTests.Support;
using Pusula.Startup;
using Shouldly;
using Xunit;

namespace Pusula.IntegrationTests.Startup;

// The single-file download is started from any folder and has nothing next to it: outside Development the page and the
// default settings come from inside the program, whatever folder the content root is.
public sealed class BuiltInContentTests
{
    private static PusulaFactory StartedIn(TempDirectory temp, string folder) =>
        PusulaFactory.FromSourcesFile(
            temp.Resolve("data/pusula/sources.json"),
            new UserDirectories(temp.CreateDirectory("home"), temp.CreateDirectory("data")),
            configureHost: builder => builder.UseEnvironment("Production").UseContentRoot(temp.CreateDirectory(folder)));

    private static async Task<HttpResponseMessage> GetAsync(PusulaFactory factory, string url)
    {
        using HttpClient client = factory.CreateClient();
        return await client.GetAsync(url, TestContext.Current.CancellationToken);
    }

    [Theory]
    [InlineData("/")]
    [InlineData("/index.html")]
    public async Task Get_PageOutsideDevelopment_IsTheOneInsideTheProgram(string url)
    {
        using var temp = new TempDirectory();
        await using PusulaFactory factory = StartedIn(temp, "started-here");

        using HttpResponseMessage response = await GetAsync(factory, url);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken)).ShouldContain("<title>pusula</title>");
    }

    [Theory]
    [InlineData("/js/app.js", "text/javascript")]
    [InlineData("/app.css", "text/css")]
    [InlineData("/vendor/markdown-it/markdown-it.umd.min.js", "text/javascript")]
    public async Task Get_FileOfThePageOutsideDevelopment_IsServedWithItsContentType(string url, string contentType)
    {
        using var temp = new TempDirectory();
        await using PusulaFactory factory = StartedIn(temp, "started-here");

        using HttpResponseMessage response = await GetAsync(factory, url);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        response.Content.Headers.ContentType?.MediaType.ShouldBe(contentType);
    }

    // Started inside another web project, say: its wwwroot is not served as the page of pusula, nor next to it.
    [Fact]
    public async Task Get_OutsideDevelopment_NothingOfAWwwrootInTheFolderItWasStartedInIsServed()
    {
        using var temp = new TempDirectory();
        temp.Write("started-here/wwwroot/index.html", "<html><title>not pusula</title></html>");
        temp.Write("started-here/wwwroot/extra.txt", "x");
        await using PusulaFactory factory = StartedIn(temp, "started-here");

        using HttpResponseMessage page = await GetAsync(factory, "/");
        using HttpResponseMessage extra = await GetAsync(factory, "/extra.txt");

        (await page.Content.ReadAsStringAsync(TestContext.Current.CancellationToken)).ShouldContain("<title>pusula</title>");
        extra.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    // No appsettings.json next to the program: its settings are built in (the port of the README, quiet framework logs).
    [Fact]
    public async Task Settings_WithoutAnAppsettingsFile_AreTheBuiltInOnes()
    {
        using var temp = new TempDirectory();
        await using PusulaFactory factory = StartedIn(temp, "started-here");

        IConfiguration configuration = factory.Services.GetRequiredService<IConfiguration>();

        configuration["Urls"].ShouldBe("http://localhost:5190");
        configuration["Logging:LogLevel:Microsoft.AspNetCore"].ShouldBe("Warning");
    }
}
