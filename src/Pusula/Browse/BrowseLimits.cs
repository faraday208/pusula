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
/// <param name="SearchEntries">The most file system entries that one search for vaults reads before it gives up. The look into the folders that may be folders of notes, and the asking of the folders at the last level of the search for an entry note, come after the search of the disk and read from the same budget.</param>
/// <param name="SearchTime">The longest one search for vaults takes before it gives up, the look into the folders that may be folders of notes included.</param>
/// <param name="CheckEntriesPerFolder">The most file system entries that the look into one folder reads below it, to see whether it is a folder of notes: a folder that has more is judged by what was read, so that one big folder (a project with a README and a hundred thousand files) does not use up what the others need. What it reads counts for <paramref name="SearchEntries"/> too.</param>
/// <param name="CacheLifetime">How long the result of a search is remembered.</param>
/// <param name="DeadlineMargin">How long a request waits for the file system past the time limit of the part of the search that is running (<paramref name="SearchTime"/> for the search of the disk, <paramref name="CountTime"/> for the counting of the folders found that follows it): a call of the file system that has not returned by then (a disk that sleeps, a network drive that does not answer) does not hold the request any longer, which is answered with the folders found so far and says it is not complete.</param>
internal sealed record BrowseLimits(
    int MaxFolders,
    int MaxMarkdownFiles,
    int CountEntries,
    int CountEntriesPerFolder,
    TimeSpan CountTime,
    int SearchEntries,
    TimeSpan SearchTime,
    int CheckEntriesPerFolder,
    TimeSpan CacheLifetime,
    TimeSpan DeadlineMargin)
{
    /// <summary>The limits of the application: 500 folders, 999 Markdown files, 20,000 entries (2,000 of them for one folder) or 400 ms of counting, 100,000 entries or 1.5 seconds of searching (2,000 entries of them for the look into one folder of notes), a search remembered for 60 seconds, a request that waits 250 ms longer than the time limit of the search, and then of the counting, for a file system that does not answer.</summary>
    public static BrowseLimits Default { get; } = new(
        MaxFolders: 500,
        MaxMarkdownFiles: 999,
        CountEntries: 20_000,
        CountEntriesPerFolder: 2_000,
        CountTime: TimeSpan.FromMilliseconds(400),
        SearchEntries: 100_000,
        SearchTime: TimeSpan.FromMilliseconds(1500),
        CheckEntriesPerFolder: 2_000,
        CacheLifetime: TimeSpan.FromSeconds(60),
        DeadlineMargin: TimeSpan.FromMilliseconds(250));
}
