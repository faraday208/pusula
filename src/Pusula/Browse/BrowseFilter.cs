using Microsoft.Extensions.Options;
using Pusula.Sources;
using Pusula.Startup;

namespace Pusula.Browse;

/// <summary>
/// Runs before the endpoints of the folder browser: answers 403 (<c>Remote</c>, <c>CommandLine</c> or <c>CrossOrigin</c>,
/// see <see cref="SourceEditAccess"/>) unless the request may add sources, which is what the browser is for. The rule is
/// that of <see cref="SourceEditFilter"/>, except that a request the user made by hand (<c>Sec-Fetch-Site: none</c>, as
/// when the address of the endpoint is typed into the browser) passes too: it only reads. A request from another machine
/// passes when <c>Pusula:AllowRemoteEdit</c> is on. A refused request is logged: it is either a mistake or someone trying.
/// </summary>
internal sealed partial class BrowseFilter(ISourceRegistry registry, IOptions<PusulaOptions> options, ILogger<BrowseFilter> logger) : IEndpointFilter
{
    /// <inheritdoc />
    public ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        HttpContext http = context.HttpContext;
        if (SourceEditAccess.Refusal(http, registry.SourcesFile, options.Value.AllowRemoteEdit, allowUserInitiated: true) is not { } refusal)
        {
            return next(context);
        }

        LogRefused(refusal, http.Request.Path.Value, http.Connection.RemoteIpAddress?.ToString());
        return ValueTask.FromResult<object?>(SourceProblems.For(refusal));
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Refused to list folders ({Reason}) on {Path} from {Remote}")]
    private partial void LogRefused(EditError reason, string? path, string? remote);
}
