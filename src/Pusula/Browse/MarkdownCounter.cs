using Pusula.Indexing;

namespace Pusula.Browse;

/// <summary>
/// Counts the Markdown files below a folder, the way a source over that folder would find them (what the index of a
/// source has as files): at any depth, hidden files and folders left out, a symbolic link that leads to a folder followed
/// (a link that leads back into a folder that is being read is not entered, as the index does; a file that two links lead
/// to is counted for each). A folder of notes (a vault or a plain Markdown folder) is read in full, <c>node_modules</c>
/// excepted. A Claude Code folder is read the way <see cref="IndexBuilder"/> reads one: the runtime folders directly in it
/// (<c>plugins</c>, <c>sessions</c>, ... see <see cref="IndexBuilder.IsSkippedTopLevelDirectory"/>) are not entered, and of
/// <c>projects/&lt;project&gt;/</c> only <c>memory</c> is. The count stops at a number of files that is more than anyone
/// wants to read off a list, at a number of entries that one folder may take, and at the end of the budget it is given. The
/// folder is only ever read.
/// </summary>
internal static class MarkdownCounter
{
    // The folders of a Claude Code folder that IndexBuilder treats on their own (see IndexBuilder.ChildMode).
    private const string ProjectsDirectory = "projects";
    private const string MemoryDirectory = "memory";

    /// <summary>Counts the Markdown files below a folder.</summary>
    /// <param name="folder">Full path of the folder.</param>
    /// <param name="profile">What kind of folder it is: <see cref="SourceProfile.Claude"/> is read as a Claude Code folder, any other as a folder of notes.</param>
    /// <param name="limit">The most files to count; with more files the count is this many and says so.</param>
    /// <param name="maxEntries">The most entries to read for this folder; with more entries the count is what was counted by then, and says there are more.</param>
    /// <param name="budget">What the counting may read, shared with the other folders of the same request.</param>
    /// <param name="cancellationToken">Stops the counting when the request is gone.</param>
    /// <returns>The count; null when the budget ran out before it was known (a number that is only part of the count is not given), and for a folder whose real path cannot be found (a link that never ends). A folder that cannot be read has no files to count: 0.</returns>
    public static MarkdownCount? Count(string folder, SourceProfile profile, int limit, int maxEntries, ScanBudget budget, CancellationToken cancellationToken)
    {
        if (budget.IsSpent || FolderWalk.RealPath(new DirectoryInfo(folder), parentRealPath: null) is not { } realPath)
        {
            return null;
        }

        var scan = new Scan(limit, maxEntries, budget, cancellationToken);
        scan.Visiting.Add(realPath);
        Walk(folder, realPath, profile == SourceProfile.Claude ? WalkMode.Root : WalkMode.Notes, scan);

        return scan.Stopped switch
        {
            Stop.Budget => null,
            Stop.Limit => new MarkdownCount(limit, More: true),
            Stop.Entries => new MarkdownCount(scan.Count, More: true),
            _ => new MarkdownCount(scan.Count, More: false),
        };
    }

    private static void Walk(string folder, string realPath, WalkMode mode, Scan scan)
    {
        scan.CancellationToken.ThrowIfCancellationRequested();

        try
        {
            foreach (FileSystemInfo entry in FolderWalk.Entries(folder))
            {
                // The entries of one folder are counted before the budget of the request is: what is not read is not spent.
                if (++scan.Entries > scan.MaxEntries)
                {
                    scan.Stopped = Stop.Entries;
                    return;
                }

                if (!scan.Budget.TrySpend())
                {
                    scan.Stopped = Stop.Budget;
                    return;
                }

                string name = entry.Name;
                if (FolderNames.IsHidden(name))
                {
                    continue;
                }

                if (entry is DirectoryInfo child)
                {
                    if (ChildMode(mode, name) is { } childMode && FolderWalk.RealPath(child, realPath) is { } childRealPath && scan.Visiting.Add(childRealPath))
                    {
                        try
                        {
                            Walk(child.FullName, childRealPath, childMode, scan);
                        }
                        finally
                        {
                            scan.Visiting.Remove(childRealPath);
                        }

                        if (scan.Stopped != Stop.None)
                        {
                            return;
                        }
                    }
                }
                else if (mode is not (WalkMode.Projects or WalkMode.ProjectDirectory)
                    && name.EndsWith(".md", StringComparison.OrdinalIgnoreCase)
                    && ++scan.Count > scan.Limit)
                {
                    scan.Stopped = Stop.Limit;
                    return;
                }
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // What cannot be read is not counted; the rest of the folder is not looked at.
        }
    }

    // Which folders are entered, and how, depending on where the count currently is: what IndexBuilder does, for the folders it does.
    private static WalkMode? ChildMode(WalkMode mode, string name) => mode switch
    {
        WalkMode.Root when IndexBuilder.IsSkippedTopLevelDirectory(name, PathComparison.Current) => null,
        WalkMode.Root when string.Equals(name, ProjectsDirectory, PathComparison.Current) => WalkMode.Projects,
        WalkMode.Root or WalkMode.Normal => WalkMode.Normal,

        // A folder of notes is read in full, except for the dependencies of a project that lives inside it.
        WalkMode.Notes when FolderNames.IsNodeModules(name) => null,
        WalkMode.Notes => WalkMode.Notes,

        // projects/<project>/: only the memory directory holds configuration; the rest is transcripts.
        WalkMode.Projects => WalkMode.ProjectDirectory,
        WalkMode.ProjectDirectory when string.Equals(name, MemoryDirectory, PathComparison.Current) => WalkMode.Normal,
        _ => null,
    };

    private enum WalkMode
    {
        /// <summary>The Claude Code folder itself.</summary>
        Root,

        /// <summary>Any folder of a Claude Code folder that is read in full.</summary>
        Normal,

        /// <summary>The <c>projects</c> folder: only its project folders are entered.</summary>
        Projects,

        /// <summary><c>projects/&lt;project&gt;</c>: only its <c>memory</c> folder is entered.</summary>
        ProjectDirectory,

        /// <summary>Any folder of a folder of notes.</summary>
        Notes,
    }

    private enum Stop
    {
        /// <summary>The count was not stopped: it is the whole count.</summary>
        None,

        /// <summary>More files than the limit.</summary>
        Limit,

        /// <summary>More entries than one folder may take.</summary>
        Entries,

        /// <summary>The budget of the request ran out.</summary>
        Budget,
    }

    private sealed class Scan(int limit, int maxEntries, ScanBudget budget, CancellationToken cancellationToken)
    {
        public int Limit { get; } = limit;

        public int MaxEntries { get; } = maxEntries;

        public ScanBudget Budget { get; } = budget;

        public CancellationToken CancellationToken { get; } = cancellationToken;

        // The real paths of the folders the count is inside of now (a link back to one of them is a loop).
        public HashSet<string> Visiting { get; } = new(FolderWalk.PathComparer);

        public int Count;

        public int Entries;

        public Stop Stopped;
    }
}
