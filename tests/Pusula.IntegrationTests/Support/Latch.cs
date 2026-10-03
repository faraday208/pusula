using Xunit;

namespace Pusula.IntegrationTests.Support;

/// <summary>
/// Holds something back in the middle of its work until a test lets it go. The code under test calls <see cref="Park"/>
/// (through a logger it logs to, for one); the test <see cref="Arm"/>s the latch first, waits with
/// <see cref="ParkedAsync"/> until the code got there, does what it wants to do meanwhile, and <see cref="Release"/>s
/// it. Only the first call after <see cref="Arm"/> waits; every other one goes through.
/// </summary>
internal sealed class Latch
{
    // Long enough for any test, short enough that a test which forgets to release it fails and does not hang.
    private static readonly TimeSpan Limit = TimeSpan.FromSeconds(30);

    private readonly TaskCompletionSource _parked = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource _released = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private int _armed;

    /// <summary>The next call to <see cref="Park"/> waits.</summary>
    public void Arm() => Volatile.Write(ref _armed, 1);

    /// <summary>Called by the code that is to be held back: waits for <see cref="Release"/> when the latch is armed, else returns at once.</summary>
    public void Park()
    {
        if (Interlocked.Exchange(ref _armed, 0) == 0)
        {
            return;
        }

        _parked.TrySetResult();
        _released.Task.Wait(Limit);
    }

    /// <summary>Completes when some code is held back by the latch.</summary>
    public Task ParkedAsync() => _parked.Task.WaitAsync(Limit, TestContext.Current.CancellationToken);

    /// <summary>Lets the code that is held back go on.</summary>
    public void Release() => _released.TrySetResult();
}
