namespace Pusula.Updates;

// What the checks say about themselves: one line at the Information level when a check fails (no network is not an error of pusula), without
// a stack trace; the next check follows a day later.
internal sealed partial class UpdateService
{
    [LoggerMessage(Level = LogLevel.Information, Message = "Could not check for a newer pusula: {Problem}; the next check is in {Hours} hours")]
    private partial void LogFailed(string problem, int hours);
}
