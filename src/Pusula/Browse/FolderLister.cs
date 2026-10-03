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
    /// limit. Hidden folders (names that start with a dot) are left out unless <paramref name="includeHidden"/> says
    /// otherwise, except the <c>.claude</c> of the home directory, and <c>node_modules</c> is never listed. A folder that
    /// cannot be read has no folders to list. Each folder is described by <see cref="Describe"/>, with the Markdown count
    /// that the budget of the limits allows for the whole listing.
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
                if (IsListed(directory.Name, includeHidden, isHome))
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
        // "Markdown" is every folder that is neither a vault nor a Claude Code folder: it has no kind.
        SourceProfile profile = SourceProfiles.Detect(path);
        MarkdownCount? count = MarkdownCounter.Count(path, profile, limits.MaxMarkdownFiles, limits.CountEntriesPerFolder, budget, cancellationToken);
        return new BrowseFolder(
            SourceIds.NameOf(path),
            path,
            BrowsePaths.Display(path, home),
            Listed: false,
            profile == SourceProfile.Markdown ? null : profile,
            count?.Count,
            count is { More: true } ? true : null);
    }

    // Hidden folders are left out unless asked for: but the .claude of the home directory is the one that is wanted most.
    private static bool IsListed(string name, bool includeHidden, bool isHome)
    {
        if (FolderNames.IsNodeModules(name))
        {
            return false;
        }

        return !FolderNames.IsHidden(name) || includeHidden || (isHome && FolderNames.IsClaudeFolder(name));
    }
}
