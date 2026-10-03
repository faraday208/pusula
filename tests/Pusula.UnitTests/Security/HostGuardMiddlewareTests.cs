using System.Net;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Pusula.Security;
using Pusula.Startup;
using Pusula.UnitTests.Support;
using Shouldly;
using Xunit;

namespace Pusula.UnitTests.Security;

public sealed class HostGuardMiddlewareTests
{
    private static HostGuardMiddleware Create(RequestDelegate next, string? allowedHosts = null, ILogger<HostGuardMiddleware>? logger = null) =>
        new(next, Options.Create(new PusulaOptions { AllowedHosts = allowedHosts }), logger ?? NullLogger<HostGuardMiddleware>.Instance);

    private static DefaultHttpContext ContextFor(string? host)
    {
        var context = new DefaultHttpContext();
        if (host is not null)
        {
            context.Request.Headers.Host = host;
        }

        return context;
    }

    [Theory]
    [InlineData("localhost:5190")]
    [InlineData("127.0.0.1")]
    [InlineData("[::1]:5190")]
    public async Task InvokeAsync_AllowedHost_RunsTheRestOfThePipeline(string host)
    {
        bool nextCalled = false;
        HostGuardMiddleware middleware = Create(_ =>
        {
            nextCalled = true;
            return Task.CompletedTask;
        });
        DefaultHttpContext context = ContextFor(host);

        await middleware.InvokeAsync(context, new RecordingProblemDetailsService());

        nextCalled.ShouldBeTrue();
        context.Response.StatusCode.ShouldBe(StatusCodes.Status200OK);
    }

    [Fact]
    public async Task InvokeAsync_MachineNameAndConfiguredHosts_AreAllowed()
    {
        int served = 0;
        HostGuardMiddleware middleware = Create(
            _ =>
            {
                served++;
                return Task.CompletedTask;
            },
            allowedHosts: "pusula.example.com;other.example");

        await middleware.InvokeAsync(ContextFor(Dns.GetHostName()), new RecordingProblemDetailsService());
        await middleware.InvokeAsync(ContextFor("pusula.example.com:8443"), new RecordingProblemDetailsService());
        await middleware.InvokeAsync(ContextFor("OTHER.example"), new RecordingProblemDetailsService());

        served.ShouldBe(3);
    }

    [Theory]
    [InlineData("evil.example")]
    [InlineData("evil.example:5190")]
    [InlineData("")]
    [InlineData(null)]
    public async Task InvokeAsync_ForeignOrMissingHost_Answers400WithProblemDetailsAndDoesNotRunTheRestOfThePipeline(string? host)
    {
        bool nextCalled = false;
        HostGuardMiddleware middleware = Create(_ =>
        {
            nextCalled = true;
            return Task.CompletedTask;
        });
        DefaultHttpContext context = ContextFor(host);
        var problemDetails = new RecordingProblemDetailsService();

        await middleware.InvokeAsync(context, problemDetails);

        nextCalled.ShouldBeFalse();
        context.Response.StatusCode.ShouldBe(StatusCodes.Status400BadRequest);
        context.Response.Headers["X-Frame-Options"].ToString().ShouldBe("DENY");
        context.Response.Headers["Content-Security-Policy"].ToString().ShouldNotBeEmpty();
        problemDetails.Written.Count.ShouldBe(1);
        problemDetails.Written[0].Status.ShouldBe(StatusCodes.Status400BadRequest);
        problemDetails.Written[0].Title.ShouldBe("Invalid Host header");
        problemDetails.Written[0].Detail.ShouldNotBeNull().ShouldNotContain("evil.example");
    }

    [Fact]
    public async Task InvokeAsync_RejectedHost_IsLoggedWithAHintAndTruncated()
    {
        var logger = new CapturingLogger<HostGuardMiddleware>();
        HostGuardMiddleware middleware = Create(_ => Task.CompletedTask, logger: logger);
        string longHost = new string('a', 500) + ".example";

        await middleware.InvokeAsync(ContextFor("evil.example"), new RecordingProblemDetailsService());
        await middleware.InvokeAsync(ContextFor(longHost), new RecordingProblemDetailsService());

        logger.Entries.Count.ShouldBe(2);
        logger.Entries.ShouldAllBe(entry => entry.Level == LogLevel.Warning && entry.Message.Contains("Pusula:AllowedHosts", StringComparison.Ordinal));
        logger.Entries[0].Message.ShouldContain("'evil.example'");
        logger.Entries[1].Message.ShouldNotContain(longHost);
        logger.Entries[1].Message.ShouldContain(new string('a', 128));
    }

    private sealed class RecordingProblemDetailsService : IProblemDetailsService
    {
        public List<Microsoft.AspNetCore.Mvc.ProblemDetails> Written { get; } = [];

        public ValueTask WriteAsync(ProblemDetailsContext context)
        {
            Written.Add(context.ProblemDetails);
            return ValueTask.CompletedTask;
        }

        public ValueTask<bool> TryWriteAsync(ProblemDetailsContext context)
        {
            Written.Add(context.ProblemDetails);
            return ValueTask.FromResult(true);
        }
    }
}
