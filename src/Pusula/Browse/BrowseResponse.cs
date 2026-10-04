namespace Pusula.Browse;

/// <summary>The folders inside one folder of this machine: what a page needs to let the user pick a folder to show.</summary>
/// <param name="Path">Full path of the folder that was listed: <c>~</c> expanded, <c>..</c> resolved, no separator at the end.</param>
/// <param name="Display">The path as a person writes it: <c>~</c> for the home directory, <c>~/...</c> below it, the full path anywhere else.</param>
/// <param name="Home">Full path of the home directory of the user the server runs as, where a page starts; the root of the file system when the user has none.</param>
/// <param name="Folders">The folders inside it (never a file), sorted by name: ignoring case, in Turkish alphabetical order. At most 500; <c>truncated</c> says when there are more.</param>
/// <param name="Truncated">True when the folder has more folders than are listed: the first 500 are.</param>
/// <param name="Parent">Full path of the folder above this one; left out at the root of the file system.</param>
public sealed record BrowseResponse(
    string Path,
    string Display,
    string Home,
    IReadOnlyList<BrowseFolder> Folders,
    bool Truncated,
    string? Parent = null);

/// <summary>One folder of a listing or of the folders that were found: its name and path, what it looks like and whether a source shows it already. Never a file name, never what is in a file.</summary>
/// <param name="Name">The name of the folder.</param>
/// <param name="Path">Full path of the folder.</param>
/// <param name="Display">The path as a person writes it: <c>~/...</c> below the home directory, the full path anywhere else.</param>
/// <param name="Listed">True when a source shows this very folder (the same full path); a folder above or below a source is not listed.</param>
/// <param name="Kind">What the folder looks like: <c>Vault</c> (it has a <c>.obsidian</c> directory), <c>Claude</c> (a Claude Code configuration folder, as a source that is left to "auto" would decide it) or <c>Notes</c> (a folder of linked notes without a <c>.obsidian</c> directory, recognized by its content; only among the folders that were found, never in a listing). Left out for any other folder.</param>
/// <param name="MarkdownCount">The number of Markdown files below the folder, at any depth, counted the way a source over the folder finds them: hidden folders and files are left out, and so is <c>node_modules</c> in a vault or a plain folder. In a Claude Code folder (<c>kind</c> Claude) the runtime folders directly in it (<c>plugins</c>, <c>sessions</c>, ...) are not entered and of <c>projects/&lt;project&gt;/</c> only <c>memory</c> is, which makes the number the file count of a source over that folder. At most 999, with <c>more</c> set when there are more; and one folder is read for at most 2,000 file system entries, a bigger one is counted as far as that, with <c>more</c> set. Left out for the folders that the budget of one request (about 20,000 file system entries or 400 ms) did not reach.</param>
/// <param name="More">True when the folder has more Markdown files than <c>markdownCount</c> says: the count stops at 999 files or at 2,000 entries read (so <c>markdownCount</c> can be less than 999, even 0). Left out otherwise.</param>
public sealed record BrowseFolder(
    string Name,
    string Path,
    string Display,
    bool Listed,
    FolderKind? Kind = null,
    int? MarkdownCount = null,
    bool? More = null);

/// <summary>The folders of this machine that look like something to show: Obsidian vaults, folders of linked notes that have no <c>.obsidian</c> directory but are recognized by their content, and the <c>.claude</c> folder of the home directory.</summary>
/// <param name="Folders">The folders that were found, sorted by <c>name</c> (ignoring case, in Turkish alphabetical order) and, for the same name, by <c>display</c>.</param>
/// <param name="Complete">False when the search stopped at its budget (about 1.5 seconds or 100,000 file system entries, the recognizing of folders of notes included) before it had looked everywhere: there may be more folders than are listed. Also false when a call of the file system did not return in time (a disk that sleeps, a network drive that does not answer): the folders are then the ones found so far, and have no <c>markdownCount</c>.</param>
public sealed record FoundResponse(IReadOnlyList<BrowseFolder> Folders, bool Complete);
