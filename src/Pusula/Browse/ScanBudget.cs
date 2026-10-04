namespace Pusula.Browse;

/// <summary>
/// What one scan may still read: so many file system entries, within so much time. The scan asks for every entry it
/// comes across; once the budget is used up it says no for ever, and the scan stops with what it has. The time is looked at
/// at every ask, so a scan that asks for each entry it reads is stopped by the time in the middle of a folder, however big the
/// folder is. Not for use from several threads.
/// </summary>
internal sealed class ScanBudget
{
    private readonly int _maxEntries;
    private readonly TimeSpan _maxTime;
    private readonly TimeProvider _time;
    private readonly long _started;
    private int _entries;

    /// <summary>Starts the clock.</summary>
    /// <param name="maxEntries">The most entries that may be read.</param>
    /// <param name="maxTime">The longest the scan may take; a scan that is given none (zero) reads nothing.</param>
    /// <param name="time">The clock the time is told by; the system's unless a test gives its own, so that none has to wait for time to pass.</param>
    public ScanBudget(int maxEntries, TimeSpan maxTime, TimeProvider? time = null)
    {
        _maxEntries = maxEntries;
        _maxTime = maxTime;
        _time = time ?? TimeProvider.System;
        _started = _time.GetTimestamp();
    }

    /// <summary>True once an entry was asked for beyond the budget (too many entries, or too much time).</summary>
    public bool IsSpent { get; private set; }

    /// <summary>How many entries have been taken so far: the asks that were answered yes. For the log.</summary>
    public int Taken => IsSpent ? _entries - 1 : _entries;

    /// <summary>What used the budget up, once it is: <c>entries</c> or <c>time</c>; null before that. For the log.</summary>
    public string? SpentBy { get; private set; }

    /// <summary>How long after the budget was started it was used up: the moment of the ask that was refused; zero before that. For the log.</summary>
    public TimeSpan SpentAt { get; private set; }

    /// <summary>Takes one entry from the budget.</summary>
    /// <returns><c>false</c> when the budget is used up: this entry is not to be read, nor any after it.</returns>
    public bool TrySpend()
    {
        if (IsSpent)
        {
            return false;
        }

        if (++_entries > _maxEntries)
        {
            IsSpent = true;
            SpentBy = "entries";
            SpentAt = _time.GetElapsedTime(_started);
            return false;
        }

        TimeSpan elapsed = _time.GetElapsedTime(_started);
        if (elapsed >= _maxTime)
        {
            IsSpent = true;
            SpentBy = "time";
            SpentAt = elapsed;
            return false;
        }

        return true;
    }
}
