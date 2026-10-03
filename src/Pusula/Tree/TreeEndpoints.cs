using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Http.HttpResults;
using Pusula.Indexing;

namespace Pusula.Tree;

/// <summary>The <c>/api/sources/{source}/tree</c> endpoint.</summary>
internal static class TreeEndpoints
{
    /// <summary>Maps <c>GET tree</c> onto the route group of a source (see <c>SourceEndpoints.MapSources</c>).</summary>
    /// <param name="routes">The route group <c>/api/sources/{source}</c>.</param>
    public static IEndpointRouteBuilder MapTree(this IEndpointRouteBuilder routes)
    {
        routes.MapGet("/tree", (HttpContext http) => GetTree(http.Features.GetRequiredFeature<IIndexProvider>()))
            .WithName("GetTree")
            .WithSummary("Get the file tree")
            .WithDescription(
                "Returns every indexed Markdown file of the source as a tree, with the layer, load mode, estimated "
                + "tokens (of the whole file and of the part that is loaded in every session; a directory carries the "
                + "sums of the files below it), number of broken links and orphan flag of each file. A file of a "
                + "vault or a plain Markdown folder also lists its tags, when it has any.")
            .WithTags("Tree");
        return routes;
    }

    internal static Ok<TreeResponse> GetTree(IIndexProvider provider) =>
        TypedResults.Ok(TreeMapper.ToResponse(provider.Current));
}
