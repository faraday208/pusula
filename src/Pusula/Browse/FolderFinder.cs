using System.Diagnostics;
using System.Runtime.ExceptionServices;
using Pusula.Indexing;
using Pusula.Sources;
using Pusula.Startup;

namespace Pusula.Browse;

/// <summary>
/// Looks for the folders that a user is likely to want to show: Obsidian vaults (a folder with a <c>.obsidian</c>
/// directory) in the home directory and the drive folders (<see cref="DriveFolders"/>: <c>/mnt</c> and <c>/media</c>, the
/// drives themselves on Windows), folders of linked notes that have no <c>.obsidian</c> directory but are recognized by their
/// content (<see cref="FolderKind.Notes"/>, see <see cref="NoteFolderCheck"/> for the rules), and the <c>.claude</c> folder
/// of the home directory. The search goes level by level, so that what is close to a root is found before what is deep down.
/// It does not go into hidden folders (see <see cref="FolderNames.IsHidden(DirectoryInfo)"/>: on Windows that includes the
/// ones with the Hidden or System attribute, such as <c>AppData</c>), <c>node_modules</c>, <c>bin</c> and <c>obj</c>, nor into
/// a vault, nor at the top of a root into the folders the root skips (<see cref="SearchRoot.SkippedAtTop"/>: the system
/// folders of a Windows drive); it follows a symbolic link once. On its way it takes note of the folders that have an entry
/// note (<see cref="NoteFolderCheck.IsEntryNote"/>), the candidates for folders of notes, but for the roots, which are never
/// found, and, below the roots that ask their last level, of the folders at that level, which it does not read
/// (<see cref="WalkFindings.Deepest"/>: it has their names from the folder above, and noting them costs nothing). When the walk is done, <see cref="NoteFolderSearch"/> looks into the
/// candidates, the shallowest first, then asks the folders of the last level for an entry note (it reads the first
/// <see cref="NoteFolderCheck.ProbeEntries"/> entries of each at most, every one taken from the budget), and settles which are folders of notes.
/// The last level is asked under the home directory and, on Windows, on the system drive, and not on the other drives (<c>/mnt</c>, <c>/media</c>,
/// <c>/Volumes</c>, the other drives of Windows): an external disk may sleep or be slow, one folder that takes seconds to open holds the whole search
/// back, and the folders of notes measured at the last level were all under the home directory (see <see cref="SearchRoot.ProbesLastLevel"/>). So a
/// folder of notes is found as deep as a vault is there, and one level less deep on the other drives, where a vault is found at every level; and
/// the walk keeps the whole of its budget: what the folders of notes cost comes from what is left of it. The search stops at the
/// budget of <see cref="BrowseLimits"/>, which makes <see cref="FoundFolders.Complete"/> false; a candidate or a folder of the last
/// level that was not looked into by then is not shown. The result is remembered for
/// <see cref="BrowseLimits.CacheLifetime"/>. Folders are only ever read; of a note only its first kilobytes, and only to see
/// whether it has a wikilink. A call of the file system that does not return (a disk that sleeps, a network drive that does not answer)
/// does not hold a request for more than its deadline: see <see cref="Find"/>.
/// </summary>
internal sealed partial class FolderFinder(UserDirectories directories, DriveFolders drives, BrowseLimits limits, TimeProvider time, ILogger<FolderFinder> logger)
{
    /// <summary>How many levels below the home directory the search looks.</summary>
    internal const int HomeDepth = 4;

    private const string ObsidianDirectory = ".obsidian";

    private readonly object _gate = new();
    private Remembered? _remembered;
    private Running? _running;

    /// <summary>
    /// How the search lists a folder, when something other than the file system is to answer; the file system's way when null. The extension point
    /// of the tests, which make a folder stall to see that a request does not wait for it (see <see cref="FolderWalk.Use"/>).
    /// </summary>
    internal Func<string, IEnumerable<FileSystemInfo>>? Listing { get; init; }

    /// <summary>
    /// How the search tells a network location, which a link must not lead to, when something other than the system is to say; the system's way when null.
    /// The extension point of the tests (see <see cref="LinkGuard.Use"/>).
    /// </summary>
    internal Func<string, bool>? NetworkLocation { get; init; }

    /// <summary>
    /// Gives the folders that were found: the result of the last search while it is not older than the cache lifetime,
    /// otherwise the result of a new search. The search runs on a thread of its own, one at a time: a request that comes while it
    /// runs waits for it and shares its result. A request waits for as long as the time limit of the part of the search that is running
    /// allows, and <see cref="BrowseLimits.DeadlineMargin"/> more: the search of the disk has <see cref="BrowseLimits.SearchTime"/> and the counting
    /// of the folders found that follows it <see cref="BrowseLimits.CountTime"/>. A call of the file system that has not returned by then (a disk that
    /// sleeps, a network drive that does not answer) is not waited for: the request is answered with the folders that the search had
    /// found so far (every vault the walk had met, the folders of notes of the stages that were over), without counts and not complete. The search is
    /// given up on: it stops at its next step once the call returns, and what it has is thrown away. While it still runs, no other search starts on the same file system, and every request is answered with that snapshot.
    /// A result that was cut short this way is never remembered, so the first request after the file system answers again searches anew.
    /// </summary>
    /// <param name="cancellationToken">Stops the wait when the request is gone: the search goes on, and is remembered when it ends.</param>
    public FoundFolders Find(CancellationToken cancellationToken)
    {
        Running running;
        lock (_gate)
        {
            if (Recall() is { } recalled)
            {
                return recalled;
            }

            cancellationToken.ThrowIfCancellationRequested();
            running = _running ??= Start();
        }

        // The search of the disk is waited for until its time limit and a margin after its start, and the counting that follows it until its time limit
        // and a margin after the end of the search of the disk.
        WaitFor(running.SearchOver, limits.SearchTime, running.Started, cancellationToken);
        if (running.SearchOver.IsSet)
        {
            WaitFor(running.Done, limits.CountTime, running.SearchEnded, cancellationToken);
        }

        bool givesUp;
        FoundFolders answer;
        lock (_gate)
        {
            if (running.Final is { } final)
            {
                return final;
            }

            running.Error?.Throw();
            givesUp = !running.Finished && !running.GaveUp;
            if (givesUp)
            {
                running.GiveUp();
            }

            answer = running.Snapshot ??= Snapshot(running.Found, BrowsePaths.HomeFolder(directories));
        }

        if (givesUp)
        {
            LogGaveUp(running.SearchOver.IsSet ? "counting" : "searching", Stopwatch.GetElapsedTime(running.Started).TotalMilliseconds, answer.Folders.Count);
        }

        return answer;
    }

    // The gate is held.
    private FoundFolders? Recall() =>
        _remembered is { } remembered && time.GetUtcNow() - remembered.At < limits.CacheLifetime ? remembered.Found : null;

    // Waits for the signal until the time limit and the margin after `from` (a stopwatch timestamp) have passed; it does not wait at all when they have.
    private void WaitFor(ManualResetEventSlim signal, TimeSpan limit, long from, CancellationToken cancellationToken)
    {
        TimeSpan left = limit + limits.DeadlineMargin - Stopwatch.GetElapsedTime(from);
        signal.Wait(left > TimeSpan.Zero ? left : TimeSpan.Zero, cancellationToken);
    }

    // The gate is held. The search has a thread of its own, for it may be stuck in a call of the file system that does not return.
    private Running Start()
    {
        var running = new Running();
        new Thread(() => Run(running)) { IsBackground = true, Name = "Pusula folder search" }.Start();
        return running;
    }

    private void Run(Running running)
    {
        FoundFolders? result = null;
        ExceptionDispatchInfo? error = null;
        try
        {
            using (FolderWalk.Use(Listing))
            using (LinkGuard.Use(NetworkLocation))
            {
                result = Search(running);
            }
        }
        catch (OperationCanceledException)
        {
            // The request gave up on this search (see Find): it has nothing to give.
        }
        catch (Exception exception)
        {
            error = ExceptionDispatchInfo.Capture(exception);
            LogFailed(exception);
        }

        lock (_gate)
        {
            if (!running.GaveUp)
            {
                running.Final = result;
                running.Error = error;
                if (result is not null)
                {
                    _remembered = new Remembered(result, time.GetUtcNow());
                }
            }

            running.Finished = true;
            _running = null;
        }

        running.EndSearch();
        running.Done.Set();
    }

    private FoundFolders Search(Running running)
    {
        CancellationToken cancellationToken = running.Cancellation.Token;
        long started = Stopwatch.GetTimestamp();
        var stats = new SearchStats();
        var budget = new ScanBudget(limits.SearchEntries, limits.SearchTime);
        ScanBudget? counting = null;
        FoundFolders? result = null;
        try
        {
            string? home = BrowsePaths.HomeFolder(directories);
            List<(string Path, FolderKind Kind)> found = [];

            // The folder of Claude Code is not looked for: it is hidden, which no search goes into, so it is asked for by name.
            string? claude = home is null ? null : Path.Join(home, FolderNames.ClaudeFolder);
            if (claude is not null && Directory.Exists(claude))
            {
                found.Add((claude, FolderKind.Claude));
            }

            running.Publish([.. found]);

            // The walk first, with the whole of the budget: what makes a vault is only a directory, and the folders of notes are looked into with what is left.
            // What is found is told to whoever gives up on the search (see Find) as it goes: each vault, and the folders of notes at the ends of the stages.
            var walked = new WalkFindings();
            Walk(home, budget, walked, stats, () => running.Publish([.. found, .. walked.Vaults.Select(path => (path, FolderKind.Vault))]), cancellationToken);
            stats.Candidates = walked.Candidates.Count;
            stats.LastLevelFolders = walked.Deepest.Count;
            stats.VaultsFound = walked.Vaults.Count;
            stats.NoteBudget(budget, "the walk");
            found.AddRange(walked.Vaults.Select(path => (path, FolderKind.Vault)));

            IReadOnlyList<string> notes = NoteFolderSearch.Find(
                walked,
                limits,
                budget,
                stats,
                shown => running.Publish([.. found, .. shown.Select(path => (path, FolderKind.Notes))]),
                cancellationToken);
            found.AddRange(notes.Select(path => (path, FolderKind.Notes)));
            running.Publish([.. found]);

            // The search of the disk is over; the counting that is left has a time limit of its own, and so has the wait of a request for it.
            running.EndSearch();
            counting = new ScanBudget(limits.CountEntries, limits.CountTime);
            BrowseFolder[] folders =
            [
                .. Ordered(found, home).Select(folder => Describe(folder.Path, folder.Kind == FolderKind.Notes, home, counting, stats, cancellationToken)),
            ];

            result = new FoundFolders(folders, Complete: !budget.IsSpent);
            return result;
        }
        finally
        {
            TimeSpan elapsed = Stopwatch.GetElapsedTime(started);
            LogDetails(stats, budget, counting, elapsed);
            if (result is null)
            {
                LogStopped(elapsed.TotalMilliseconds);
            }
            else
            {
                LogSearched(result.Folders.Count, result.Complete, elapsed.TotalMilliseconds);
            }
        }
    }

    // The folders sorted by name (the Turkish alphabet, ignoring case) and, for the same name, by display; counted in that order, which is the way a page
    // shows them (but for the folders a source shows already, which it moves to the end): what the budget of the counting does not reach is what is further down.
    private static IEnumerable<(string Path, FolderKind Kind, string Name, string Display)> Ordered(IEnumerable<(string Path, FolderKind Kind)> found, string? home) =>
        found
            .Select(folder => (folder.Path, folder.Kind, Name: SourceIds.NameOf(folder.Path), Display: BrowsePaths.Display(folder.Path, home)))
            .OrderBy(folder => folder.Name, Comparer<string>.Create(FolderNames.CompareIgnoringCase))
            .ThenBy(folder => folder.Display, Comparer<string>.Create(FolderNames.Compare));

    // What is known of the folders so far, made without a call of the file system: what each looks like by the way it was found, and no count.
    private static FoundFolders Snapshot(IEnumerable<(string Path, FolderKind Kind)> found, string? home) =>
        new([.. Ordered(found, home).Select(folder => new BrowseFolder(folder.Name, folder.Path, folder.Display, Listed: false, folder.Kind))], Complete: false);

    // What a found folder looks like. A folder of notes has no kind of its own as far as a listing goes (it would be plain Markdown), so it gets Notes here;
    // one that a source would take for a Claude Code folder keeps that, for that is what adding it would make of it.
    private BrowseFolder Describe(string path, bool notes, string? home, ScanBudget counting, SearchStats stats, CancellationToken cancellationToken)
    {
        long started = Stopwatch.GetTimestamp();
        int taken = counting.Taken;
        BrowseFolder described = FolderLister.Describe(path, home, limits, counting, cancellationToken);
        stats.Describes.Add(Stopwatch.GetTimestamp() - started, counting.Taken - taken, path);
        return notes && described.Kind is null ? described with { Kind = FolderKind.Notes } : described;
    }

    // Breadth first over all the roots at once: what the budget cuts off is what is deepest, in every root. What it comes across besides the folders
    // it enters goes to walked (see WalkFindings).
    private void Walk(string? home, ScanBudget budget, WalkFindings walked, SearchStats stats, Action vaultsFound, CancellationToken cancellationToken)
    {
        var seen = new HashSet<string>(FolderWalk.PathComparer);
        var queue = new Queue<Level>();
        foreach (SearchRoot root in home is null ? drives.Roots : [new SearchRoot(home, HomeDepth) { ProbesLastLevel = true }, .. drives.Roots])
        {
            if (root.MaxDepth > 0
                && Directory.Exists(root.Path)
                && FolderWalk.RealPath(new DirectoryInfo(root.Path), parentRealPath: null) is { } realPath
                && seen.Add(realPath))
            {
                queue.Enqueue(new Level(root.Path, realPath, Depth: 0, root.MaxDepth, root, root.ProbesLastLevel));
            }
        }

        while (!budget.IsSpent && queue.TryDequeue(out Level level))
        {
            cancellationToken.ThrowIfCancellationRequested();
            long visited = Stopwatch.GetTimestamp();
            int taken = budget.Taken;
            int vaults = walked.Vaults.Count;
            Visit(level, budget, seen, queue, walked);
            stats.Walk.Add(Stopwatch.GetTimestamp() - visited, budget.Taken - taken, level.Path);
            if (walked.Vaults.Count > vaults)
            {
                vaultsFound();
            }
        }
    }

    // Reads the folders inside one folder. A folder with a .obsidian directory is found and not entered; the roots
    // themselves are never found (the home directory is too broad to be a source, and a drive folder is no vault).
    // A folder that is skipped is neither found nor entered, vault or not. A folder that has an entry note is a candidate for a folder of notes,
    // and is entered all the same (a vault, or another folder of notes, may lie inside it); a root is not one, for it is never found.
    // A folder at the last level of the walk is not entered, as before; below a root that asks its last level (SearchRoot.ProbesLastLevel) it is kept (it costs
    // nothing: its name was read already) for NoteFolderSearch to ask for an entry note, after the walk, so that this costs the walk none of its reach; below
    // another root it is let go, and only a vault is found there.
    // Internal for the tests: a folder that went away in the middle of a search cannot be made to go away from outside.
    internal static void Visit(Level level, ScanBudget budget, HashSet<string> seen, Queue<Level> queue, WalkFindings walked)
    {
        bool hasEntryNote = false;
        try
        {
            foreach (FileSystemInfo entry in FolderWalk.Entries(level.Path))
            {
                if (!budget.TrySpend())
                {
                    return;
                }

                if (entry is not DirectoryInfo child)
                {
                    if (!hasEntryNote && level.Depth > 0 && NoteFolderCheck.IsEntryNote(entry.Name))
                    {
                        hasEntryNote = true;
                        walked.Candidates.Add(level);
                    }

                    continue;
                }

                if (IsSkipped(child, level) || FolderWalk.RealPath(child, level.RealPath) is not { } realPath || !seen.Add(realPath))
                {
                    continue;
                }

                if (Directory.Exists(Path.Join(child.FullName, ObsidianDirectory)))
                {
                    walked.Vaults.Add(child.FullName);
                }
                else if (level.Depth + 1 < level.MaxDepth)
                {
                    queue.Enqueue(new Level(child.FullName, realPath, level.Depth + 1, level.MaxDepth, ProbesLastLevel: level.ProbesLastLevel));
                }
                else if (level.ProbesLastLevel)
                {
                    walked.Deepest.Add(new Level(child.FullName, realPath, level.Depth + 1, level.MaxDepth, ProbesLastLevel: true));
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
        FolderNames.IsLeftOutOfSearch(child) || level.Root?.Skips(child.Name) == true;

    /// <summary>A folder that is to be read (or, at the last level of the walk, only looked into): where it is, its real path, and how deep it is below the root it was found from.</summary>
    /// <param name="Path">Full path of the folder, as it was reached (through a symbolic link when there was one).</param>
    /// <param name="RealPath">Its real path.</param>
    /// <param name="Depth">How many levels below its root it is.</param>
    /// <param name="MaxDepth">How many levels below the root the search goes.</param>
    /// <param name="Root">The root, for the level that is the root itself (depth 0); null for the folders below it. The names it skips apply to its top only.</param>
    /// <param name="ProbesLastLevel">Whether the root it was found from asks its last level for an entry note (<see cref="SearchRoot.ProbesLastLevel"/>).</param>
    internal readonly record struct Level(string Path, string RealPath, int Depth, int MaxDepth, SearchRoot? Root = null, bool ProbesLastLevel = false);

    /// <summary>What the walk comes across besides the folders it enters; <see cref="NoteFolderSearch"/> takes it from there.</summary>
    internal sealed class WalkFindings
    {
        /// <summary>The full paths of the vaults: folders with a <c>.obsidian</c> directory, at any level of the walk, the last included.</summary>
        public List<string> Vaults { get; } = [];

        /// <summary>The folders that have an entry note, which the walk read the entries of (so not those at the last level, and not the roots): in the order it met them, which is the shallowest first.</summary>
        public List<Level> Candidates { get; } = [];

        /// <summary>
        /// The folders at the last level of the walk, below which it does not go, that are neither skipped nor vaults, in the order it met them, but for
        /// those below a root that does not ask its last level (<see cref="SearchRoot.ProbesLastLevel"/>). The walk read the name of each in the listing of
        /// the folder above, and not what is in it: whether it has an entry note is asked later.
        /// </summary>
        public List<Level> Deepest { get; } = [];
    }

    private sealed record Remembered(FoundFolders Found, DateTimeOffset At);

    // One search that is running (or is given up on and has not returned from a call of the file system yet). What the search and the requests tell
    // each other is here: the folders that the search has found so far, and, with the gate held, whether a request gave up on it.
    private sealed class Running
    {
        private (string Path, FolderKind Kind)[] _found = [];
        private long _searchEnded;

        /// <summary>When it began, by <see cref="Stopwatch.GetTimestamp"/>.</summary>
        public long Started { get; } = Stopwatch.GetTimestamp();

        /// <summary>Set when the search of the disk has ended and the counting begins, or when the search has ended before that.</summary>
        public ManualResetEventSlim SearchOver { get; } = new();

        /// <summary>Set when the search has ended, whatever came of it.</summary>
        public ManualResetEventSlim Done { get; } = new();

        /// <summary>What stops the search at its next step once a request has given up on it.</summary>
        public CancellationTokenSource Cancellation { get; } = new();

        /// <summary>The folders that the search has found so far, as it last told (see <see cref="Publish"/>).</summary>
        public (string Path, FolderKind Kind)[] Found => Volatile.Read(ref _found);

        /// <summary>What a request that gave up on the search answered with and every request after it is answered with, not complete and without counts; only changed with the gate held.</summary>
        public FoundFolders? Snapshot { get; set; }

        /// <summary>When the search of the disk ended and the counting began, by <see cref="Stopwatch.GetTimestamp"/>; 0 until then.</summary>
        public long SearchEnded => Volatile.Read(ref _searchEnded);

        /// <summary>Whether a request gave up on the search; only changed with the gate held.</summary>
        public bool GaveUp { get; private set; }

        /// <summary>Whether the search has ended; only changed with the gate held.</summary>
        public bool Finished { get; set; }

        /// <summary>What a search that nobody gave up on found, for the requests that wait for it; only changed with the gate held.</summary>
        public FoundFolders? Final { get; set; }

        /// <summary>What went wrong in a search that nobody gave up on, for the requests that wait for it; only changed with the gate held.</summary>
        public ExceptionDispatchInfo? Error { get; set; }

        /// <summary>Tells the folders that are found so far. A copy is made for it, and it is cheap: what is made of it (sorted, with the names) is made by the request that gives up, when there is one.</summary>
        /// <param name="found">All the folders found so far, with how each was found.</param>
        public void Publish((string Path, FolderKind Kind)[] found) => Volatile.Write(ref _found, found);

        public void EndSearch()
        {
            Interlocked.CompareExchange(ref _searchEnded, Stopwatch.GetTimestamp(), 0);
            SearchOver.Set();
        }

        public void GiveUp()
        {
            GaveUp = true;
            Cancellation.Cancel();
        }
    }
}
