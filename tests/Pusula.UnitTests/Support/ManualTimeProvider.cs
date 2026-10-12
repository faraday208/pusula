namespace Pusula.UnitTests.Support;

/// <summary>
/// A clock that only moves when a test says so: for what depends on how old something is, or on what day it is. A test can give the
/// time zone too, so that the local day does not depend on the machine the test runs on.
/// </summary>
internal sealed class ManualTimeProvider : TimeProvider
{
    private readonly TimeZoneInfo? _localTimeZone;
    private DateTimeOffset _now;

    /// <summary>A clock at 12:00 UTC on 2 October 2026, in the time zone of the machine.</summary>
    public ManualTimeProvider()
        : this(new DateTimeOffset(2026, 10, 2, 12, 0, 0, TimeSpan.Zero))
    {
    }

    /// <summary>A clock at a given moment.</summary>
    /// <param name="now">The moment.</param>
    /// <param name="localTimeZone">The local time zone of the clock; the time zone of the machine when left out.</param>
    public ManualTimeProvider(DateTimeOffset now, TimeZoneInfo? localTimeZone = null)
    {
        _now = now;
        _localTimeZone = localTimeZone;
    }

    /// <inheritdoc />
    public override TimeZoneInfo LocalTimeZone => _localTimeZone ?? base.LocalTimeZone;

    /// <inheritdoc />
    public override DateTimeOffset GetUtcNow() => _now;

    /// <summary>Moves the clock on.</summary>
    /// <param name="by">How far.</param>
    public void Advance(TimeSpan by) => _now += by;
}
