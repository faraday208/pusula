namespace Pusula.Browse;

// What one search says about itself. At the Information level one line when it ends, and one when a request gives up on it (a call of the file
// system did not return). At the Debug level (switch it on for this class with --Logging:LogLevel:Pusula.Browse=Debug) a line for each stage of it,
// written once at its end: where the time went, what the budget was spent on, and which folder was the slowest. The numbers are kept as the search
// goes (see SearchStats); nothing here reads the disk.
internal sealed partial class FolderFinder
{
    // Every stage of the search with how much it did, what it took from the budget and how long it took, and then where the budget ran out, if it did.
    // The counting is null when the search ended before it began.
    private void LogDetails(SearchStats stats, ScanBudget budget, ScanBudget? counting, TimeSpan total)
    {
        if (logger.IsEnabled(LogLevel.Debug))
        {
            LogWalk(stats.Walk.Count, stats.Walk.Entries, stats.Walk.Milliseconds, stats.Walk.SlowestMilliseconds, stats.Walk.Slowest);
            LogWalked(stats.Candidates, stats.LastLevelFolders, stats.VaultsFound);
            LogCandidates(stats.Scans.Count, stats.Scans.Capped, stats.Scans.Entries, stats.Scans.Milliseconds, stats.Scans.SlowestMilliseconds, stats.Scans.Slowest, stats.FoldersOfNotes, stats.Unreached);
            LogSampling(stats.Scans.NotesOpened, stats.Scans.NoteMilliseconds, stats.Scans.SlowestNoteMilliseconds, stats.Scans.SlowestNoteFolder);
            LogLastLevel(stats.Probes.Count, stats.ProbesFound, stats.Probes.Entries, stats.Probes.Milliseconds, stats.Probes.SlowestMilliseconds, stats.Probes.Slowest);
            if (stats.SpentDuring is { } during)
            {
                LogBudgetSpent(stats.SpentBy ?? "?", during, stats.SpentEntries, stats.SpentAfter.TotalMilliseconds);
            }
            else
            {
                LogBudgetLeft(budget.Taken);
            }

            if (counting is not null)
            {
                LogDescribe(stats.Describes.Count, stats.Describes.Milliseconds, stats.Describes.SlowestMilliseconds, stats.Describes.Slowest, counting.Taken, counting.SpentBy ?? "nothing");
            }

            LogTime(total.TotalMilliseconds, total.TotalMilliseconds - stats.AccountedMilliseconds);
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Searched for vaults: {Count} found, complete {Complete}, in {ElapsedMs:F1} ms")]
    private partial void LogSearched(int count, bool complete, double elapsedMs);

    [LoggerMessage(Level = LogLevel.Information, Message = "Searched for vaults: stopped after {ElapsedMs:F1} ms without a result")]
    private partial void LogStopped(double elapsedMs);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Searched for vaults: the file system did not answer while {Phase}; the request was answered after {ElapsedMs:F1} ms with the {Count} folders found so far, not complete and without counts; the search goes on until the call returns, and what it finds then is thrown away")]
    private partial void LogGaveUp(string phase, double elapsedMs, int count);

    [LoggerMessage(Level = LogLevel.Error, Message = "Searched for vaults: the search failed")]
    private partial void LogFailed(Exception exception);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Search walk: {Folders} folders read, {Entries} entries in {Ms:F1} ms; the slowest folder took {SlowestMs:F1} ms: {Slowest}")]
    private partial void LogWalk(int folders, long entries, double ms, double slowestMs, string? slowest);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Search walk found: {Candidates} folders with an entry note, {LastLevel} folders at the last level, {Vaults} vaults")]
    private partial void LogWalked(int candidates, int lastLevel, int vaults);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Search candidates: {Count} looked into ({Capped} cut at the limit of entries for one folder), {Entries} entries in {Ms:F1} ms; the slowest took {SlowestMs:F1} ms: {Slowest}; {OfNotes} are folders of notes, {Unreached} were not reached")]
    private partial void LogCandidates(int count, int capped, long entries, double ms, double slowestMs, string? slowest, int ofNotes, int unreached);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Search sampling: {Notes} notes opened in {Ms:F1} ms (part of the time of the candidates); the slowest took {SlowestMs:F1} ms, in the folder {Folder}")]
    private partial void LogSampling(int notes, double ms, double slowestMs, string? folder);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Search last level: {Count} folders asked for an entry note, {Found} have one, {Entries} entries in {Ms:F1} ms; the slowest took {SlowestMs:F1} ms: {Slowest}")]
    private partial void LogLastLevel(int count, int found, long entries, double ms, double slowestMs, string? slowest);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Search budget: used up by {Limit} during {Stage}, after {Entries} entries and {Ms:F1} ms")]
    private partial void LogBudgetSpent(string limit, string stage, int entries, double ms);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Search budget: not used up ({Entries} entries taken)")]
    private partial void LogBudgetLeft(int entries);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Search describe: {Count} folders in {Ms:F1} ms, with the budget of the counting; the slowest took {SlowestMs:F1} ms: {Slowest}; the counting took {Entries} entries, used up by {SpentBy}")]
    private partial void LogDescribe(int count, double ms, double slowestMs, string? slowest, int entries, string spentBy);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Search time: {Total:F1} ms in all, {Elsewhere:F1} ms of it outside the stages above")]
    private partial void LogTime(double total, double elsewhere);
}
