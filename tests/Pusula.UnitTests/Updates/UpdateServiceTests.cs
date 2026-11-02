using System.Net;
using Microsoft.Extensions.Logging;
using Pusula.Updates;
using Pusula.UnitTests.Support;
using Shouldly;
using Xunit;

namespace Pusula.UnitTests.Updates;

public sealed class UpdateServiceTests
{
    private sealed class Harness(StubHandler handler, bool enabled = true, string current = "0.3.0") : IDisposable
    {
        public StubHandler Handler { get; } = handler;

        public UpdateNotice Notice { get; } = new(enabled, current, new ManualTimeProvider());

        public FakeHttpClientFactory Clients { get; } = new(handler);

        public FakeLifetime Lifetime { get; } = new();

        public CapturingLogger<UpdateService> Log { get; } = new();

        public List<string> Said { get; } = [];

        public void Dispose() => Lifetime.Dispose();

        public UpdateService Service(TimeSpan? interval = null) =>
            new(Notice, Clients, Lifetime, TimeProvider.System, Log, Said.Add) { Interval = interval ?? TimeSpan.FromHours(24) };
    }

    private static async Task<bool> UntilAsync(Func<bool> condition)
    {
        DateTime end = DateTime.UtcNow.AddSeconds(10);
        while (!condition() && DateTime.UtcNow < end)
        {
            await Task.Delay(10, TestContext.Current.CancellationToken);
        }

        return condition();
    }

    [Theory]
    [InlineData(null, false, true)]
    [InlineData(null, true, false)]
    [InlineData(true, true, true)]
    [InlineData(true, false, true)]
    [InlineData(false, false, false)]
    [InlineData(false, true, false)]
    public void ShouldCheck_TheSettingWhenSet_ElseOutsideDevelopmentOnly(bool? setting, bool isDevelopment, bool expected) =>
        UpdateService.ShouldCheck(setting, isDevelopment).ShouldBe(expected);

    [Fact]
    public void Interval_ByDefault_IsADay()
    {
        using var harness = new Harness(StubHandler.Json("{}"));

        harness.Service().Interval.ShouldBe(TimeSpan.FromHours(24));
    }

    [Fact]
    public async Task CheckAsync_ANewerRelease_SaysSoOnceInOneLine()
    {
        using var harness = new Harness(StubHandler.Json(StubHandler.Release("v0.4.0")));
        UpdateService service = harness.Service();

        await service.CheckAsync(TestContext.Current.CancellationToken);

        harness.Said.ShouldBe(["pusula: 0.4.0 is out (this is 0.3.0): https://github.com/faraday208/pusula/releases/tag/v0.4.0"]);
        harness.Notice.Snapshot().State.ShouldBe(UpdateState.Newer);
        harness.Clients.Names.ShouldBe(["pusula-updates"]);
    }

    [Fact]
    public async Task CheckAsync_TheSameNewerReleaseAgain_IsNotSaidAgain()
    {
        using var harness = new Harness(StubHandler.Json(StubHandler.Release("v0.4.0")));
        UpdateService service = harness.Service();

        await service.CheckAsync(TestContext.Current.CancellationToken);
        await service.CheckAsync(TestContext.Current.CancellationToken);
        await service.CheckAsync(TestContext.Current.CancellationToken);

        harness.Said.Count.ShouldBe(1);
        harness.Handler.Count.ShouldBe(3);
    }

    [Fact]
    public async Task CheckAsync_AFailureBetweenTwoChecksOfTheSameRelease_DoesNotSayItAgain()
    {
        string body = StubHandler.Release("v0.4.0");
        HttpStatusCode status = HttpStatusCode.OK;
        using var harness = new Harness(new StubHandler((_, _) => Task.FromResult(StubHandler.Reply(status, body))));
        UpdateService service = harness.Service();

        await service.CheckAsync(TestContext.Current.CancellationToken);
        status = HttpStatusCode.BadGateway;
        await service.CheckAsync(TestContext.Current.CancellationToken);
        status = HttpStatusCode.OK;
        await service.CheckAsync(TestContext.Current.CancellationToken);

        harness.Said.Count.ShouldBe(1);
    }

    [Fact]
    public async Task CheckAsync_ANewerReleaseThanTheOneSaid_IsSaidToo()
    {
        string tag = "v0.4.0";
        using var harness = new Harness(new StubHandler((_, _) => Task.FromResult(StubHandler.Reply(HttpStatusCode.OK, StubHandler.Release(tag)))));
        UpdateService service = harness.Service();

        await service.CheckAsync(TestContext.Current.CancellationToken);
        tag = "v0.5.0";
        await service.CheckAsync(TestContext.Current.CancellationToken);

        harness.Said.Count.ShouldBe(2);
        harness.Said[1].ShouldBe("pusula: 0.5.0 is out (this is 0.3.0): https://github.com/faraday208/pusula/releases/tag/v0.5.0");
    }

    [Fact]
    public async Task CheckAsync_TheSameVersionOrAnOlderOne_SaysNothing()
    {
        using var harness = new Harness(StubHandler.Json(StubHandler.Release("v0.3.0")));

        await harness.Service().CheckAsync(TestContext.Current.CancellationToken);

        harness.Said.ShouldBeEmpty();
        harness.Notice.Snapshot().State.ShouldBe(UpdateState.UpToDate);
    }

    [Fact]
    public async Task CheckAsync_AFailure_IsLoggedAtInformationWithoutAStackTraceAndSaysNothing()
    {
        using var harness = new Harness(StubHandler.Status(HttpStatusCode.Forbidden));

        await harness.Service().CheckAsync(TestContext.Current.CancellationToken);

        harness.Notice.Snapshot().State.ShouldBe(UpdateState.Failed);
        harness.Said.ShouldBeEmpty();
        (LogLevel level, string message) = harness.Log.Entries.ShouldHaveSingleItem();
        level.ShouldBe(LogLevel.Information);
        message.ShouldBe("Could not check for a newer pusula: GitHub answered 403; the next check is in 24 hours");
        message.ShouldNotContain(" at ");
    }

    [Fact]
    public async Task ExecuteAsync_TheCheckIsOff_NeverAsksAndNeverWaitsForTheStart()
    {
        using var harness = new Harness(StubHandler.Json(StubHandler.Release("v0.4.0")), enabled: false);
        using UpdateService service = harness.Service(TimeSpan.FromMilliseconds(20));

        await service.StartAsync(TestContext.Current.CancellationToken);
        harness.Lifetime.Start();
        await Task.Delay(150, TestContext.Current.CancellationToken);
        await service.StopAsync(TestContext.Current.CancellationToken);

        harness.Handler.Count.ShouldBe(0);
        harness.Notice.Snapshot().State.ShouldBe(UpdateState.Off);
        harness.Said.ShouldBeEmpty();
    }

    [Fact]
    public async Task ExecuteAsync_TheStart_IsNotHeldBackAndTheFirstCheckWaitsForTheServer()
    {
        using var harness = new Harness(StubHandler.Json(StubHandler.Release("v0.4.0")));
        using UpdateService service = harness.Service();

        await service.StartAsync(TestContext.Current.CancellationToken).WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        await Task.Delay(100, TestContext.Current.CancellationToken);
        harness.Handler.Count.ShouldBe(0, "no request before the server has started");
        harness.Notice.Snapshot().State.ShouldBe(UpdateState.NotChecked);

        harness.Lifetime.Start();
        (await UntilAsync(() => harness.Notice.Snapshot().State == UpdateState.Newer)).ShouldBeTrue();
        await service.StopAsync(TestContext.Current.CancellationToken);

        harness.Handler.Count.ShouldBe(1);
        harness.Said.Count.ShouldBe(1);
    }

    [Fact]
    public async Task ExecuteAsync_EveryInterval_ChecksAgainAndSaysTheReleaseOnce()
    {
        using var harness = new Harness(StubHandler.Json(StubHandler.Release("v0.4.0")));
        using UpdateService service = harness.Service(TimeSpan.FromMilliseconds(30));
        harness.Lifetime.Start();

        await service.StartAsync(TestContext.Current.CancellationToken);
        (await UntilAsync(() => harness.Handler.Count >= 3)).ShouldBeTrue();
        await service.StopAsync(TestContext.Current.CancellationToken);

        harness.Said.Count.ShouldBe(1);
    }

    [Fact]
    public async Task ExecuteAsync_AFailedCheck_WaitsForTheNextTickAndTheServiceKeepsRunning()
    {
        int calls = 0;
        using var harness = new Harness(new StubHandler((_, _) =>
            Task.FromResult(Interlocked.Increment(ref calls) == 1 ? StubHandler.Reply(HttpStatusCode.BadGateway, "{}") : StubHandler.Reply(HttpStatusCode.OK, StubHandler.Release("v0.4.0")))));
        using UpdateService service = harness.Service(TimeSpan.FromMilliseconds(30));
        harness.Lifetime.Start();

        await service.StartAsync(TestContext.Current.CancellationToken);
        (await UntilAsync(() => harness.Notice.Snapshot().State == UpdateState.Newer)).ShouldBeTrue();
        await service.StopAsync(TestContext.Current.CancellationToken);

        harness.Log.Entries.ShouldContain(entry => entry.Level == LogLevel.Information && entry.Message.Contains("502", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ExecuteAsync_TheLineCannotBeWritten_IsLoggedAndTheLoopGoesOn()
    {
        using var harness = new Harness(StubHandler.Json(StubHandler.Release("v0.4.0")));
        int announced = 0;
        using var service = new UpdateService(harness.Notice, harness.Clients, harness.Lifetime, TimeProvider.System, harness.Log, _ =>
        {
            announced++;
            throw new IOException("the console is closed");
        })
        { Interval = TimeSpan.FromMilliseconds(30) };
        harness.Lifetime.Start();

        await service.StartAsync(TestContext.Current.CancellationToken);
        (await UntilAsync(() => harness.Handler.Count >= 3)).ShouldBeTrue();
        service.ExecuteTask!.IsCompleted.ShouldBeFalse("the service is still running");
        await service.StopAsync(TestContext.Current.CancellationToken);

        announced.ShouldBeGreaterThanOrEqualTo(1);
        harness.Log.Entries.ShouldContain(entry => entry.Level == LogLevel.Information && entry.Message.Contains("the console is closed", StringComparison.Ordinal));
    }

    [Fact]
    public async Task StopAsync_WhileWaitingForTheServer_EndsWithoutAnError()
    {
        using var harness = new Harness(StubHandler.Json(StubHandler.Release("v0.4.0")));
        using UpdateService service = harness.Service();

        await service.StartAsync(TestContext.Current.CancellationToken);
        await service.StopAsync(TestContext.Current.CancellationToken).WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);

        harness.Handler.Count.ShouldBe(0);
    }
}
