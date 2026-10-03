using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Pusula.Security;
using Shouldly;
using Xunit;

namespace Pusula.UnitTests.Security;

public sealed class SecurityHeadersMiddlewareTests
{
    private const string ExpectedPolicy =
        "default-src 'self'; img-src 'self' data:; style-src 'self'; script-src 'self'; connect-src 'self'; "
        + "object-src 'none'; base-uri 'none'; frame-ancestors 'none'; form-action 'none'";

    [Fact]
    public void Apply_SetsTheFourHeaders()
    {
        HttpResponse response = new DefaultHttpContext().Response;

        SecurityHeadersMiddleware.Apply(response);

        response.Headers["Content-Security-Policy"].ToString().ShouldBe(ExpectedPolicy);
        response.Headers["X-Content-Type-Options"].ToString().ShouldBe("nosniff");
        response.Headers["Referrer-Policy"].ToString().ShouldBe("no-referrer");
        response.Headers["X-Frame-Options"].ToString().ShouldBe("DENY");
        SecurityHeadersMiddleware.ContentSecurityPolicy.ShouldBe(ExpectedPolicy);
    }

    [Fact]
    public void Apply_ReplacesValuesThatAreAlreadyThere()
    {
        HttpResponse response = new DefaultHttpContext().Response;
        response.Headers["X-Frame-Options"] = "SAMEORIGIN";
        response.Headers["Content-Security-Policy"] = "default-src *";

        SecurityHeadersMiddleware.Apply(response);

        response.Headers["X-Frame-Options"].ToString().ShouldBe("DENY");
        response.Headers["Content-Security-Policy"].ToString().ShouldBe(ExpectedPolicy);
    }

    [Fact]
    public async Task InvokeAsync_RunsTheRestOfThePipelineAndWritesTheHeadersWhenTheResponseStarts()
    {
        var responseFeature = new CapturingResponseFeature();
        var context = new DefaultHttpContext();
        context.Features.Set<IHttpResponseFeature>(responseFeature);
        bool nextCalled = false;
        var middleware = new SecurityHeadersMiddleware(_ =>
        {
            nextCalled = true;
            return Task.CompletedTask;
        });

        await middleware.InvokeAsync(context);

        nextCalled.ShouldBeTrue();
        context.Response.Headers.ContainsKey("X-Frame-Options").ShouldBeFalse("The headers are only written when the response starts.");
        await responseFeature.RunStartingCallbacksAsync();
        context.Response.Headers["X-Frame-Options"].ToString().ShouldBe("DENY");
        context.Response.Headers["Content-Security-Policy"].ToString().ShouldBe(ExpectedPolicy);
    }

    [Fact]
    public async Task InvokeAsync_HeadersSetByLaterMiddlewareThatWipeTheResponse_AreStillWritten()
    {
        var responseFeature = new CapturingResponseFeature();
        var context = new DefaultHttpContext();
        context.Features.Set<IHttpResponseFeature>(responseFeature);
        var middleware = new SecurityHeadersMiddleware(next =>
        {
            // What the exception handler does: start over with an empty header collection.
            next.Response.Headers.Clear();
            return Task.CompletedTask;
        });

        await middleware.InvokeAsync(context);
        await responseFeature.RunStartingCallbacksAsync();

        context.Response.Headers["X-Content-Type-Options"].ToString().ShouldBe("nosniff");
    }

    // Runs the callbacks that are registered with OnStarting when the response is started; the default feature ignores them.
    private sealed class CapturingResponseFeature : HttpResponseFeature
    {
        private readonly List<(Func<object, Task> Callback, object State)> _callbacks = [];

        public override void OnStarting(Func<object, Task> callback, object state) => _callbacks.Add((callback, state));

        public async Task RunStartingCallbacksAsync()
        {
            foreach ((Func<object, Task> callback, object state) in _callbacks)
            {
                await callback(state);
            }
        }
    }
}
