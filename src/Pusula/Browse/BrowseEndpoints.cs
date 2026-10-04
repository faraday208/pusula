using Microsoft.AspNetCore.Http.HttpResults;
using Pusula.Sources;
using Pusula.Startup;

namespace Pusula.Browse;

/// <summary>
/// The <c>/api/browse</c> endpoints: the folder browser that adding a source is done with, so that a page lets the user
/// pick a folder instead of typing its path. Only folder names and counts leave the server: never the name or the
/// content of a file. Who may use them is decided by <see cref="BrowseFilter"/>.
/// </summary>
internal static class BrowseEndpoints
{
    /// <summary>Maps <c>GET /api/browse</c> and <c>GET /api/browse/found</c>.</summary>
    /// <param name="routes">The route builder.</param>
    public static IEndpointRouteBuilder MapBrowse(this IEndpointRouteBuilder routes)
    {
        RouteGroupBuilder browse = routes.MapGroup("/api/browse");
        browse.AddEndpointFilter<BrowseFilter>();
        browse.WithTags("Browse");
        browse.ProducesProblem(StatusCodes.Status403Forbidden);

        browse.MapGet("/", GetBrowse)
            .WithName("BrowseFolders")
            .WithSummary("List the folders inside a folder")
            .WithDescription(
                "Lists the folders inside a folder of this machine, so that a page can let the user pick the folder of a new "
                + "source instead of typing its path. 'path' is absolute or starts with '~' (the home directory), and is the "
                + "home directory when left out or blank. Only folders are listed: never the name or the content of a file. "
                + "Folders whose names start with '.' (on Windows also the folders with the Hidden or System attribute, such as "
                + "'AppData') are left out unless 'hidden=1' is given (the '.claude' folder of the home directory is always "
                + "listed), and 'node_modules' never is. They come sorted by name, ignoring case, in "
                + "Turkish alphabetical order; at most 500 are listed, and 'truncated' says there are more. 'display' is the "
                + "path as a person writes it: '~/...' below the home directory, the full path elsewhere. 'parent' is the folder "
                + "above (left out at the root of the file system) and 'home' the home directory, where a page starts. Of each "
                + "folder: 'kind' is Vault (it has a '.obsidian' directory) or Claude (a Claude Code configuration folder, as the "
                + "'auto' profile of a source would decide), and is left out for any other folder; 'markdownCount' is the number of "
                + "Markdown files below it, at any depth, counted the way a source over the folder finds them (hidden folders and files "
                + "are not counted; 'node_modules' neither in a vault or a plain folder; in a Claude folder the runtime folders directly "
                + "in it, such as 'plugins' and 'sessions', are not entered and of 'projects/<project>/' only 'memory' is, so the number "
                + "is the file count of a source over it), at most 999, with 'more' true when there are more. One folder is read for at "
                + "most 2,000 file system entries: a bigger one is counted as far as that, with 'more' true (the count can then be "
                + "less than 999, even 0). 'listed' says whether a source shows this very folder already. The counts are given as "
                + "far as the budget of one request reaches (about 20,000 file system entries or 400 ms, the folders in the order of "
                + "the list): the folders after that have no 'markdownCount'. A folder that cannot be read "
                + "has no folders to list. Nothing is written. Who may ask is who may add a source (POST /api/sources): only a request "
                + "from the machine the server runs on (not one that a reverse proxy forwarded: a Forwarded, X-Forwarded-For, "
                + "X-Forwarded-Host or X-Real-IP header makes it Remote), made by a page of this server or by hand (its Origin "
                + "header, when it has one, must be this server's own, and its Sec-Fetch-Site header, when it has one, must say "
                + "same-origin or none), and only while the sources come from a sources file and not from the command line or "
                + "Pusula:Root. The setting Pusula:AllowRemoteEdit lifts the first condition and only that one (so anyone who can "
                + "reach the server can then read the names of the folders of this machine). Every error is problem details whose "
                + "'code' says what is wrong: 400 PathNotAbsolute (also a path with a character that no path has), 404 "
                + "FolderNotFound (the folder does not exist or is not a directory); 403 Remote, CommandLine, CrossOrigin.")
            .Produces<BrowseResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status404NotFound);

        browse.MapGet("/found", GetFound)
            .WithName("FindFolders")
            .WithSummary("Find the vaults and folders of notes of this machine")
            .WithDescription(
                "Looks for the folders that are likely to be wanted as a source: Obsidian vaults (folders with a '.obsidian' "
                + "directory; kind Vault), folders of linked notes that have no '.obsidian' directory but are recognized by their "
                + "content (kind Notes) and the '.claude' folder of the home directory. It looks in the home directory and below "
                + "/mnt and /media (down to 4 levels each) and below /Volumes on macOS (3 levels); on Windows it looks in the "
                + "root of every ready fixed or removable drive instead (down to 4 levels each; not into the Windows, Program "
                + "Files, Program Files (x86), ProgramData, Users, PerfLogs, Recovery and System Volume Information folders at "
                + "the top of a drive, nor into the ones whose names start with '$'). It does not go into hidden folders "
                + "(names that start with '.', and on Windows folders with the Hidden or System attribute, such as 'AppData'), "
                + "'node_modules', 'bin' or 'obj', nor into a vault. "
                + "A folder without a '.obsidian' directory is listed as a folder of notes when all of these hold: it has an entry "
                + "note of its own (Home.md, index.md, README.md, MOC.md, _index.md or start.md, in any case); in it and up to 3 "
                + "levels below it there are at least 10 Markdown files, and they are at least half of the files there (hidden "
                + "files are not counted, and the folders that are not entered are not read); and at least a fifth of a sample of "
                + "those notes have a wikilink ('[[' in the first 4 KB of the note; the sample is at most 20 notes, spread over the "
                + "folder; they are read on the server, and nothing of them leaves it). When a vault or another folder of notes lies "
                + "inside a folder of notes and holds at least 60% of its wikilinked notes (estimated as the share of the sample "
                + "that has a wikilink times the number of notes), only the one inside is listed: a project folder with a README and "
                + "a vault in it is listed as the vault. A vault is never left out. The folders that the search starts from are never "
                + "listed. A folder of notes is found as deep as a vault is (4 levels) under the home directory and, on Windows, on "
                + "the system drive (the drive of the Windows folder): the search does not read the folders of the last level, so each "
                + "of those is asked for an entry note by reading its first 200 entries, in the order of the file system, and one that "
                + "has none among them is taken to have none (a choice: a folder of notes is small near its root, and no more is read of "
                + "a folder of thousands of files). Below /mnt, /media and /Volumes and on the other drives of Windows the folders of the "
                + "last level are not asked: an external disk may sleep or be slow, so that one folder takes seconds to open and holds the "
                + "whole search back, and the folders of notes that were measured at the last level were all under the home directory. "
                + "A folder of notes there is found down to one level above the last (3 levels, 2 below /Volumes); a vault is found at "
                + "every level. "
                + "The search stops after about 1.5 seconds or 100,000 file system entries: 'complete' is then false, and there may be "
                + "more folders than are listed. The vaults are looked for first; the folders of notes are looked into with what is "
                + "left of that budget, the shallowest first and at most 2,000 entries for one folder, and one that was not looked into "
                + "is not listed. The folders of the last level are asked after the others, at one entry for the folder and one for "
                + "each entry read (the ones inside a folder of notes first), and one that was not asked is not listed. Every entry "
                + "that the search reads is taken from the budget, so that the number of entries and the time both stop it at any point. "
                + "A file system that does not answer (a disk that sleeps, a network drive that is gone) does not hold the request: it waits "
                + "for the search at most 250 ms longer than the 1.5 seconds, and then for the counting of the folders found at most 250 ms "
                + "longer than its 400 ms. If a call of the file system has not returned by then, the request is answered with the folders "
                + "that the search had found so far (every vault the walk had met, the folders of notes of the stages that were over), with "
                + "'complete' false and without 'markdownCount' (they were not counted); the search goes on until the call returns, what it "
                + "finds then is thrown away, and no other search starts "
                + "meanwhile (every request is answered with those same folders). Such an answer is not remembered: the first request after "
                + "the file system answers again searches anew. "
                + "The result of a search that ended is remembered in memory for 60 seconds; 'listed' always says what the sources are now. "
                + "The folders come sorted by 'name' (ignoring case, in Turkish alphabetical order) and, for the same name, by "
                + "'display', in the form of the folders of GET /api/browse ('kind', 'markdownCount', 'more' and 'listed' are told the "
                + "same way, 'markdownCount' with the same budget and the same limit for one folder; 'kind' is Notes only here, and a "
                + "folder of notes that a source left to 'auto' would take for a Claude Code folder is Claude). Nothing is written. "
                + "Who may ask, and the problem details of a refusal (403 Remote, CommandLine, CrossOrigin), are those of GET /api/browse.")
            .Produces<FoundResponse>(StatusCodes.Status200OK);

        return routes;
    }

    /// <summary>List the folders inside a folder</summary>
    /// <param name="path">The folder: absolute or starting with <c>~</c>; the home directory when left out.</param>
    /// <param name="hidden"><c>1</c> to list the hidden folders too.</param>
    /// <param name="registry">The sources, to tell which folders are shown already.</param>
    /// <param name="directories">The directories of the user.</param>
    /// <param name="limits">How much a listing lists and reads.</param>
    /// <param name="cancellationToken">Set when the request is gone.</param>
    internal static Results<Ok<BrowseResponse>, ProblemHttpResult> GetBrowse(
        string? path,
        string? hidden,
        ISourceRegistry registry,
        UserDirectories directories,
        BrowseLimits limits,
        CancellationToken cancellationToken)
    {
        if (!BrowsePaths.TryResolve(path, directories, out string? folder, out BrowseError? error))
        {
            return BrowseProblems.For(error.Value);
        }

        string? home = BrowsePaths.HomeFolder(directories);
        bool includeHidden = hidden is "1" || string.Equals(hidden, "true", StringComparison.OrdinalIgnoreCase);
        FolderListing listing = FolderLister.List(folder, includeHidden, home, new ListedFolders(registry), limits, cancellationToken);

        return TypedResults.Ok(new BrowseResponse(
            folder,
            BrowsePaths.Display(folder, home),
            home ?? BrowsePaths.StartFolder(directories),
            listing.Folders,
            listing.Truncated,
            BrowsePaths.Parent(folder)));
    }

    /// <summary>Find the vaults and folders of notes of this machine</summary>
    /// <param name="registry">The sources, to tell which folders are shown already.</param>
    /// <param name="finder">The search, which remembers its result.</param>
    /// <param name="cancellationToken">Set when the request is gone.</param>
    internal static Ok<FoundResponse> GetFound(ISourceRegistry registry, FolderFinder finder, CancellationToken cancellationToken)
    {
        FoundFolders found = finder.Find(cancellationToken);

        // Whether a source shows a folder is not part of what is remembered: it is what the sources are now.
        var listed = new ListedFolders(registry);
        return TypedResults.Ok(new FoundResponse(
            [.. found.Folders.Select(folder => folder with { Listed = listed.Contains(folder.Path) })],
            found.Complete));
    }
}
