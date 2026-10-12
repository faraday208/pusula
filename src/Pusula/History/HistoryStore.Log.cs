namespace Pusula.History;

// What the history says about itself: one line at the Information level when a summary is kept (once a day for each source), and at the
// Warning level when the file of a source cannot be used (once, until it has been fine again) or today's summary cannot be written (once a
// day for each source; the next request tries again). The answer of the changes endpoint carries the same problems.
internal sealed partial class HistoryStore
{
    [LoggerMessage(Level = LogLevel.Information, Message = "Kept the summary of {Day:yyyy-MM-dd} of {Root}: {Files} files, in {File}")]
    private partial void LogKept(DateOnly day, string root, int files, string file);

    [LoggerMessage(Level = LogLevel.Warning, Message = "The history file {File} cannot be used and is treated as empty: {Problem}; the next summary replaces it")]
    private partial void LogUnusable(string file, string problem);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Today's summary of {Root} could not be written to {Folder}: {Problem}; the next request tries again")]
    private partial void LogWriteFailed(string root, string folder, string problem);
}
