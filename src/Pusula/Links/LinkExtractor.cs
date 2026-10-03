using System.Text.RegularExpressions;

namespace Pusula.Links;

/// <summary>
/// Finds references to other files in a Markdown body. The rules are shared with the browser client, which
/// matches the results by <c>Kind + "|" + Raw</c>: code fences are ignored, inline code is ignored for
/// Markdown links, wiki links and embeds, and path references are recognized in plain text and in inline code.
/// </summary>
internal static partial class LinkExtractor
{
    /// <summary>
    /// How long, in milliseconds, one pattern may work on one body before the match is abandoned with a
    /// <see cref="RegexMatchTimeoutException"/>. A body of the wrong shape, such as a very long line with a thousand
    /// or more backtick runs that are never closed, makes the inline code pattern run for seconds; this bounds the damage.
    /// </summary>
    internal const int MatchTimeoutMilliseconds = 1000;

    // The expressions are constants because the source generator and the run-time set (PatternSet.WithTimeout) share them.

    // Inline code span on a single line: a run of backticks, content, the same run again.
    private const string InlineCodeExpression = """(?<!`)(`+)(?!`)(.+?)(?<!`)\1(?!`)""";

    // [text](destination "title") — images ("![") are excluded. Group 1: <destination>, group 2: bare destination.
    private const string MarkdownLinkExpression = """(?<!!)\[(?:[^\[\]\n]|\[[^\[\]\n]*\])*\]\((?:<([^<>\n]*)>|([^()\s]+))(?:\s+(?:"[^"\n]*"|'[^'\n]*'))?\s*\)""";

    // [[target#heading|alias]] — embeds ("![[") are excluded. Group 1: inner text.
    private const string WikiLinkExpression = """(?<![!\[])\[\[([^\[\]\n]+?)\]\]""";

    // ![[target#heading|size]] — an embed. Group 1: inner text.
    private const string EmbedExpression = """!\[\[([^\[\]\n]+?)\]\]""";

    // ~/.claude/... — stops at delimiters and at '$' (a shell variable, so the path is only known up to there) and
    // never ends with '.' or ':'.
    private const string ClaudePathExpression = """~/\.claude/[^\s`'"<>()\[\]{}*|,;$]*[^\s`'"<>()\[\]{}*|,;$.:]""";

    // What MaskCodeAndLinks puts in place of code and links: neither a letter, a digit nor white space.
    private const char MaskPlaceholder = '\u0001';

    // A relative path: at least one "dir/" segment, optional leading "./".
    private const string RelativePathExpression = """^(?:\./)?(?:[\p{L}\p{N}_.\-]+/)+[\p{L}\p{N}_.\-]*$""";

    // A URI scheme such as "https:" or "mailto:".
    private const string SchemeExpression = """^[A-Za-z][A-Za-z0-9+.\-]*:""";

    [GeneratedRegex(InlineCodeExpression, RegexOptions.None, MatchTimeoutMilliseconds)]
    private static partial Regex GeneratedInlineCode();

    [GeneratedRegex(MarkdownLinkExpression, RegexOptions.None, MatchTimeoutMilliseconds)]
    private static partial Regex GeneratedMarkdownLink();

    [GeneratedRegex(WikiLinkExpression, RegexOptions.None, MatchTimeoutMilliseconds)]
    private static partial Regex GeneratedWikiLink();

    [GeneratedRegex(EmbedExpression, RegexOptions.None, MatchTimeoutMilliseconds)]
    private static partial Regex GeneratedEmbed();

    [GeneratedRegex(ClaudePathExpression, RegexOptions.None, MatchTimeoutMilliseconds)]
    private static partial Regex GeneratedClaudePath();

    [GeneratedRegex(RelativePathExpression, RegexOptions.None, MatchTimeoutMilliseconds)]
    private static partial Regex GeneratedRelativePath();

    [GeneratedRegex(SchemeExpression, RegexOptions.None, MatchTimeoutMilliseconds)]
    private static partial Regex GeneratedScheme();

    /// <summary>
    /// The regular expressions of one extraction. <see cref="Default"/> is what the application uses; tests build a
    /// set with a short timeout (<see cref="WithTimeout"/>) to exercise the timeout without waiting for it.
    /// </summary>
    internal sealed record PatternSet(Regex InlineCode, Regex MarkdownLink, Regex WikiLink, Regex ClaudePath, Regex RelativePath, Regex Scheme, Regex Embed)
    {
        /// <summary>The source-generated expressions, each with a timeout of <see cref="MatchTimeoutMilliseconds"/>.</summary>
        public static PatternSet Default { get; } = new(
            GeneratedInlineCode(),
            GeneratedMarkdownLink(),
            GeneratedWikiLink(),
            GeneratedClaudePath(),
            GeneratedRelativePath(),
            GeneratedScheme(),
            GeneratedEmbed());

        /// <summary>The same expressions, built at run time with another timeout (an attribute argument cannot be changed).</summary>
        /// <param name="matchTimeout">The timeout of every expression.</param>
        public static PatternSet WithTimeout(TimeSpan matchTimeout) => new(
            new Regex(InlineCodeExpression, RegexOptions.None, matchTimeout),
            new Regex(MarkdownLinkExpression, RegexOptions.None, matchTimeout),
            new Regex(WikiLinkExpression, RegexOptions.None, matchTimeout),
            new Regex(ClaudePathExpression, RegexOptions.None, matchTimeout),
            new Regex(RelativePathExpression, RegexOptions.None, matchTimeout),
            new Regex(SchemeExpression, RegexOptions.None, matchTimeout),
            new Regex(EmbedExpression, RegexOptions.None, matchTimeout));
    }

    /// <summary>
    /// Extracts every reference from <paramref name="body"/>, in document order. A body that makes a pattern run for
    /// longer than <see cref="MatchTimeoutMilliseconds"/> is not extracted at all: see <see cref="TryExtract"/>.
    /// </summary>
    /// <param name="body">The Markdown body (frontmatter excluded).</param>
    /// <param name="bodyStartLine">1-based line of the first body line in the file.</param>
    /// <param name="patterns">The expressions to use; null selects <see cref="PatternSet.Default"/>.</param>
    /// <exception cref="RegexMatchTimeoutException">A pattern did not finish within its timeout.</exception>
    public static IReadOnlyList<RawLink> Extract(string body, int bodyStartLine, PatternSet? patterns = null)
    {
        if (body.Length == 0)
        {
            return [];
        }

        patterns ??= PatternSet.Default;
        string fenceMasked = MaskFences(body);

        var codeSpans = new List<CodeSpan>();
        foreach (Match match in patterns.InlineCode.Matches(fenceMasked))
        {
            codeSpans.Add(new CodeSpan(match.Index, match.Length, match.Groups[2].Value.Trim()));
        }

        string linkMasked = Blank(fenceMasked, codeSpans.Select(span => (span.Index, span.Length)).ToList());

        var newlines = new List<int>();
        for (int i = body.IndexOf('\n'); i >= 0; i = body.IndexOf('\n', i + 1))
        {
            newlines.Add(i);
        }

        var found = new List<Found>();
        var linkSpans = new List<(int Index, int Length)>();

        // The patterns run on masked text, but the masks keep every position, so the reference text itself is
        // always taken from the original body ("as written", even when a code span sits inside it).
        foreach (Match match in patterns.MarkdownLink.Matches(linkMasked))
        {
            linkSpans.Add((match.Index, match.Length));
            string destination = TextOf(match.Groups[1].Success ? match.Groups[1] : match.Groups[2]);
            if (IsLocalDestination(destination, patterns))
            {
                found.Add(new Found(match.Index, new RawLink(LinkKind.MarkdownLink, destination, LineOf(match.Index))));
            }
        }

        foreach (Match match in patterns.WikiLink.Matches(linkMasked))
        {
            linkSpans.Add((match.Index, match.Length));
            found.Add(new Found(match.Index, new RawLink(LinkKind.WikiLink, TextOf(match.Groups[1]), LineOf(match.Index))));
        }

        foreach (Match match in patterns.Embed.Matches(linkMasked))
        {
            linkSpans.Add((match.Index, match.Length));
            found.Add(new Found(match.Index, new RawLink(LinkKind.Embed, TextOf(match.Groups[1]), LineOf(match.Index))));
        }

        // Paths inside links were already reported as links; inline code is deliberately not masked here
        // because paths are usually written in backticks.
        string claudeScan = Blank(fenceMasked, linkSpans);
        foreach (Match match in patterns.ClaudePath.Matches(claudeScan))
        {
            found.Add(new Found(match.Index, new RawLink(LinkKind.ClaudePath, match.Value, LineOf(match.Index))));
        }

        foreach (CodeSpan span in codeSpans)
        {
            if (IsRelativePath(span.Content, patterns))
            {
                found.Add(new Found(span.Index, new RawLink(LinkKind.RelativePath, span.Content, LineOf(span.Index))));
            }
        }

        return found
            .OrderBy(item => item.Index)
            .ThenBy(item => item.Link.Kind)
            .Select(item => item.Link)
            .ToList();

        int LineOf(int index)
        {
            int position = newlines.BinarySearch(index);
            return bodyStartLine + (position >= 0 ? position : ~position);
        }

        string TextOf(Group group) => body.Substring(group.Index, group.Length);
    }

    /// <summary>
    /// Like <see cref="Extract"/>, but a body that makes a pattern run out of time gives no links and <c>false</c>
    /// instead of an exception: such a body is skipped, it does not stop whoever is extracting.
    /// </summary>
    /// <param name="body">The Markdown body (frontmatter excluded).</param>
    /// <param name="bodyStartLine">1-based line of the first body line in the file.</param>
    /// <param name="links">The references, in document order; empty when the result is <c>false</c>.</param>
    /// <param name="patterns">The expressions to use; null selects <see cref="PatternSet.Default"/>.</param>
    /// <returns><c>false</c> when a pattern did not finish within its timeout.</returns>
    public static bool TryExtract(string body, int bodyStartLine, out IReadOnlyList<RawLink> links, PatternSet? patterns = null)
    {
        try
        {
            links = Extract(body, bodyStartLine, patterns);
            return true;
        }
        catch (RegexMatchTimeoutException)
        {
            links = [];
            return false;
        }
    }

    /// <summary>
    /// The body with everything blanked in which a <c>#tag</c> does not count: fenced code blocks, inline code and
    /// the text of Markdown links, wiki links and embeds. Every position is kept, and what is blanked is not white
    /// space (so a tag that follows a code span is still not preceded by a space).
    /// </summary>
    /// <param name="body">The Markdown body (frontmatter excluded).</param>
    /// <param name="patterns">The expressions to use; null selects <see cref="PatternSet.Default"/>.</param>
    /// <exception cref="RegexMatchTimeoutException">A pattern did not finish within its timeout.</exception>
    internal static string MaskCodeAndLinks(string body, PatternSet? patterns = null)
    {
        if (body.Length == 0)
        {
            return body;
        }

        patterns ??= PatternSet.Default;
        string fenceMasked = MaskFences(body);

        var spans = new List<(int Index, int Length)>();
        foreach (Match match in patterns.InlineCode.Matches(fenceMasked))
        {
            spans.Add((match.Index, match.Length));
        }

        // The links are looked for outside inline code, as in Extract.
        string linkMasked = Blank(fenceMasked, spans);
        foreach (Regex pattern in new[] { patterns.MarkdownLink, patterns.WikiLink, patterns.Embed })
        {
            foreach (Match match in pattern.Matches(linkMasked))
            {
                spans.Add((match.Index, match.Length));
            }
        }

        return Blank(fenceMasked, spans, MaskPlaceholder);
    }

    /// <summary>
    /// Replaces every character of fenced code blocks (opening line through closing line) with a space, keeping
    /// line breaks. An opening fence is 0–3 spaces followed by three or more backticks or tildes; it is closed by
    /// a line with at least as many of the same character followed only by whitespace. An unclosed fence runs to
    /// the end of the text.
    /// </summary>
    internal static string MaskFences(string text)
    {
        char[]? buffer = null;
        bool inFence = false;
        char fenceChar = '\0';
        int fenceLength = 0;

        int position = 0;
        while (position < text.Length)
        {
            int newline = text.IndexOf('\n', position);
            int lineEnd = newline < 0 ? text.Length : newline;
            ReadOnlySpan<char> line = text.AsSpan(position, lineEnd - position);

            bool masked = inFence;
            if (inFence)
            {
                inFence = !IsClosingFence(line, fenceChar, fenceLength);
            }
            else if (TryGetOpeningFence(line, out fenceChar, out fenceLength))
            {
                inFence = true;
                masked = true;
            }

            if (masked)
            {
                buffer ??= text.ToCharArray();
                BlankRange(buffer.AsSpan(position, lineEnd - position));
            }

            position = newline < 0 ? text.Length : newline + 1;
        }

        return buffer is null ? text : new string(buffer);
    }

    private static bool TryGetOpeningFence(ReadOnlySpan<char> line, out char fenceChar, out int length)
    {
        fenceChar = '\0';
        length = 0;

        int indent = LeadingSpaces(line);
        if (indent > 3 || indent >= line.Length || line[indent] is not ('`' or '~'))
        {
            return false;
        }

        char candidate = line[indent];
        int run = RunLength(line, indent, candidate);
        if (run < 3)
        {
            return false;
        }

        fenceChar = candidate;
        length = run;
        return true;
    }

    private static bool IsClosingFence(ReadOnlySpan<char> line, char fenceChar, int minimumLength)
    {
        int indent = LeadingSpaces(line);
        if (indent > 3)
        {
            return false;
        }

        int run = RunLength(line, indent, fenceChar);
        return run >= minimumLength && line[(indent + run)..].IsWhiteSpace();
    }

    private static int LeadingSpaces(ReadOnlySpan<char> line)
    {
        int count = 0;
        while (count < line.Length && line[count] == ' ')
        {
            count++;
        }

        return count;
    }

    private static int RunLength(ReadOnlySpan<char> line, int start, char value)
    {
        int count = 0;
        while (start + count < line.Length && line[start + count] == value)
        {
            count++;
        }

        return count;
    }

    private static string Blank(string text, List<(int Index, int Length)> spans, char fill = ' ')
    {
        if (spans.Count == 0)
        {
            return text;
        }

        char[] buffer = text.ToCharArray();
        foreach ((int index, int length) in spans)
        {
            BlankRange(buffer.AsSpan(index, length), fill);
        }

        return new string(buffer);
    }

    private static void BlankRange(Span<char> range, char fill = ' ')
    {
        for (int i = 0; i < range.Length; i++)
        {
            if (range[i] is not ('\n' or '\r'))
            {
                range[i] = fill;
            }
        }
    }

    // Anchors ("#...") and foreign schemes (http:, https:, mailto:, ...) are not references to files.
    // "file:" is kept: it can point into the configuration root.
    private static bool IsLocalDestination(string destination, PatternSet patterns) =>
        !string.IsNullOrWhiteSpace(destination)
        && destination[0] != '#'
        && (destination.StartsWith("file:", StringComparison.OrdinalIgnoreCase) || !patterns.Scheme.IsMatch(destination));

    // The pattern cannot match a leading '~' or '/', so only ".." needs an explicit check.
    private static bool IsRelativePath(string content, PatternSet patterns) =>
        !content.StartsWith("..", StringComparison.Ordinal) && patterns.RelativePath.IsMatch(content);

    private readonly record struct CodeSpan(int Index, int Length, string Content);

    private readonly record struct Found(int Index, RawLink Link);
}
