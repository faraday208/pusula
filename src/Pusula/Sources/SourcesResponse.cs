using Pusula.Indexing;

namespace Pusula.Sources;

/// <summary>The folders this server shows.</summary>
/// <param name="Sources">The sources, in the order they were configured.</param>
/// <param name="CanEdit">Whether this request may add and remove sources (<c>POST /api/sources</c>, <c>DELETE /api/sources/{id}</c>): only one from the machine the server runs on may (unless <c>remoteEdit</c> is true), and only while the list comes from a sources file. <c>editBlocked</c> says why not.</param>
/// <param name="Machine">The name of the machine the server runs on (the machine name of the operating system, such as the one a shell shows). A page says with it which browser can change the sources, when <c>remoteEdit</c> is false: the one on this machine.</param>
/// <param name="RemoteEdit">The setting <c>Pusula:AllowRemoteEdit</c>: true when a request from another machine may add and remove sources too (the list must still come from a sources file, and the request from a page of this server); false when only one from <c>machine</c> may.</param>
/// <param name="SourcesFile">Full path of the sources file the list was read from, or looked for and not found; left out when the folders were named on the command line or in <c>Pusula:Root</c>.</param>
/// <param name="EditBlocked">Why <c>canEdit</c> is false: <c>Remote</c> (the request is not from the machine the server runs on, or a reverse proxy forwarded it; never while <c>remoteEdit</c> is true) or <c>CommandLine</c> (the folders were named on the command line or in <c>Pusula:Root</c>); left out when <c>canEdit</c> is true.</param>
/// <param name="SourcesFileError">Why the sources file cannot be used, when it was changed after the server started into something that is not a valid list; the last valid list is still shown. Left out when the file is fine.</param>
public sealed record SourcesResponse(
    IReadOnlyList<SourceResponse> Sources,
    bool CanEdit,
    string Machine,
    bool RemoteEdit,
    string? SourcesFile = null,
    string? EditBlocked = null,
    string? SourcesFileError = null);

/// <summary>One folder this server shows.</summary>
/// <param name="Id">The part of the URL after <c>/api/sources/</c>: lowercase letters and digits joined by single hyphens.</param>
/// <param name="Name">The name to show for the source.</param>
/// <param name="Path">Full path of the folder.</param>
/// <param name="Profile">How the folder is scanned: <c>Claude</c> (a Claude Code configuration folder), <c>Vault</c> (an Obsidian vault) or <c>Markdown</c> (any other folder of Markdown files). Never "auto": it is what the folder turned out to be.</param>
/// <param name="Available">False when the folder could not be shown (it does not exist, cannot be read, or is too large); the endpoints of such a source answer 503.</param>
/// <param name="FileCount">The number of indexed Markdown files; 0 when the source is not available.</param>
/// <param name="Error">Why the source is not available, in an English sentence: the folder does not exist, cannot be read, or is too large to show (more than 20,000 Markdown files, 50,000 folders or 256 MiB of Markdown); left out when it is.</param>
/// <param name="ErrorCode">Why the source is not available, as a code to act on: <c>FolderMissing</c> (the folder does not exist, or is not a directory; the source starts when the folder appears), <c>NotReadable</c> (the folder cannot be read) or <c>TooLarge</c> (the folder is more than is shown). The endpoints of such a source answer 503 with the same value as the <c>code</c> of the problem details. Left out when the source is available.</param>
public sealed record SourceResponse(string Id, string Name, string Path, SourceProfile Profile, bool Available, int FileCount, string? Error = null, string? ErrorCode = null);
