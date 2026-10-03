using System.Diagnostics;

namespace Pusula.Browse;

/// <summary>
/// What one scan may still read: so many file system entries, within so much time. The scan asks for every entry it
/// comes across; once the budget is used up it says no for ever, and the scan stops with what it has. Not for use from
/// several threads.
/// </summary>
internal sealed class ScanBudget
{
    private readonly int _maxEntries;
    private readonly TimeSpan _maxTime;
    private readonly long _started = Stopwatch.GetTimestamp();
    private int _entries;

    /// <summary>Starts the clock.</summary>
    /// <param name="maxEntries">The most entries that may be read.</param>
    /// <param name="maxTime">The longest the scan may take; a scan that is given none (zero) reads nothing.</param>
    public ScanBudget(int maxEntries, TimeSpan maxTime)
    {
        _maxEntries = maxEntries;
        _maxTime = maxTime;
    }

    /// <summary>True once an entry was asked for beyond the budget (too many entries, or too much time).</summary>
    public bool IsSpent { get; private set; }

    /// <summary>Takes one entry from the budget.</summary>
    /// <returns><c>false</c> when the budget is used up: this entry is not to be read, nor any after it.</returns>
    public bool TrySpend()
    {
        if (IsSpent)
        {
            return false;
        }

        if (++_entries > _maxEntries || Stopwatch.GetElapsedTime(_started) >= _maxTime)
        {
            IsSpent = true;
            return false;
        }

        return true;
    }
}
