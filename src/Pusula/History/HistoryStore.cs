using System.Collections.Concurrent;
using Pusula.Indexing;
using Pusula.Sources;
using Pusula.Startup;

namespace Pusula.History;

/// <summary>
/// Keeps a daily summary of every source and tells what changed since the earlier ones (<see cref="Changes"/>). The first time the changes
/// of a source are asked for on a day, the summary of that day is taken and written to the history file of the source in the state folder of
/// pusula (see <see cref="HistoryFile"/>); a summary is never replaced later the same day. Every summary of the last seven days is kept, and
/// of the older ones only the newest: that one is what the comparison with a week ago is made against. pusula never writes into a folder that
/// is shown, so when the state folder is inside one nothing is written. The summaries of a source are read when it is first asked for and then
/// kept in memory; the file is read again just before a summary is added to it, for the file is what is kept (another pusula may have written it).
/// </summary>
/// <param name="directories">The directories of the user: the history is kept in <c>pusula/history</c> below their state directory, and is off when there is none.</param>
/// <param name="time">The clock; the day is its local calendar day.</param>
/// <param name="logger">Where problems with the history files are said.</param>
internal sealed partial class HistoryStore(UserDirectories directories, TimeProvider time, ILogger<HistoryStore> logger)
{
    /// <summary>
    /// How many days back the summaries are kept in full: a summary of a day after today minus this many days is kept, and of the older
    /// ones the newest, which is also the one that the comparison with a week ago is made against.
    /// </summary>
    internal const int KeptDays = 7;

    private readonly string? _folder = directories.State.Length == 0 ? null : Path.Join(directories.State, "pusula", "history");
    private readonly ConcurrentDictionary<string, SourceDays> _sources = new(StringComparer.Ordinal);

    /// <summary>
    /// Keeps today's summary of the source when there is none yet, and compares the source with the summary of the day before and the
    /// summary of a week ago. A problem with the history is told in the answer and never thrown: the comparison is what matters.
    /// </summary>
    /// <param name="current">The index of the source as it is now.</param>
    /// <param name="shownRoots">The full paths of the folders of all sources: pusula does not write into any of them.</param>
    public ChangesResponse Changes(ConfigIndex current, IReadOnlyCollection<string> shownRoots)
    {
        if (_folder is null)
        {
            return new ChangesResponse(HistoryState.Off);
        }

        DateTimeOffset now = time.GetLocalNow();
        var today = DateOnly.FromDateTime(now.DateTime);
        string root = HistoryFile.RootOf(current);
        string name = HistoryFile.NameOf(current.Profile, root);
        SourceDays source = _sources.GetOrAdd(name, static _ => new SourceDays());

        HistoryState state;
        string? error;
        IReadOnlyList<DailySummary> days;
        lock (source)
        {
            (state, error) = Keep(source, _folder, name, current, root, shownRoots, now, today);
            days = source.Days;
        }

        // Today's own summary is never compared with: only a day before today, and a day that is a week ago or earlier.
        DailySummary? previousDay = days.Where(day => day.Day < today).MaxBy(day => day.Day);
        DailySummary? previousWeek = days.Where(day => day.Day <= today.AddDays(-KeptDays)).MaxBy(day => day.Day);
        ChangesSince? sinceDay = previousDay is null ? null : ChangeCalculator.Compare(previousDay, current);
        ChangesSince? sinceWeek = previousWeek is null ? null : ReferenceEquals(previousWeek, previousDay) ? sinceDay : ChangeCalculator.Compare(previousWeek, current);

        return new ChangesResponse(state, days.Count == 0 ? null : days.Min(day => day.Day), sinceDay, sinceWeek, error);
    }

    /// <summary>
    /// Puts the summaries of the same day together: of two summaries of one day the one that was taken earlier is kept, for a summary
    /// of a day is never replaced later that day. The result is in the order of the days, the oldest first.
    /// </summary>
    /// <param name="days">The summaries, from any number of places.</param>
    internal static List<DailySummary> Merge(IEnumerable<DailySummary> days)
    {
        var byDay = new Dictionary<DateOnly, DailySummary>();
        foreach (DailySummary summary in days)
        {
            if (!byDay.TryGetValue(summary.Day, out DailySummary? known) || summary.TakenAt < known.TakenAt)
            {
                byDay[summary.Day] = summary;
            }
        }

        return [.. byDay.Values.OrderBy(summary => summary.Day)];
    }

    /// <summary>Keeps every summary of a day after <paramref name="today"/> minus <see cref="KeptDays"/> days, and of the others only the newest.</summary>
    /// <param name="days">The summaries, one for each day.</param>
    /// <param name="today">The local calendar day.</param>
    internal static List<DailySummary> Prune(IEnumerable<DailySummary> days, DateOnly today)
    {
        DateOnly cutoff = today.AddDays(-KeptDays);
        DailySummary[] all = [.. days];
        DateOnly? newestOlder = all.Where(summary => summary.Day <= cutoff).Select(summary => (DateOnly?)summary.Day).Max();
        return [.. all.Where(summary => summary.Day > cutoff || summary.Day == newestOlder)];
    }

    // Reads the file of the source when it was not read yet, and takes today's summary when it is not kept yet and can be. Under the lock of the source.
    private (HistoryState State, string? Error) Keep(
        SourceDays source,
        string folder,
        string name,
        ConfigIndex current,
        string root,
        IReadOnlyCollection<string> shownRoots,
        DateTimeOffset now,
        DateOnly today)
    {
        string file = Path.Join(folder, name);
        if (!source.Loaded)
        {
            source.Days = Merge(Read(source, file, current.Profile, root));
            source.Loaded = true;
        }

        if (source.Days.Any(day => day.Day == today))
        {
            return (HistoryState.Kept, null);
        }

        if (IsInside(folder, shownRoots))
        {
            return (HistoryState.InsideSource, null);
        }

        try
        {
            // Another pusula may have written the file since it was read: today's summary is added to what the file holds now.
            DailySummary summary = DailySummary.Of(current, today, now);
            List<DailySummary> kept = Prune(Merge([.. Read(source, file, current.Profile, root), summary]), today);
            HistoryFile.Write(folder, name, current.Profile, root, kept);
            source.Days = kept;
            LogKept(today, root, summary.Files.Count, file);
            return (HistoryState.Kept, null);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // The next request tries again, and says nothing more about it today.
            if (source.FailedOn != today)
            {
                source.FailedOn = today;
                LogWriteFailed(root, folder, exception.Message);
            }

            return (HistoryState.WriteFailed, exception.Message);
        }
    }

    private List<DailySummary> Read(SourceDays source, string file, SourceProfile profile, string root)
    {
        HistoryRead read = HistoryFile.Read(file, profile, root);
        if (read.Problem is null)
        {
            source.Unusable = false;
        }
        else if (!source.Unusable)
        {
            source.Unusable = true;
            LogUnusable(file, read.Problem);
        }

        return [.. read.Days];
    }

    // Whether the folder is a folder that is shown, or inside one: compared as full paths, the way the platform compares paths.
    private static bool IsInside(string folder, IReadOnlyCollection<string> shownRoots)
    {
        string full = Path.TrimEndingDirectorySeparator(Path.GetFullPath(folder));
        foreach (string shownRoot in shownRoots)
        {
            string shown = Path.TrimEndingDirectorySeparator(Path.GetFullPath(shownRoot));
            string below = Path.EndsInDirectorySeparator(shown) ? shown : shown + Path.DirectorySeparatorChar;
            if (SourcePaths.Same(full, shown) || full.StartsWith(below, PathComparison.Current))
            {
                return true;
            }
        }

        return false;
    }

    // What is known of one source. Only used with a lock on the object itself.
    private sealed class SourceDays
    {
        // Whether the file was read. It is read once; after that the summaries are kept in Days.
        public bool Loaded { get; set; }

        // The summaries that are kept, the oldest first.
        public IReadOnlyList<DailySummary> Days { get; set; } = [];

        // Whether the file could not be used when it was read last: told once, and again when it has been fine in between.
        public bool Unusable { get; set; }

        // The day on which writing the summary failed last: told once a day.
        public DateOnly? FailedOn { get; set; }
    }
}
