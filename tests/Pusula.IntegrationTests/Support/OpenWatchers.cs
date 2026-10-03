using Xunit;

namespace Pusula.IntegrationTests.Support;

/// <summary>
/// Counts the file system watchers this process has open, to see that a test left none behind. On Linux every
/// <see cref="FileSystemWatcher"/> holds an inotify instance, which shows as a descriptor of the process. Elsewhere there
/// is no way to count them and the checks do nothing.
/// </summary>
internal static class OpenWatchers
{
    private const string Descriptors = "/proc/self/fd";

    private static readonly TimeSpan Limit = TimeSpan.FromSeconds(10);

    /// <summary>The number of watchers that are open now; 0 where they cannot be counted.</summary>
    public static int Count()
    {
        if (!OperatingSystem.IsLinux() || !Directory.Exists(Descriptors))
        {
            return 0;
        }

        int count = 0;
        foreach (string descriptor in Directory.EnumerateFileSystemEntries(Descriptors))
        {
            try
            {
                if (new FileInfo(descriptor).LinkTarget?.Contains("inotify", StringComparison.Ordinal) == true)
                {
                    count++;
                }
            }
            catch (IOException)
            {
                // The descriptor was closed while the others were listed.
            }
        }

        return count;
    }

    /// <summary>
    /// Waits until at most <paramref name="expected"/> watchers are open: a watcher that was disposed gives its
    /// descriptor back a moment later. Fails when there are more after a time.
    /// </summary>
    /// <param name="expected">How many were open before the test began.</param>
    public static async Task WaitUntilNoMoreThanAsync(int expected)
    {
        DateTime deadline = DateTime.UtcNow + Limit;
        int open;
        while ((open = Count()) > expected)
        {
            if (DateTime.UtcNow > deadline)
            {
                throw new TimeoutException($"{open - expected} file system watcher(s) were left open ({open} are, {expected} were before).");
            }

            await Task.Delay(20, TestContext.Current.CancellationToken);
        }
    }
}
