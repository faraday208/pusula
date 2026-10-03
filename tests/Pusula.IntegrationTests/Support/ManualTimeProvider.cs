namespace Pusula.IntegrationTests.Support;

/// <summary>A clock that only moves when a test says so: for what depends on how old something is.</summary>
internal sealed class ManualTimeProvider : TimeProvider
{
    private DateTimeOffset _now = new(2026, 10, 2, 12, 0, 0, TimeSpan.Zero);

    public override DateTimeOffset GetUtcNow() => _now;

    /// <summary>Moves the clock on.</summary>
    /// <param name="by">How far.</param>
    public void Advance(TimeSpan by) => _now += by;
}
