using Pusula.Indexing;

namespace Pusula.Sources;

/// <summary>
/// Runs before every endpoint of <c>/api/sources/{source}</c>: finds the source that the id in the URL names. An id
/// that no source has is a 404, a source that could not be started a 503 (its problem details carry the
/// <see cref="SourceErrorCode"/> as <c>code</c>); otherwise the index of the source becomes the
/// <see cref="IIndexProvider"/> feature of the request, which is where the handlers get it from. Nothing from the
/// request is ever used as a path: the id only selects one of the sources that were configured.
/// </summary>
internal sealed class SourceFilter(ISourceRegistry registry) : IEndpointFilter
{
    /// <inheritdoc />
    public ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        HttpContext http = context.HttpContext;
        if (http.GetRouteValue("source") is not string id || !registry.TryGet(id, out SourceEntry? entry))
        {
            return ValueTask.FromResult<object?>(TypedResults.Problem(
                title: "Source not found",
                detail: "No source has this id.",
                statusCode: StatusCodes.Status404NotFound));
        }

        if (entry.Index is null)
        {
            return ValueTask.FromResult<object?>(TypedResults.Problem(
                title: "Source unavailable",
                detail: entry.Error,
                statusCode: StatusCodes.Status503ServiceUnavailable,
                extensions: new Dictionary<string, object?> { ["code"] = entry.ErrorCode?.ToString() }));
        }

        http.Features.Set(entry.Index);
        return next(context);
    }
}
