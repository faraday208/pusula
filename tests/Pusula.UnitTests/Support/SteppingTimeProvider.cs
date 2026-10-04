namespace Pusula.UnitTests.Support;

/// <summary>
/// A clock that moves on by a step each time it is read and by nothing else: time passes with the work that is done, as it does for real
/// (a scan that asks the budget for every entry reads the clock once for each), and no test has to wait for it.
/// </summary>
/// <param name="step">How far the clock moves on at every reading.</param>
internal sealed class SteppingTimeProvider(TimeSpan step) : TimeProvider
{
    private long _ticks;

    /// <summary>How many times the clock was read.</summary>
    public int Reads { get; private set; }

    /// <inheritdoc />
    public override long TimestampFrequency => TimeSpan.TicksPerSecond;

    /// <inheritdoc />
    public override long GetTimestamp()
    {
        Reads++;
        _ticks += step.Ticks;
        return _ticks;
    }
}
