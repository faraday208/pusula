using Pusula.Indexing;
using Pusula.Sources;

namespace Pusula.Browse;

/// <summary>
/// Lists the folders inside a folder and says what each of them looks like. Only folders: the name or the content of a
/// file never leaves this class. The folder is only ever read.
/// </summary>
internal static class FolderLister
{
    /// <summary>
    /// Lists the folders inside <paramref name="folder"/>, sorted (see <see cref="FolderNames.Compare"/>) and cut at the
    /// limit. Hidden folders (names that start with a dot and, on Windows, folders with the Hidden or System attribute, such
    /// as <c>AppData</c>: see <see cref="FolderNames.IsHidden(DirectoryInfo)"/>) are left out unless
    /// <paramref name="includeHidden"/> says otherwise, except the <c>.claude</c> of the home directory, and
    /// <c>node_modules</c> is never listed. A folder that cannot be read has no folders to list. Each folder is described by
    /// <see cref="Describe"/>, with the Markdown count that the budget of the limits allows for the whole listing.
    /// </summary>
    /// <param name="folder">Full path of the folder, which exists.</param>
    /// <param name="includeHidden">Whether the hidden folders are listed.</param>
    /// <param name="home">Full path of the home directory; null when there is none.</param>
    /// <param name="listed">The folders that sources show now.</param>
    /// <param name="limits">How many folders to list and how much to read for the counts.</param>
    /// <param name="cancellationToken">Stops the listing when the request is gone.</param>
    public static FolderListing List(string folder, bool includeHidden, string? home, ListedFolders listed, BrowseLimits limits, CancellationToken cancellationToken)
    {
        bool isHome = home is not null && SourcePaths.Same(folder, home);

        var names = new List<string>();
        try
        {
            foreach (DirectoryInfo directory in FolderWalk.Folders(folder))
            {
                if (IsListed(directory, includeHidden, isHome))
                {
                    names.Add(directory.Name);
                }
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // A folder that cannot be read has no folders to list; for one that went away while it was listed, the names read so far stay.
        }

        names.Sort(FolderNames.Compare);

        var budget = new ScanBudget(limits.CountEntries, limits.CountTime);
        var folders = new List<BrowseFolder>(Math.Min(names.Count, limits.MaxFolders));
        foreach (string name in names.Take(limits.MaxFolders))
        {
            cancellationToken.ThrowIfCancellationRequested();
            string path = Path.Join(folder, name);
            folders.Add(Describe(path, home, limits, budget, cancellationToken) with { Listed = listed.Contains(path) });
        }

        return new FolderListing(folders, Truncated: names.Count > limits.MaxFolders);
    }

    /// <summary>
    /// Says what a folder looks like: its name, the path as a person writes it, its kind (a vault, a Claude Code folder, or
    /// neither) and the number of Markdown files below it, when the budget has that much left. The files are counted the
    /// way a source over the folder would find them (see <see cref="MarkdownCounter"/>): the kind is the profile that a
    /// source left to "auto" would get. Whether a source shows the folder is not known here: <see cref="BrowseFolder.Listed"/> is false.
    /// </summary>
    /// <param name="path">Full path of the folder.</param>
    /// <param name="home">Full path of the home directory; null when there is none.</param>
    /// <param name="limits">How many Markdown files to count at most, and how many entries one folder may take.</param>
    /// <param name="budget">What the counting may still read.</param>
    /// <param name="cancellationToken">Stops the counting when the request is gone.</param>
    public static BrowseFolder Describe(string path, string? home, BrowseLimits limits, ScanBudget budget, CancellationToken cancellationToken)
    {
        // A folder that is a link to a network location is not looked into: what it looks like is what is in it, and that is not read (see LinkGuard).
        if (!LinkGuard.MayFollow(new DirectoryInfo(path)))
        {
            return new BrowseFolder(SourceIds.NameOf(path), path, BrowsePaths.Display(path, home), Listed: false);
        }

        // "Markdown" is every folder that is neither a vault nor a Claude Code folder: it has no kind.
        SourceProfile profile = SourceProfiles.Detect(path);
        MarkdownCount? count = MarkdownCounter.Count(path, profile, limits.MaxMarkdownFiles, limits.CountEntriesPerFolder, budget, cancellationToken);
        return new BrowseFolder(
            SourceIds.NameOf(path),
            path,
            BrowsePaths.Display(path, home),
            Listed: false,
            KindOf(profile),
            count?.Count,
            count is { More: true } ? true : null);
    }

    // The kinds that a listing knows: a vault and a Claude Code folder. Any other folder has none (a folder of notes that is recognized by
    // its content gets its kind from the search for folders, which has read what is in it).
    private static FolderKind? KindOf(SourceProfile profile) => profile switch
    {
        SourceProfile.Vault => FolderKind.Vault,
        SourceProfile.Claude => FolderKind.Claude,
        _ => null,
    };

    // Hidden folders are left out unless asked for: but the .claude of the home directory is the one that is wanted most.
    private static bool IsListed(DirectoryInfo directory, bool includeHidden, bool isHome)
    {
        // A link that leads to a network location is not followed, and so not listed (see LinkGuard): to describe it is to open it.
        if (FolderNames.IsNodeModules(directory.Name) || !LinkGuard.MayFollow(directory))
        {
            return false;
        }

        return !FolderNames.IsHidden(directory) || includeHidden || (isHome && FolderNames.IsClaudeFolder(directory.Name));
    }
}
