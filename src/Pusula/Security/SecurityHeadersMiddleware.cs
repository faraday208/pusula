namespace Pusula.Security;

/// <summary>
/// Adds the security headers to every response: a content security policy that only allows the page's own files
/// (no inline script or style, no other origin), and headers that turn off content sniffing, referrers and framing.
/// </summary>
internal sealed class SecurityHeadersMiddleware(RequestDelegate next)
{
    internal const string ContentSecurityPolicy =
        "default-src 'self'; img-src 'self' data:; style-src 'self'; script-src 'self'; connect-src 'self'; "
        + "object-src 'none'; base-uri 'none'; frame-ancestors 'none'; form-action 'none'";

    /// <summary>Registers the headers to be written when the response starts, and runs the rest of the pipeline.</summary>
    public Task InvokeAsync(HttpContext context)
    {
        // Setting the headers when the response starts (and not up front) keeps them on responses that the
        // exception handler rebuilds from scratch.
        context.Response.OnStarting(
            static state =>
            {
                Apply(((HttpContext)state).Response);
                return Task.CompletedTask;
            },
            context);
        return next(context);
    }

    /// <summary>Writes the security headers to <paramref name="response"/>, replacing any earlier values.</summary>
    /// <param name="response">The response that has not started yet.</param>
    internal static void Apply(HttpResponse response)
    {
        IHeaderDictionary headers = response.Headers;
        headers["Content-Security-Policy"] = ContentSecurityPolicy;
        headers["X-Content-Type-Options"] = "nosniff";
        headers["Referrer-Policy"] = "no-referrer";
        headers["X-Frame-Options"] = "DENY";
    }
}
