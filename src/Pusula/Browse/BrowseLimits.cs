namespace Pusula.Browse;

/// <summary>
/// How much the folder browser lists, reads and remembers, so that a folder far larger than anyone browses (a whole
/// drive, a folder with a million entries) cannot take all the time of the server. The extension point of the tests,
/// like <see cref="Pusula.Indexing.ScanLimits"/>: they give small limits and make no folder of 20,000 files.
/// </summary>
/// <param name="MaxFolders">The most folders one listing has; a folder that has more is cut and says so.</param>
/// <param name="MaxMarkdownFiles">The most Markdown files that are counted below one folder; a folder that has more is counted as this many and says so.</param>
/// <param name="CountEntries">The most file system entries that the counting of Markdown files reads in one request (one listing, or one search); the folders it does not reach get no count.</param>
/// <param name="CountEntriesPerFolder">The most file system entries that the counting reads for one folder; a folder that has more is counted as far as that and says so (<c>more</c>), so that one big folder does not use up what the others need. What it reads counts for <paramref name="CountEntries"/> too.</param>
/// <param name="CountTime">The longest the counting of Markdown files takes in one request; the folders it does not reach get no count.</param>
/// <param name="SearchEntries">The most file system entries that one search for vaults reads before it gives up.</param>
/// <param name="SearchTime">The longest one search for vaults takes before it gives up.</param>
/// <param name="CacheLifetime">How long the result of a search is remembered.</param>
internal sealed record BrowseLimits(
    int MaxFolders,
    int MaxMarkdownFiles,
    int CountEntries,
    int CountEntriesPerFolder,
    TimeSpan CountTime,
    int SearchEntries,
    TimeSpan SearchTime,
    TimeSpan CacheLifetime)
{
    /// <summary>The limits of the application: 500 folders, 999 Markdown files, 20,000 entries (2,000 of them for one folder) or 400 ms of counting, 100,000 entries or 1.5 seconds of searching, a search remembered for 60 seconds.</summary>
    public static BrowseLimits Default { get; } = new(
        MaxFolders: 500,
        MaxMarkdownFiles: 999,
        CountEntries: 20_000,
        CountEntriesPerFolder: 2_000,
        CountTime: TimeSpan.FromMilliseconds(400),
        SearchEntries: 100_000,
        SearchTime: TimeSpan.FromMilliseconds(1500),
        CacheLifetime: TimeSpan.FromSeconds(60));
}
