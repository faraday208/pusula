namespace Pusula.IntegrationTests.Support;

/// <summary>Runs things at the same moment, to see what happens when they overlap.</summary>
internal static class Race
{
    private static readonly TimeSpan Limit = TimeSpan.FromSeconds(10);

    /// <summary>
    /// Starts every action on a thread of its own, all at once (after a short pause of a different length for each,
    /// so that the same overlap is not tried every time), and waits for all of them. Fails with what any of them threw.
    /// An action that blocks does not hold up the others, nor the thread pool the others finish on.
    /// </summary>
    /// <param name="actions">What to run.</param>
    public static async Task RunAsync(params Func<Task>[] actions)
    {
        using var start = new Barrier(actions.Length);
        Task[] running =
        [
            .. actions.Select(action => Task.Factory.StartNew(
                () =>
                {
                    start.SignalAndWait(Limit);
                    Thread.SpinWait(Random.Shared.Next(0, 4000));
                    return action();
                },
                CancellationToken.None,
                TaskCreationOptions.LongRunning,
                TaskScheduler.Default).Unwrap()),
        ];

        await Task.WhenAll(running);
    }
}
