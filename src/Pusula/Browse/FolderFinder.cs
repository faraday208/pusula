using System.Diagnostics;
using Pusula.Indexing;
using Pusula.Sources;
using Pusula.Startup;

namespace Pusula.Browse;

/// <summary>
/// Looks for the folders that a user is likely to want to show: Obsidian vaults (a folder with a <c>.obsidian</c>
/// directory) in the home directory and the drive folders (<see cref="DriveFolders"/>: <c>/mnt</c> and <c>/media</c>, the
/// drives themselves on Windows), and the <c>.claude</c> folder of the home directory. The search goes level by level, so
/// that what is close to a root is found before what is deep down. It does not go into hidden folders (see
/// <see cref="FolderNames.IsHidden(DirectoryInfo)"/>: on Windows that includes the ones with the Hidden or System attribute,
/// such as <c>AppData</c>), <c>node_modules</c>, <c>bin</c> and <c>obj</c>, nor into a vault, nor at the top of a root into
/// the folders the root skips (<see cref="SearchRoot.SkippedAtTop"/>: the system folders of a Windows drive); it follows a
/// symbolic link once; and it stops at the budget of <see cref="BrowseLimits"/>, which makes
/// <see cref="FoundFolders.Complete"/> false. The result is remembered for <see cref="BrowseLimits.CacheLifetime"/>.
/// Folders are only ever read.
/// </summary>
internal sealed partial class FolderFinder(UserDirectories directories, DriveFolders drives, BrowseLimits limits, TimeProvider time, ILogger<FolderFinder> logger)
{
    /// <summary>How many levels below the home directory the search looks.</summary>
    internal const int HomeDepth = 4;

    private const string ObsidianDirectory = ".obsidian";

    // Folders of a project that hold what is built or fetched and not notes; the hidden ones (.git among them) are never entered anyway.
    private static readonly string[] SkippedFolders = [IndexBuilder.NodeModulesDirectory, "bin", "obj"];

    private readonly object _gate = new();
    private Remembered? _remembered;

    /// <summary>
    /// Gives the folders that were found: the result of the last search while it is not older than the cache lifetime,
    /// otherwise the result of a new search. One search runs at a time: a request that comes while it runs waits for it and
    /// shares its result.
    /// </summary>
    /// <param name="cancellationToken">Stops a search that this request started when the request is gone; nothing is remembered then.</param>
    public FoundFolders Find(CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            if (Recall() is { } recalled)
            {
                return recalled;
            }

            FoundFolders found = Search(cancellationToken);
            _remembered = new Remembered(found, time.GetUtcNow());
            return found;
        }
    }

    // The gate is held.
    private FoundFolders? Recall() =>
        _remembered is { } remembered && time.GetUtcNow() - remembered.At < limits.CacheLifetime ? remembered.Found : null;

    private FoundFolders Search(CancellationToken cancellationToken)
    {
        long started = Stopwatch.GetTimestamp();
        string? home = BrowsePaths.HomeFolder(directories);
        var budget = new ScanBudget(limits.SearchEntries, limits.SearchTime);
        List<string> found = [];

        // The folder of Claude Code is not looked for: it is hidden, which no search goes into, so it is asked for by name.
        string? claude = home is null ? null : Path.Join(home, FolderNames.ClaudeFolder);
        if (claude is not null && Directory.Exists(claude))
        {
            found.Add(claude);
        }

        Walk(home, budget, found, cancellationToken);

        // Sorted by name (the Turkish alphabet, ignoring case) and, for the same name, by display; counted in that order, which is the way a page shows
        // them (but for the folders a source shows already, which it moves to the end): what the budget of the counting does not reach is what is
        // further down.
        var counting = new ScanBudget(limits.CountEntries, limits.CountTime);
        BrowseFolder[] folders =
        [
            .. found
                .Select(path => (Path: path, Name: SourceIds.NameOf(path), Display: BrowsePaths.Display(path, home)))
                .OrderBy(folder => folder.Name, Comparer<string>.Create(FolderNames.CompareIgnoringCase))
                .ThenBy(folder => folder.Display, Comparer<string>.Create(FolderNames.Compare))
                .Select(folder => FolderLister.Describe(folder.Path, home, limits, counting, cancellationToken)),
        ];

        bool complete = !budget.IsSpent;
        TimeSpan elapsed = Stopwatch.GetElapsedTime(started);
        LogSearched(folders.Length, complete, elapsed.TotalMilliseconds);
        return new FoundFolders(folders, complete);
    }

    // Breadth first over all the roots at once: what the budget cuts off is what is deepest, in every root.
    private void Walk(string? home, ScanBudget budget, List<string> found, CancellationToken cancellationToken)
    {
        var seen = new HashSet<string>(FolderWalk.PathComparer);
        var queue = new Queue<Level>();
        foreach (SearchRoot root in home is null ? drives.Roots : [new SearchRoot(home, HomeDepth), .. drives.Roots])
        {
            if (root.MaxDepth > 0
                && Directory.Exists(root.Path)
                && FolderWalk.RealPath(new DirectoryInfo(root.Path), parentRealPath: null) is { } realPath
                && seen.Add(realPath))
            {
                queue.Enqueue(new Level(root.Path, realPath, Depth: 0, root.MaxDepth, root));
            }
        }

        while (!budget.IsSpent && queue.TryDequeue(out Level level))
        {
            cancellationToken.ThrowIfCancellationRequested();
            Visit(level, budget, seen, queue, found);
        }
    }

    // Reads the folders inside one folder. A folder with a .obsidian directory is found and not entered; the roots
    // themselves are never found (the home directory is too broad to be a source, and a drive folder is no vault).
    // A folder that is skipped is neither found nor entered, vault or not.
    // Internal for the tests: a folder that went away in the middle of a search cannot be made to go away from outside.
    internal static void Visit(Level level, ScanBudget budget, HashSet<string> seen, Queue<Level> queue, List<string> found)
    {
        try
        {
            foreach (FileSystemInfo entry in FolderWalk.Entries(level.Path))
            {
                if (!budget.TrySpend())
                {
                    return;
                }

                if (entry is not DirectoryInfo child || IsSkipped(child, level) || FolderWalk.RealPath(child, level.RealPath) is not { } realPath || !seen.Add(realPath))
                {
                    continue;
                }

                if (Directory.Exists(Path.Join(child.FullName, ObsidianDirectory)))
                {
                    found.Add(child.FullName);
                }
                else if (level.Depth + 1 < level.MaxDepth)
                {
                    queue.Enqueue(new Level(child.FullName, realPath, level.Depth + 1, level.MaxDepth));
                }
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // What cannot be read is not searched.
        }
    }

    // A hidden folder, one that holds what is built or fetched, and at the top of a root the folders that the root names.
    private static bool IsSkipped(DirectoryInfo child, Level level) =>
        FolderNames.IsHidden(child)
        || Array.Exists(SkippedFolders, skipped => string.Equals(skipped, child.Name, PathComparison.Current))
        || level.Root?.Skips(child.Name) == true;

    [LoggerMessage(Level = LogLevel.Information, Message = "Searched for vaults: {Count} found, complete {Complete}, in {ElapsedMs:F1} ms")]
    private partial void LogSearched(int count, bool complete, double elapsedMs);

    /// <summary>A folder that is to be read: where it is, its real path, and how deep it is below the root it was found from.</summary>
    /// <param name="Path">Full path of the folder, as it was reached (through a symbolic link when there was one).</param>
    /// <param name="RealPath">Its real path.</param>
    /// <param name="Depth">How many levels below its root it is.</param>
    /// <param name="MaxDepth">How many levels below the root the search goes.</param>
    /// <param name="Root">The root, for the level that is the root itself (depth 0); null for the folders below it. The names it skips apply to its top only.</param>
    internal readonly record struct Level(string Path, string RealPath, int Depth, int MaxDepth, SearchRoot? Root = null);

    private sealed record Remembered(FoundFolders Found, DateTimeOffset At);
}
