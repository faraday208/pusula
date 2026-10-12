using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Http.HttpResults;
using Pusula.Indexing;
using Pusula.Sources;

namespace Pusula.History;

/// <summary>The <c>/api/sources/{source}/changes</c> endpoint.</summary>
internal static class HistoryEndpoints
{
    /// <summary>Maps <c>GET changes</c> onto the route group of a source (see <c>SourceEndpoints.MapSources</c>).</summary>
    /// <param name="routes">The route group <c>/api/sources/{source}</c>.</param>
    public static IEndpointRouteBuilder MapChanges(this IEndpointRouteBuilder routes)
    {
        routes.MapGet("/changes", (HttpContext http, HistoryStore store, ISourceRegistry registry) =>
                GetChanges(http.Features.GetRequiredFeature<IIndexProvider>(), store, registry))
            .WithName("GetChanges")
            .WithSummary("Get what changed")
            .WithDescription(
                "Tells what changed in the source since an earlier day: the files that were added, removed or changed in "
                + "size (in estimated tokens), and the links that became broken or stopped being broken. For that pusula keeps "
                + "a daily summary of every source: the first time this endpoint is asked for a source on a day (the overview "
                + "page asks every time it is shown), it takes the summary of that day, which holds for every indexed file its "
                + "estimated tokens in all and in every session, and the broken links (the file, the kind and the text of each, "
                + "not the line). The day is the local calendar day of the machine the server runs on, and the summary of a day "
                + "is never replaced later that day. The summaries are kept in pusula's own state folder, never in a folder that "
                + "is shown: $XDG_STATE_HOME/pusula/history (when XDG_STATE_HOME is an absolute path) or "
                + "~/.local/state/pusula/history on Linux and macOS, %LOCALAPPDATA%\\pusula\\history on Windows, in one JSON "
                + "file for each source (its folder and profile). Every summary of the last seven days is kept, and of the older "
                + "ones only the newest. 'previousDay' compares the source as it is now with the newest summary of an earlier day "
                + "(a day on which pusula was not opened is skipped), 'previousWeek' with the newest summary that is seven days "
                + "old or older; each is left out when there is no such summary, and today's own summary is never compared with. "
                + "'firstDay' is the day of the oldest summary (today's included once it is kept); it is left out when there is "
                + "none. 'history' says whether today's summary is kept: Kept; Off (pusula has no state folder to keep summaries "
                + "in, because the home directory is not known: nothing is written and there is nothing to compare with); "
                + "InsideSource (the state folder is inside a folder that is shown, and pusula never writes there: today's "
                + "summary was not written, and older ones are still compared); WriteFailed (writing today's summary failed: "
                + "'error' says why, older summaries are still compared, and the next request tries again). Each comparison has "
                + "the 'day' and 'takenAt' of the summary, the number of files ('filesBefore', 'filesAfter') and the tokens that "
                + "are loaded at the start of every session ('everySessionTokensBefore', 'everySessionTokensAfter'), and these "
                + "lists, none of them capped: 'added' (files that are in the source now and not in the summary, with their "
                + "current tokens, everySessionTokens and loadMode), 'removed' (files that are in the summary and not in the "
                + "source now, with the values of the summary and no loadMode), 'resized' (files in both whose tokens or "
                + "everySessionTokens differ, with both values and the current loadMode; a file whose text changed but whose "
                + "token counts did not is not listed), 'newlyBroken' (links that are broken now and were not in the summary, "
                + "with the line of their first occurrence now) and 'noLongerBroken' (links that were broken in the summary and "
                + "are not broken now: fixed, removed, or their file is gone; they have no line). A broken link is told apart by "
                + "its file, its kind and its text, not by its line, so a link that only moved down is not new. 'added' and "
                + "'removed' come with the file that adds the most to every session first (then the largest, then by path), "
                + "'resized' with the biggest change in everySessionTokens first (then in tokens, then by path), 'newlyBroken' "
                + "by file and line, 'noLongerBroken' by file, kind (by its name) and text; files and texts are compared by "
                + "character code, not by alphabet. For a vault or a plain Markdown folder the tokens in every session are all 0.")
            .WithTags("Overview");
        return routes;
    }

    internal static Ok<ChangesResponse> GetChanges(IIndexProvider provider, HistoryStore store, ISourceRegistry registry) =>
        TypedResults.Ok(store.Changes(provider.Current, [.. registry.Sources.Select(source => source.Definition.Path)]));
}
