using Microsoft.Extensions.Options;
using Pusula.Startup;

namespace Pusula.Sources;

/// <summary>
/// Runs before the endpoints that add and remove sources: answers 403 (<c>Remote</c>, <c>CommandLine</c> or
/// <c>CrossOrigin</c>, see <see cref="SourceEditAccess"/>) unless the request may change the sources. A request from
/// another machine may when <c>Pusula:AllowRemoteEdit</c> is on. A refused request is logged: it is either a mistake or
/// someone trying.
/// </summary>
internal sealed partial class SourceEditFilter(ISourceRegistry registry, IOptions<PusulaOptions> options, ILogger<SourceEditFilter> logger) : IEndpointFilter
{
    /// <inheritdoc />
    public ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        HttpContext http = context.HttpContext;
        if (SourceEditAccess.Refusal(http, registry.SourcesFile, options.Value.AllowRemoteEdit) is not { } refusal)
        {
            return next(context);
        }

        LogRefused(refusal, http.Request.Method, http.Connection.RemoteIpAddress?.ToString());
        return ValueTask.FromResult<object?>(SourceProblems.For(refusal));
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Refused {Method} on the sources ({Reason}) from {Remote}")]
    private partial void LogRefused(EditError reason, string method, string? remote);
}
