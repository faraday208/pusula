using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Pusula.Links;

namespace Pusula.Indexing;

/// <summary>Finds the tags of a note: the <c>tags</c> (or <c>tag</c>) of its frontmatter and the <c>#tags</c> in its text.</summary>
internal static partial class NoteTags
{
    // A tag in the text, as Obsidian reads it: a "#" after the start of a line or after white space, then letters,
    // digits, "_", "-" and "/", of which at least one is not a digit ("#1984" is a number, "#y1984" a tag). "# Title"
    // is a heading: the "#" is followed by a space. Code and links are blanked before this runs.
    private const string InlineTagExpression = """(?<![^\s])#(?=[\p{L}\p{N}_/\-]*[\p{L}_/\-])([\p{L}\p{N}_/\-]+)""";

    private static readonly string[] FrontmatterKeys = ["tags", "tag"];

    private static readonly char[] TextSeparators = [',', ' ', '\t', '\r', '\n'];

    [GeneratedRegex(InlineTagExpression, RegexOptions.None, LinkExtractor.MatchTimeoutMilliseconds)]
    private static partial Regex GeneratedInlineTag();

    /// <summary>
    /// The tags of a note, without the leading <c>#</c>, each once (ignoring case; the first spelling is kept): first
    /// those of the frontmatter, then those of the body in the order they are written.
    /// </summary>
    /// <param name="frontmatter">The parsed frontmatter, if any.</param>
    /// <param name="body">The Markdown body (frontmatter excluded).</param>
    /// <param name="tags">The tags. When the result is <c>false</c> only those that were found before the body ran out of time.</param>
    /// <param name="patterns">The link expressions that blank code and links; null selects the application's own.</param>
    /// <returns><c>false</c> when an expression did not finish within its timeout.</returns>
    public static bool TryExtract(JsonObject? frontmatter, string body, out IReadOnlyList<string> tags, LinkExtractor.PatternSet? patterns = null)
    {
        var found = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        AddFrontmatterTags(frontmatter, found, seen);

        bool completed = true;
        try
        {
            foreach (Match match in GeneratedInlineTag().Matches(LinkExtractor.MaskCodeAndLinks(body, patterns)))
            {
                Add(found, seen, match.Groups[1].Value);
            }
        }
        catch (RegexMatchTimeoutException)
        {
            completed = false;
        }

        tags = found;
        return completed;
    }

    // "tags" and "tag" hold a list or one text; a text is split at commas and white space.
    private static void AddFrontmatterTags(JsonObject? frontmatter, List<string> found, HashSet<string> seen)
    {
        if (frontmatter is null)
        {
            return;
        }

        foreach (string key in FrontmatterKeys)
        {
            if (!frontmatter.TryGetPropertyValue(key, out JsonNode? node))
            {
                continue;
            }

            if (node is JsonArray array)
            {
                foreach (JsonNode? item in array)
                {
                    if (item is JsonValue entry && entry.TryGetValue(out string? text))
                    {
                        Add(found, seen, text);
                    }
                }
            }
            else if (node is JsonValue value && value.TryGetValue(out string? text))
            {
                foreach (string part in text.Split(TextSeparators, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                {
                    Add(found, seen, part);
                }
            }
        }
    }

    private static void Add(List<string> found, HashSet<string> seen, string text)
    {
        string tag = text.Trim().TrimStart('#');
        if (tag.Length > 0 && seen.Add(tag))
        {
            found.Add(tag);
        }
    }
}
