using System.Net;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using Pusula.Startup;

namespace Pusula.Security;

/// <summary>
/// Rejects a request with <c>400 Bad Request</c> unless its <c>Host</c> header passes <see cref="HostGuard"/>. It is
/// the first middleware of the pipeline, so a request that is not meant for this server gets no further. This is
/// what stops a malicious web page from reading the user's configuration files through DNS rebinding.
/// </summary>
internal sealed partial class HostGuardMiddleware
{
    private const int MaxLoggedHostLength = 128;

    private readonly RequestDelegate _next;
    private readonly ILogger<HostGuardMiddleware> _logger;
    private readonly string _machineName;
    private readonly string[] _allowedHosts;

    /// <summary>Creates the middleware; the allowed host names are read from the options once, when the pipeline is built.</summary>
    public HostGuardMiddleware(RequestDelegate next, IOptions<PusulaOptions> options, ILogger<HostGuardMiddleware> logger)
    {
        _next = next;
        _logger = logger;
        _machineName = Dns.GetHostName();
        _allowedHosts = options.Value.ParseAllowedHosts();
    }

    /// <summary>Runs the rest of the pipeline for an allowed host; answers 400 otherwise.</summary>
    public Task InvokeAsync(HttpContext context, IProblemDetailsService problemDetails)
    {
        string host = context.Request.Headers.Host.ToString();
        return HostGuard.IsAllowed(host, _machineName, _allowedHosts)
            ? _next(context)
            : RejectAsync(context, problemDetails, host);
    }

    private async Task RejectAsync(HttpContext context, IProblemDetailsService problemDetails, string host)
    {
        LogRejected(host.Length > MaxLoggedHostLength ? host[..MaxLoggedHostLength] : host);

        context.Response.StatusCode = StatusCodes.Status400BadRequest;
        SecurityHeadersMiddleware.Apply(context.Response);
        await problemDetails.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = context,
            ProblemDetails = new ProblemDetails
            {
                Status = StatusCodes.Status400BadRequest,
                Title = "Invalid Host header",
                Detail = "The Host header is missing or names a host this server does not answer to.",
            },
        });
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Rejected a request with the Host header '{Host}'. Add the name to Pusula:AllowedHosts to serve it.")]
    private partial void LogRejected(string host);
}
