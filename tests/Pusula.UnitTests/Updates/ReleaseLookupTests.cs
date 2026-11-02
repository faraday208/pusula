using System.Net;
using Pusula.Startup;
using Pusula.Updates;
using Pusula.UnitTests.Support;
using Shouldly;
using Xunit;

namespace Pusula.UnitTests.Updates;

public sealed class ReleaseLookupTests
{
    private static readonly TimeSpan Generous = TimeSpan.FromSeconds(30);

    private static async Task<LookupResult> LookUpAsync(StubHandler handler, TimeSpan? timeout = null)
    {
        using var client = new HttpClient(handler, disposeHandler: false);
        return await ReleaseLookup.LatestTagAsync(client, timeout ?? Generous, TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task LatestTagAsync_AReleaseWithAGoodTag_GivesTheTag()
    {
        var handler = StubHandler.Json(StubHandler.Release("v0.4.0"));

        LookupResult result = await LookUpAsync(handler);

        result.Problem.ShouldBeNull();
        result.Tag.ShouldNotBeNull().Text.ShouldBe("v0.4.0");
        result.Tag.Url.ShouldBe("https://github.com/faraday208/pusula/releases/tag/v0.4.0");
    }

    [Fact]
    public async Task LatestTagAsync_TheRequest_IsTheOneForTheLatestReleaseWithTheHeadersThatGitHubAsksFor()
    {
        var handler = StubHandler.Json(StubHandler.Release("v0.4.0"));

        await LookUpAsync(handler);

        handler.Count.ShouldBe(1);
        HttpRequestMessage request = handler.Last.ShouldNotBeNull();
        request.Method.ShouldBe(HttpMethod.Get);
        request.RequestUri!.ToString().ShouldBe("https://api.github.com/repos/faraday208/pusula/releases/latest");
        request.Headers.UserAgent.ToString().ShouldBe($"pusula/{AppVersion.Current}");
        request.Headers.Accept.ToString().ShouldBe("application/vnd.github+json");
        request.Headers.GetValues("X-GitHub-Api-Version").ShouldBe(["2022-11-28"]);
        request.Headers.Contains("Authorization").ShouldBeFalse();
        request.Headers.Contains("Cookie").ShouldBeFalse();
        request.Content.ShouldBeNull();
    }

    [Fact]
    public async Task LatestTagAsync_AnHtmlUrlThatPointsElsewhere_IsIgnoredAndThePageIsMadeFromTheTag()
    {
        var handler = StubHandler.Json("""{"tag_name":"v0.4.0","html_url":"https://example.invalid/download/pusula-0.4.0"}""");

        LookupResult result = await LookUpAsync(handler);

        result.Tag.ShouldNotBeNull().Url.ShouldBe("https://github.com/faraday208/pusula/releases/tag/v0.4.0");
    }

    [Theory]
    [InlineData("""{"name":"v0.4.0"}""", "no tag_name")]
    [InlineData("""{"tag_name":null}""", "no tag_name")]
    [InlineData("""{"tag_name":4}""", "no tag_name")]
    [InlineData("""[{"tag_name":"v0.4.0"}]""", "no tag_name")]
    [InlineData("\"v0.4.0\"", "no tag_name")]
    [InlineData("""{"tag_name":"0.4.0"}""", "not a version")]
    [InlineData("""{"tag_name":"v0.4"}""", "not a version")]
    [InlineData("""{"tag_name":"latest"}""", "not a version")]
    [InlineData("""{"tag_name":"v0.4.0/../x"}""", "not a version")]
    [InlineData("""{"tag_name":"v0.4.0\n"}""", "not a version")]
    [InlineData("not json", "not JSON")]
    [InlineData("{\"tag_name\":", "not JSON")]
    [InlineData("", "not JSON")]
    public async Task LatestTagAsync_AnAnswerWithoutAGoodTag_IsAFailure(string body, string problem)
    {
        LookupResult result = await LookUpAsync(StubHandler.Json(body));

        result.Tag.ShouldBeNull();
        result.Problem.ShouldNotBeNull().ShouldContain(problem);
    }

    [Theory]
    [InlineData(HttpStatusCode.NotFound)]
    [InlineData(HttpStatusCode.Forbidden)]
    [InlineData(HttpStatusCode.TooManyRequests)]
    [InlineData(HttpStatusCode.InternalServerError)]
    [InlineData(HttpStatusCode.NoContent)]
    [InlineData(HttpStatusCode.MovedPermanently)]
    public async Task LatestTagAsync_AStatusThatIsNot200_IsAFailure(HttpStatusCode status)
    {
        LookupResult result = await LookUpAsync(StubHandler.Status(status, StubHandler.Release("v9.9.9")));

        result.Tag.ShouldBeNull();
        result.Problem.ShouldBe($"GitHub answered {(int)status}");
    }

    [Fact]
    public async Task LatestTagAsync_AnAnswerOfExactlyTheLimit_IsRead()
    {
        string head = "{\"tag_name\":\"v0.4.0\",\"pad\":\"";
        string body = head + new string('a', ReleaseLookup.MaxBytes - head.Length - 2) + "\"}";
        body.Length.ShouldBe(ReleaseLookup.MaxBytes);

        LookupResult result = await LookUpAsync(StubHandler.Json(body));

        result.Tag.ShouldNotBeNull().Text.ShouldBe("v0.4.0");
    }

    [Fact]
    public async Task LatestTagAsync_AnAnswerOneByteOverTheLimit_IsAFailure()
    {
        string head = "{\"tag_name\":\"v0.4.0\",\"pad\":\"";
        string body = head + new string('a', ReleaseLookup.MaxBytes - head.Length - 1) + "\"}";
        body.Length.ShouldBe(ReleaseLookup.MaxBytes + 1);

        LookupResult result = await LookUpAsync(StubHandler.Json(body));

        result.Tag.ShouldBeNull();
        result.Problem.ShouldNotBeNull().ShouldContain("1 MiB");
    }

    [Fact]
    public async Task LatestTagAsync_AnAnswerThatNeverEnds_IsReadOnlyUpToTheLimit()
    {
        long served = 0;
        var handler = new StubHandler((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StreamContent(new EndlessStream(count => served = count)) }));

        LookupResult result = await LookUpAsync(handler);

        result.Tag.ShouldBeNull();
        result.Problem.ShouldNotBeNull().ShouldContain("1 MiB");
        served.ShouldBeLessThanOrEqualTo(ReleaseLookup.MaxBytes + 64L * 1024);
    }

    [Fact]
    public async Task LatestTagAsync_NoAnswerInTime_IsAFailure()
    {
        var handler = new StubHandler(async (_, cancellationToken) =>
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            return StubHandler.Reply(HttpStatusCode.OK, StubHandler.Release("v0.4.0"));
        });

        LookupResult result = await LookUpAsync(handler, TimeSpan.FromMilliseconds(100));

        result.Tag.ShouldBeNull();
        result.Problem.ShouldNotBeNull().ShouldContain("no answer within");
    }

    [Fact]
    public async Task LatestTagAsync_ABodyThatNeverComesInTime_IsAFailure()
    {
        var handler = new StubHandler((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StreamContent(new StalledStream()) }));

        LookupResult result = await LookUpAsync(handler, TimeSpan.FromMilliseconds(100));

        result.Tag.ShouldBeNull();
        result.Problem.ShouldNotBeNull().ShouldContain("no answer within");
    }

    [Fact]
    public async Task LatestTagAsync_NoNetwork_IsAFailureWithTheReason()
    {
        var handler = new StubHandler((_, _) => throw new HttpRequestException("Name or service not known (api.github.com:443)"));

        LookupResult result = await LookUpAsync(handler);

        result.Tag.ShouldBeNull();
        result.Problem.ShouldBe("Name or service not known (api.github.com:443)");
    }

    [Fact]
    public async Task LatestTagAsync_PusulaStopping_ThrowsInsteadOfCountingAsAFailure()
    {
        var handler = new StubHandler(async (_, cancellationToken) =>
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            return StubHandler.Reply(HttpStatusCode.OK, "{}");
        });
        using var client = new HttpClient(handler, disposeHandler: false);
        using var stopping = new CancellationTokenSource();
        await stopping.CancelAsync();

        await Should.ThrowAsync<OperationCanceledException>(() => ReleaseLookup.LatestTagAsync(client, Generous, stopping.Token));
    }

    // A body that is as long as it is asked for, and tells how much was asked.
    private sealed class EndlessStream(Action<long> asked) : Stream
    {
        private long _read;

        public override bool CanRead => true;

        public override bool CanSeek => false;

        public override bool CanWrite => false;

        public override long Length => throw new NotSupportedException();

        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }

        public override int Read(byte[] buffer, int offset, int count) => Read(buffer.AsSpan(offset, count));

        public override int Read(Span<byte> buffer)
        {
            buffer.Fill((byte)'a');
            _read += buffer.Length;
            asked(_read);
            return buffer.Length;
        }

        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) => ValueTask.FromResult(Read(buffer.Span));

        public override void Flush()
        {
        }

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }

    // A body whose first byte never comes.
    private sealed class StalledStream : Stream
    {
        public override bool CanRead => true;

        public override bool CanSeek => false;

        public override bool CanWrite => false;

        public override long Length => throw new NotSupportedException();

        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }

        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            return 0;
        }

        public override void Flush()
        {
        }

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
