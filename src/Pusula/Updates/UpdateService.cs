namespace Pusula.Updates;

/// <summary>
/// Asks GitHub for the latest release: the first time right after the server has started (it never holds the start back), then every day as
/// long as pusula runs. A check that fails waits for the next day. It does nothing when the check is off (<see cref="UpdateNotice.Enabled"/>).
/// When a check first finds a newer version, one line says so on the console.
/// </summary>
/// <param name="notice">Keeps what the checks find.</param>
/// <param name="clients">Makes the client of the request (<see cref="ReleaseLookup.ClientName"/>).</param>
/// <param name="lifetime">Tells when the server has started.</param>
/// <param name="time">The clock of the daily timer.</param>
/// <param name="logger">Where a check that failed is told.</param>
/// <param name="announce">Writes the line for a newer version; <see cref="Console.WriteLine(string)"/> in the application.</param>
internal sealed partial class UpdateService(
    UpdateNotice notice,
    IHttpClientFactory clients,
    IHostApplicationLifetime lifetime,
    TimeProvider time,
    ILogger<UpdateService> logger,
    Action<string> announce) : BackgroundService
{
    private string? _announced;

    /// <summary>How long after a check the next one is made.</summary>
    internal TimeSpan Interval { get; init; } = TimeSpan.FromHours(24);

    /// <summary>
    /// Whether to check. <paramref name="setting"/> (<c>Pusula:CheckForUpdates</c>) decides when it is set; unset, the check is made outside
    /// Development only: <c>dotnet run</c> is for people who change pusula and start it again and again.
    /// </summary>
    /// <param name="setting">The value of <c>Pusula:CheckForUpdates</c>; null when it is not set.</param>
    /// <param name="isDevelopment">Whether the host runs in the Development environment.</param>
    public static bool ShouldCheck(bool? setting, bool isDevelopment) => setting ?? !isDevelopment;

    /// <summary>The line that says a newer version is out.</summary>
    /// <param name="answer">An answer whose state is <see cref="UpdateState.Newer"/>.</param>
    internal static string Line(UpdateResponse answer) => $"pusula: {answer.Latest} is out (this is {answer.Current}): {answer.Url}";

    /// <summary>Makes one check: asks, keeps what it found, and says so once for each newer version.</summary>
    /// <param name="cancellationToken">Ends the check when pusula stops.</param>
    internal async Task CheckAsync(CancellationToken cancellationToken)
    {
        using HttpClient client = clients.CreateClient(ReleaseLookup.ClientName);
        LookupResult result = await ReleaseLookup.LatestTagAsync(client, ReleaseLookup.Timeout, cancellationToken).ConfigureAwait(false);
        if (result.Tag is null)
        {
            notice.RecordFailure();
            LogFailed(result.Problem ?? "no answer", (int)Interval.TotalHours);
            return;
        }

        notice.RecordLatest(result.Tag);
        UpdateResponse answer = notice.Snapshot();
        if (answer.State == UpdateState.Newer && answer.Latest != _announced)
        {
            _announced = answer.Latest;
            announce(Line(answer));
        }
    }

    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!notice.Enabled)
        {
            return;
        }

        try
        {
            await WaitForStartAsync(stoppingToken).ConfigureAwait(false);
            using var timer = new PeriodicTimer(Interval, time);
            do
            {
                await CheckSafelyAsync(stoppingToken).ConfigureAwait(false);
            }
            while (await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false));
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // pusula is stopping.
        }
    }

    // Nothing that goes wrong in a check may end the loop, let alone the host (a background service that throws stops it by default).
    private async Task CheckSafelyAsync(CancellationToken stoppingToken)
    {
        try
        {
            await CheckAsync(stoppingToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (!stoppingToken.IsCancellationRequested)
        {
            string problem = $"{exception.GetType().Name}: {exception.Message}";
            LogFailed(problem, (int)Interval.TotalHours);
        }
    }

    private async Task WaitForStartAsync(CancellationToken stoppingToken)
    {
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using CancellationTokenRegistration whenStarted = lifetime.ApplicationStarted.Register(() => started.TrySetResult());
        using CancellationTokenRegistration whenStopped = stoppingToken.Register(() => started.TrySetCanceled(stoppingToken));
        await started.Task.ConfigureAwait(false);
    }
}
