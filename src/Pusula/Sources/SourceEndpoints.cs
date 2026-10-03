using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.Extensions.Options;
using Pusula.Indexing;
using Pusula.Startup;

namespace Pusula.Sources;

/// <summary>
/// The <c>/api/sources</c> endpoints: the list of sources, adding a source (<c>POST</c>) and removing one
/// (<c>DELETE /api/sources/{id}</c>); and the route group <c>/api/sources/{source}</c> that every other endpoint lives in.
/// </summary>
internal static class SourceEndpoints
{
    /// <summary>
    /// Maps <c>GET</c> and <c>POST /api/sources</c>, <c>DELETE /api/sources/{id}</c> and creates the group
    /// <c>/api/sources/{source}</c>. The group answers 404 for an id that no source has and 503 (with the
    /// <see cref="SourceErrorCode"/> as the <c>code</c> of the problem details) for a source that is not
    /// available; for any other it puts the index of the source where the endpoints of the group find it: the
    /// <see cref="IIndexProvider"/> feature of the request. Removing a source is mapped outside of the group, so that
    /// a source that is not available can be removed too.
    /// </summary>
    /// <param name="routes">The route builder.</param>
    /// <returns>The group; the endpoints of a source are mapped onto it.</returns>
    public static RouteGroupBuilder MapSources(this IEndpointRouteBuilder routes)
    {
        routes.MapGet("/api/sources", GetSources)
            .WithName("GetSources")
            .WithSummary("List the sources")
            .WithDescription(
                "Lists the folders this server shows, in the order they were configured: their id (the part of the URL "
                + "after /api/sources/), name, full path, profile (Claude, Vault or Markdown, as the folder turned out to "
                + "be), whether the folder could be read, the number of indexed Markdown files and, for a folder that "
                + "could not be shown, why: 'error' says it in an English sentence (it does not exist, cannot be read, or is "
                + "too large: more than 20,000 Markdown files, 50,000 folders or 256 MiB of Markdown) and 'errorCode' as a "
                + "code to act on: FolderMissing, NotReadable or TooLarge. The endpoints of such a source answer 503 with the "
                + "same code as the 'code' of their problem details. 'sourcesFile' is the sources file the list was read from "
                + "(or looked for, when there was none); it is left out when the folders were named on the command line. "
                + "'machine' is the name of the machine the server runs on. 'canEdit' says "
                + "whether this request may add and remove sources (POST /api/sources, DELETE /api/sources/{id}): only a "
                + "request from the machine the server runs on may (one that a reverse proxy forwarded, with a Forwarded, "
                + "X-Forwarded-For, X-Forwarded-Host or X-Real-IP header, does not count), unless the setting "
                + "Pusula:AllowRemoteEdit is on ('remoteEdit' says whether it is: then a request from another machine may "
                + "too), and only while the list comes "
                + "from a sources file; 'editBlocked' then says why not (Remote, or CommandLine). 'sourcesFileError' is there when the "
                + "sources file was changed, while the server runs, into something that is not a valid list: the last "
                + "valid list is still shown.")
            .WithTags("Sources");

        routes.MapPost("/api/sources", AddSource)
            .AddEndpointFilter<SourceEditFilter>()
            .WithName("AddSource")
            .WithSummary("Add a source")
            .WithDescription(
                "Adds a folder to the sources file and starts showing it. 'path' is absolute or starts with '~' (the home "
                + "directory); 'name' is the name of the folder when left out; 'profile' is auto (the default), claude, "
                + "vault or markdown. Only a request from the machine the server runs on (not one that a reverse proxy "
                + "forwarded: a Forwarded, X-Forwarded-For, X-Forwarded-Host or X-Real-IP header makes it Remote), made by a "
                + "page of this server (its Origin and Sec-Fetch-Site headers, when it has them, must say so), is accepted, and only while the "
                + "sources come from a sources file and not from the command line or Pusula:Root. The setting "
                + "Pusula:AllowRemoteEdit lifts the first condition and only that one: a request from another machine is "
                + "accepted too (so anyone who can reach the server can show any folder of this machine, and read its "
                + "Markdown), while the page and the sources file are asked for all the same. The file is written out "
                + "again as a whole: the other items stay as they are, comments in it are lost. 201 carries the new "
                + "source, with its first index built, and a Location header with /api/sources/{id}, which DELETE "
                + "removes; a folder that is too large to show is added all the same, with 'available' false and the reason "
                + "in 'error' (and 'errorCode'). Every error is problem details whose 'code' says what is wrong: 400 PathRequired, "
                + "PathNotAbsolute, FolderNotFound (a path with a character that no path has is a folder that is not found), "
                + "TooBroad (the root of the file system and the home directory itself "
                + "cannot be a source), InvalidName, InvalidProfile; 403 Remote, CommandLine, CrossOrigin; 409 "
                + "AlreadyListed, FileInvalid (the sources file cannot be read: it is left alone); 500 WriteFailed.")
            .WithTags("Sources")
            .Produces<SourceResponse>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status500InternalServerError);

        routes.MapDelete("/api/sources/{id}", RemoveSource)
            .AddEndpointFilter<SourceEditFilter>()
            .WithName("RemoveSource")
            .WithSummary("Remove a source")
            .WithDescription(
                "Takes the source out of the sources file and stops showing it: its event streams end. The folder itself "
                + "is not touched. A source that is not available can be removed too. Accepted under the same conditions "
                + "as adding a source (Pusula:AllowRemoteEdit included). 204 has no body. Every error is problem details whose 'code' says what is wrong: "
                + "403 Remote, CommandLine, CrossOrigin; 404 NotFound (the sources file lists no source with this id); "
                + "409 FileInvalid (the sources file cannot be read: it is left alone); 500 WriteFailed.")
            .WithTags("Sources")
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status500InternalServerError);

        RouteGroupBuilder source = routes.MapGroup("/api/sources/{source}");
        source.AddEndpointFilter<SourceFilter>();
        source.ProducesProblem(StatusCodes.Status404NotFound);
        source.ProducesProblem(StatusCodes.Status503ServiceUnavailable);
        return source;
    }

    internal static Ok<SourcesResponse> GetSources(ISourceRegistry registry, IOptions<PusulaOptions> options, HttpContext http)
    {
        bool remoteEdit = options.Value.AllowRemoteEdit;
        EditBlock? blocked = SourceEditAccess.Blocked(http, registry.SourcesFile, remoteEdit);
        return TypedResults.Ok(new SourcesResponse(
            [.. registry.Sources.Select(ToResponse)],
            CanEdit: blocked is null,
            Environment.MachineName,
            remoteEdit,
            registry.SourcesFile,
            blocked?.ToString(),
            registry.SourcesFileError));
    }

    internal static async Task<Results<Created<SourceResponse>, ProblemHttpResult>> AddSource(
        CreateSourceRequest? request,
        ISourceEditor editor,
        UserDirectories directories,
        CancellationToken cancellationToken)
    {
        if (!SourceRequestValidator.TryValidate(request, directories.Home, out NewSource? source, out EditError? invalid))
        {
            return SourceProblems.For(invalid.Value);
        }

        SourceEditResult result = await editor.AddAsync(source, cancellationToken);
        if (result.Error is { } failure)
        {
            return SourceProblems.For(failure, result.Reason);
        }

        SourceEntry added = result.Entry!;
        return TypedResults.Created($"/api/sources/{added.Definition.Id}", ToResponse(added));
    }

    internal static async Task<Results<NoContent, ProblemHttpResult>> RemoveSource(string id, ISourceEditor editor, CancellationToken cancellationToken)
    {
        SourceEditResult result = await editor.RemoveAsync(id, cancellationToken);
        if (result.Error is { } failure)
        {
            return SourceProblems.For(failure, result.Reason);
        }

        return TypedResults.NoContent();
    }

    private static SourceResponse ToResponse(SourceEntry entry) =>
        new(
            entry.Definition.Id,
            entry.Definition.Name,
            entry.Definition.Path,
            entry.Definition.Profile,
            entry.IsAvailable,
            entry.Index?.Current.Files.Count ?? 0,
            entry.Error,
            entry.ErrorCode?.ToString());
}
