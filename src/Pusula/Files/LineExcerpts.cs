namespace Pusula.Files;

/// <summary>
/// Reads the lines of one text as short excerpts. Where the lines start is found once, when the object is created, so
/// that reading a line costs nothing more: a file can link to the same target from thousands of lines, and each of
/// those lines is read for the response.
/// </summary>
internal sealed class LineExcerpts
{
    /// <summary>The most characters an excerpt has, the ellipsis of a cut one included.</summary>
    internal const int MaxLength = 200;

    private const string Ellipsis = "…";

    // The offset of the first character of every line: line n starts at _starts[n - 1].
    private readonly List<int> _starts = [0];
    private readonly string _text;

    /// <summary>Finds the lines of <paramref name="text"/>. A line ends at <c>\n</c>, the way every line number in the index is counted.</summary>
    /// <param name="text">The text, for example the whole content of a file.</param>
    public LineExcerpts(string text)
    {
        _text = text;
        for (int newline = text.IndexOf('\n'); newline >= 0; newline = text.IndexOf('\n', newline + 1))
        {
            _starts.Add(newline + 1);
        }
    }

    /// <summary>
    /// The text of a line without the whitespace around it (so without a <c>\r</c> either). A line of more than
    /// <see cref="MaxLength"/> characters is cut so that the result, with <c>…</c> at its end, has exactly that many; the cut never
    /// falls inside a surrogate pair, which could not be written as JSON.
    /// </summary>
    /// <param name="line">1-based line number.</param>
    /// <returns>The excerpt; null when the text has no such line or the line is blank.</returns>
    public string? Of(int line)
    {
        if (line < 1 || line > _starts.Count)
        {
            return null;
        }

        int start = _starts[line - 1];
        int end = line < _starts.Count ? _starts[line] - 1 : _text.Length;
        ReadOnlySpan<char> text = _text.AsSpan(start, end - start).Trim();
        if (text.IsEmpty)
        {
            return null;
        }

        if (text.Length <= MaxLength)
        {
            return text.ToString();
        }

        int keep = MaxLength - Ellipsis.Length;
        if (char.IsHighSurrogate(text[keep - 1]))
        {
            keep--;
        }

        return string.Concat(text[..keep], Ellipsis);
    }
}
