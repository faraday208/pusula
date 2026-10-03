using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Http.HttpResults;
using Pusula.Indexing;

namespace Pusula.Overview;

/// <summary>The <c>/api/sources/{source}/overview</c> endpoint.</summary>
internal static class OverviewEndpoints
{
    /// <summary>Maps <c>GET overview</c> onto the route group of a source (see <c>SourceEndpoints.MapSources</c>).</summary>
    /// <param name="routes">The route group <c>/api/sources/{source}</c>.</param>
    public static IEndpointRouteBuilder MapOverview(this IEndpointRouteBuilder routes)
    {
        routes.MapGet("/overview", (HttpContext http) => GetOverview(http.Features.GetRequiredFeature<IIndexProvider>()))
            .WithName("GetOverview")
            .WithSummary("Get the overview")
            .WithDescription(
                "Summarizes the source: its profile, tokens per layer, the files that add the most to every session, "
                + "the memory index of each project, and the broken links, not-yet-written memories or notes, orphan "
                + "files and frontmatter errors (each with its line in the file when that is known). For a vault or a "
                + "plain Markdown folder it also lists the tags and how many notes carry each (most used first), the "
                + "'recent' notes (written last, newest first, with 'modifiedAt' in UTC; at most 8), the 'mostLinked' "
                + "notes (the most backlinks first, with their 'count'; at most 8) and names an 'entry' note to start from "
                + "(the first note without a folder that is called home, index, readme, moc or start, in that order and "
                + "ignoring case; else, of the notes tagged moc or home, the one that links to the most other notes; left "
                + "out when there is none), and a note is an orphan only when it has no links at all: no note links to it "
                + "and it links to no other note. For a Claude Code folder 'tags', 'recent' and 'mostLinked' are empty and "
                + "there is no 'entry'.")
            .WithTags("Overview");
        return routes;
    }

    internal static Ok<OverviewResponse> GetOverview(IIndexProvider provider) =>
        TypedResults.Ok(OverviewMapper.ToResponse(provider.Current));
}
