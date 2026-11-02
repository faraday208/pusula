using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Pusula.IntegrationTests.Support;
using Pusula.Startup;
using Pusula.Updates;
using Shouldly;
using Xunit;

namespace Pusula.IntegrationTests.Api;

public sealed class UpdateEndpointTests
{
    // The factory with the check turned on and the handler in place of api.github.com.
    private static WebApplicationFactory<Program> WithCheck(PusulaFactory factory, ReleaseHandler handler) =>
        factory.WithWebHostBuilder(builder =>
        {
            builder.UseSetting(PusulaOptions.CheckForUpdatesKey, "true");
            builder.ConfigureTestServices(services => services.AddHttpClient(ReleaseLookup.ClientName).ConfigurePrimaryHttpMessageHandler(() => handler));
        });

    private static async Task<JsonDocument> UntilAsync(HttpClient client, Func<string, bool> done)
    {
        DateTime end = DateTime.UtcNow.AddSeconds(10);
        while (true)
        {
            JsonDocument json = await client.GetJsonAsync("/api/update");
            if (done(json.RootElement.GetProperty("state").GetString()!) || DateTime.UtcNow > end)
            {
                return json;
            }

            json.Dispose();
            await Task.Delay(10, TestContext.Current.CancellationToken);
        }
    }

    [Fact]
    public async Task GetUpdate_NewerReleaseOut_SaysNewerWithTheVersionAndAPageMadeFromTheTag()
    {
        using var root = new SyntheticRoot();
        await using var factory = new PusulaFactory(root.Path);
        var handler = ReleaseHandler.Release("v99.0.0");
        await using WebApplicationFactory<Program> app = WithCheck(factory, handler);
        using HttpClient client = app.CreateClient();

        using JsonDocument json = await UntilAsync(client, state => state != "NotChecked");
        JsonElement answer = json.RootElement;

        answer.GetProperty("state").GetString().ShouldBe("Newer");
        answer.GetProperty("current").GetString().ShouldBe(AppVersion.Current);
        answer.GetProperty("latest").GetString().ShouldBe("99.0.0");
        answer.GetProperty("url").GetString().ShouldBe("https://github.com/faraday208/pusula/releases/tag/v99.0.0");
        answer.GetProperty("checkedAt").GetDateTimeOffset().ShouldBeGreaterThan(DateTimeOffset.UtcNow.AddMinutes(-5));
        handler.LastAddress!.ToString().ShouldBe("https://api.github.com/repos/faraday208/pusula/releases/latest");
        handler.Count.ShouldBe(1);
    }

    [Fact]
    public async Task GetUpdate_TheLatestReleaseIsOlder_SaysUpToDate()
    {
        using var root = new SyntheticRoot();
        await using var factory = new PusulaFactory(root.Path);
        await using WebApplicationFactory<Program> app = WithCheck(factory, ReleaseHandler.Release("v0.0.1"));
        using HttpClient client = app.CreateClient();

        using JsonDocument json = await UntilAsync(client, state => state != "NotChecked");

        json.RootElement.GetProperty("state").GetString().ShouldBe("UpToDate");
        json.RootElement.GetProperty("latest").GetString().ShouldBe("0.0.1");
        json.RootElement.GetProperty("url").GetString().ShouldBe("https://github.com/faraday208/pusula/releases/tag/v0.0.1");
    }

    [Fact]
    public async Task GetUpdate_GitHubDoesNotAnswerWell_SaysFailedWithNoReleaseAndTheServerKeepsAnswering()
    {
        using var root = new SyntheticRoot();
        await using var factory = new PusulaFactory(root.Path);
        var handler = new ReleaseHandler((_, _) => Task.FromResult(ReleaseHandler.Reply(HttpStatusCode.InternalServerError, "{}")));
        await using WebApplicationFactory<Program> app = WithCheck(factory, handler);
        using HttpClient client = app.CreateClient();

        using JsonDocument json = await UntilAsync(client, state => state != "NotChecked");

        json.RootElement.GetProperty("state").GetString().ShouldBe("Failed");
        json.RootElement.GetProperty("current").GetString().ShouldBe(AppVersion.Current);
        json.RootElement.TryGetProperty("latest", out _).ShouldBeFalse();
        json.RootElement.TryGetProperty("url", out _).ShouldBeFalse();
        json.RootElement.TryGetProperty("checkedAt", out _).ShouldBeTrue();
        using HttpResponseMessage sources = await client.GetResponseAsync("/api/sources");
        sources.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task GetUpdate_BeforeTheFirstAnswer_SaysNotCheckedAndDoesNotHoldTheServerBack()
    {
        using var root = new SyntheticRoot();
        await using var factory = new PusulaFactory(root.Path);
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var handler = new ReleaseHandler(async (_, _) =>
        {
            await gate.Task;
            return ReleaseHandler.Reply(HttpStatusCode.OK, """{"tag_name":"v99.0.0"}""");
        });
        await using WebApplicationFactory<Program> app = WithCheck(factory, handler);
        using HttpClient client = app.CreateClient();

        using (JsonDocument waiting = await client.GetJsonAsync("/api/update"))
        {
            waiting.RootElement.GetProperty("state").GetString().ShouldBe("NotChecked");
            waiting.RootElement.GetProperty("current").GetString().ShouldBe(AppVersion.Current);
            waiting.RootElement.TryGetProperty("latest", out _).ShouldBeFalse();
            waiting.RootElement.TryGetProperty("checkedAt", out _).ShouldBeFalse();
        }

        using HttpResponseMessage sources = await client.GetResponseAsync("/api/sources");
        sources.StatusCode.ShouldBe(HttpStatusCode.OK);

        gate.SetResult();
        using JsonDocument found = await UntilAsync(client, state => state != "NotChecked");
        found.RootElement.GetProperty("state").GetString().ShouldBe("Newer");
    }

    [Fact]
    public async Task GetUpdate_CheckTurnedOff_SaysOffAndTheServiceNeverAsks()
    {
        using var root = new SyntheticRoot();
        var handler = ReleaseHandler.Release("v99.0.0");
        // Production, where the check is on unless a setting says otherwise: the default of the factory is what keeps it off.
        await using var factory = new PusulaFactory(
            root.Path,
            environment: "Production",
            configureServices: services => services.AddHttpClient(ReleaseLookup.ClientName).ConfigurePrimaryHttpMessageHandler(() => handler));
        using HttpClient client = factory.CreateClient();

        using JsonDocument json = await client.GetJsonAsync("/api/update");

        json.RootElement.PropertyNames().ShouldBe(["state", "current"], ignoreOrder: true);
        json.RootElement.GetProperty("state").GetString().ShouldBe("Off");
        json.RootElement.GetProperty("current").GetString().ShouldBe(AppVersion.Current);

        // The tripwire: the default of the factory must keep the service from sending anything. The service has ended without a check, and nothing was sent.
        UpdateService service = factory.Services.GetServices<IHostedService>().OfType<UpdateService>().ShouldHaveSingleItem();
        DateTime end = DateTime.UtcNow.AddSeconds(10);
        while (service.ExecuteTask?.IsCompleted != true && DateTime.UtcNow < end)
        {
            await Task.Delay(10, TestContext.Current.CancellationToken);
        }

        service.ExecuteTask.ShouldNotBeNull().IsCompletedSuccessfully.ShouldBeTrue();
        handler.Count.ShouldBe(0);
    }

    [Fact]
    public async Task GetUpdate_AForeignHost_IsRefusedByTheHostGuard()
    {
        using var root = new SyntheticRoot();
        await using var factory = new PusulaFactory(root.Path);
        using HttpClient client = factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/update");
        request.Headers.Host = "evil.example";

        using HttpResponseMessage response = await client.SendAsync(request, TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task GetUpdate_FromAnotherMachine_IsAnswered()
    {
        using var root = new SyntheticRoot();
        await using var factory = new PusulaFactory(root.Path, simulateConnection: true);
        using HttpClient client = factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/update");
        request.Headers.Add("X-Forwarded-For", "203.0.113.7");

        using HttpResponseMessage response = await client.SendAsync(request, TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
    }
}
