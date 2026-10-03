using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Http.HttpResults;
using Pusula.Indexing;

namespace Pusula.Files;

/// <summary>The <c>/api/sources/{source}/file</c> endpoint.</summary>
internal static class FileEndpoints
{
    /// <summary>Maps <c>GET file</c> onto the route group of a source (see <c>SourceEndpoints.MapSources</c>).</summary>
    /// <param name="routes">The route group <c>/api/sources/{source}</c>.</param>
    public static IEndpointRouteBuilder MapFiles(this IEndpointRouteBuilder routes)
    {
        routes.MapGet("/file", (string? path, HttpContext http) => GetFile(path, http.Features.GetRequiredFeature<IIndexProvider>()))
            .WithName("GetFile")
            .WithSummary("Get one indexed file")
            .WithDescription(
                "Returns the file of the source whose index key equals the 'path' query parameter (a path relative to "
                + "the source's folder, separated by '/', as listed by the tree endpoint). Files are looked up in the "
                + "index of that source only; the file system is never touched with the given value, so paths that are "
                + "not index keys (absolute paths, '../', non-Markdown files) are simply not found (404, like an id that "
                + "no source has). Each backlink carries an excerpt: the text of the line in the source file that holds "
                + "the link. A file of a vault or a plain Markdown folder also lists its tags, when it has any. When the "
                + "frontmatter is malformed, 'frontmatterError' says why; 'frontmatterErrorLine' (1-based, counted from "
                + "the top of the file) and 'frontmatterErrorText' (the text of that line) say where, and are left out "
                + "when the error has no known line.")
            .WithTags("Files")
            .ProducesProblem(StatusCodes.Status400BadRequest);
        return routes;
    }

    internal static Results<Ok<FileResponse>, ProblemHttpResult> GetFile(string? path, IIndexProvider provider)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return TypedResults.Problem(
                title: "Missing path",
                detail: "The 'path' query parameter is required.",
                statusCode: StatusCodes.Status400BadRequest);
        }

        ConfigIndex index = provider.Current;
        if (!index.Files.TryGetValue(path, out ConfigFile? file))
        {
            return TypedResults.Problem(
                title: "File not found",
                detail: "No indexed file has this path.",
                statusCode: StatusCodes.Status404NotFound);
        }

        return TypedResults.Ok(FileMapper.ToResponse(index, file));
    }
}
