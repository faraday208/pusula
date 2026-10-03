using System.Text;

namespace Pusula.UnitTests.Support;

/// <summary>Markdown bodies that are hostile to the link patterns: one very long line of a shape that makes them work hard.</summary>
internal static class PathologicalMarkdown
{
    /// <summary>
    /// Runs of 1, 2, 3, ... backticks, each followed by <paramref name="separator"/>. No run has a partner of its own
    /// length, so none of them closes, and the inline code pattern scans to the end of the line from every one.
    /// </summary>
    /// <param name="length">The number of characters of the result.</param>
    /// <param name="separator">What follows each run.</param>
    public static string UnclosedBacktickRuns(int length, string separator)
    {
        var text = new StringBuilder(length + 64);
        for (int run = 1; text.Length < length; run++)
        {
            text.Append('`', run).Append(separator);
        }

        return text.ToString(0, length);
    }

    /// <summary><paramref name="unit"/> repeated, cut to <paramref name="length"/> characters.</summary>
    /// <param name="unit">The text to repeat.</param>
    /// <param name="length">The number of characters of the result.</param>
    public static string Repeat(string unit, int length)
    {
        var text = new StringBuilder(length + unit.Length);
        while (text.Length < length)
        {
            text.Append(unit);
        }

        return text.ToString(0, length);
    }
}
