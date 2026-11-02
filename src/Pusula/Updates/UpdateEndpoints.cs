using Microsoft.AspNetCore.Http.HttpResults;

namespace Pusula.Updates;

/// <summary>The <c>/api/update</c> endpoint.</summary>
internal static class UpdateEndpoints
{
    /// <summary>Maps <c>GET /api/update</c>.</summary>
    /// <param name="routes">The route builder.</param>
    public static IEndpointRouteBuilder MapUpdate(this IEndpointRouteBuilder routes)
    {
        routes.MapGet("/api/update", (UpdateNotice notice) => GetUpdate(notice))
            .WithName("GetUpdate")
            .WithSummary("Get whether a newer pusula is out")
            .WithDescription(
                "Tells what the check for a newer release has found. When pusula is started from the download, and then once a day while it "
                + "runs, it asks GitHub (api.github.com) for the latest release of pusula (drafts and pre-releases are left out); only that "
                + "request is made, and nothing is downloaded or installed. 'state' is Off (the check is turned off: "
                + "--Pusula:CheckForUpdates false, or this is a 'dotnet run' build, where it is off unless the setting says true), "
                + "NotChecked (the first check has not found an answer yet), UpToDate (the latest release is this version or an older one; "
                + "also when the version of this pusula cannot be compared), Newer (a newer release is out) or Failed (the check failed and "
                + "there is no earlier answer; the next one is a day later). 'current' is the version of this pusula. When a check has found "
                + "a release, 'latest' is its version (without the 'v' of the tag) and 'url' its page on GitHub, which pusula makes from the "
                + "tag itself and does not take from the answer of GitHub; 'checkedAt' says when. A check that fails after an earlier answer "
                + "leaves that answer as it was. Nothing is kept in a file: a new start begins at NotChecked. The answer holds nothing about "
                + "the folders that are shown, so any machine that passes the host check may ask.")
            .WithTags("Update");
        return routes;
    }

    internal static Ok<UpdateResponse> GetUpdate(UpdateNotice notice) => TypedResults.Ok(notice.Snapshot());
}
