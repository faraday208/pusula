using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using YamlDotNet.Core;
using YamlDotNet.RepresentationModel;

namespace Pusula.Indexing;

/// <summary>Splits a Markdown file into YAML frontmatter and body.</summary>
internal static partial class FrontmatterParser
{
    // Defence against YAML bombs and extreme nesting in hostile or broken files. YamlDotNet recurses once per nesting
    // level: measured with YamlDotNet 18.1.0, about 5,000 nested flow collections ("[[[...") overflow the stack of a
    // thread with a 1.5 MB stack and abort the process. MaxYamlLength and MaxFlowDepth are checked before YamlDotNet
    // sees the text; MaxNodeDepth and MaxNodes while the parsed tree is converted (aliases can expand it).
    private const int MaxYamlLength = 64 * 1024;
    private const int MaxFlowDepth = 64;
    private const int MaxNodeDepth = 32;
    private const int MaxNodes = 10_000;

    // Top-level "key: value" line, used when the YAML as a whole cannot be parsed.
    [GeneratedRegex("""^([\p{L}_][\p{L}\p{N}_\-]*):[ \t]*(.*)$""")]
    private static partial Regex TopLevelEntry();

    /// <summary>
    /// Parses <paramref name="text"/>. Frontmatter is a block that starts on the first line with <c>---</c> and ends at
    /// the first line that is exactly <c>---</c> or <c>...</c>; without a closing line there is no frontmatter.
    /// </summary>
    public static FrontmatterResult Parse(string text)
    {
        if (!TryLocateBlock(text, out string yaml, out int bodyStart, out int bodyStartLine))
        {
            return new FrontmatterResult(null, null, null, text, 1);
        }

        (JsonObject? frontmatter, string? error, int? errorLine) = ParseYaml(yaml);
        return new FrontmatterResult(frontmatter, error, errorLine, text[bodyStart..], bodyStartLine);
    }

    private static bool TryLocateBlock(string text, out string yaml, out int bodyStart, out int bodyStartLine)
    {
        yaml = string.Empty;
        bodyStart = 0;
        bodyStartLine = 1;

        int openLength;
        if (text.StartsWith("---\n", StringComparison.Ordinal))
        {
            openLength = 4;
        }
        else if (text.StartsWith("---\r\n", StringComparison.Ordinal))
        {
            openLength = 5;
        }
        else
        {
            return false;
        }

        int lineStart = openLength;
        int lineNumber = 2;
        while (true)
        {
            int newline = text.IndexOf('\n', lineStart);
            int lineEnd = newline < 0 ? text.Length : newline;
            int contentEnd = lineEnd > lineStart && text[lineEnd - 1] == '\r' ? lineEnd - 1 : lineEnd;

            ReadOnlySpan<char> content = text.AsSpan(lineStart, contentEnd - lineStart);
            if (content is "---" or "...")
            {
                yaml = text[openLength..lineStart];
                bodyStart = newline < 0 ? text.Length : newline + 1;
                bodyStartLine = lineNumber + 1;
                return true;
            }

            if (newline < 0)
            {
                return false;
            }

            lineStart = newline + 1;
            lineNumber++;
        }
    }

    // The line is that of the file, and null for an error that has no position: only a YamlException knows where it is.
    private static (JsonObject? Frontmatter, string? Error, int? ErrorLine) ParseYaml(string yaml)
    {
        if (yaml.Length > MaxYamlLength)
        {
            return (ParseTopLevelEntries(yaml), $"Frontmatter is longer than {MaxYamlLength} characters.", null);
        }

        if (ExceedsFlowDepth(yaml))
        {
            return (ParseTopLevelEntries(yaml), "Frontmatter is nested too deeply.", null);
        }

        try
        {
            var stream = new YamlStream();
            stream.Load(new StringReader(yaml));

            // An empty block (or one with only comments) has no documents: it is an empty mapping.
            if (stream.Documents.Count == 0)
            {
                return (new JsonObject(), null, null);
            }

            if (stream.Documents[0].RootNode is not YamlMappingNode mapping)
            {
                return (null, "Frontmatter is not a mapping", null);
            }

            int budget = MaxNodes;
            return (ConvertMapping(mapping, 0, ref budget), null, null);
        }
        catch (YamlException exception)
        {
            return (ParseTopLevelEntries(yaml), Describe(exception), FileLine(exception));
        }
        catch (Exception exception) when (exception is InvalidOperationException or ArgumentException)
        {
            // YamlDotNet throws these (not YamlException) for some malformed input, such as an unclosed flow sequence.
            return (ParseTopLevelEntries(yaml), $"Invalid YAML: {exception.Message}", null);
        }
    }

    // A cheap over-approximation: brackets inside quoted scalars count too, which is harmless at this limit.
    private static bool ExceedsFlowDepth(string yaml)
    {
        int depth = 0;
        foreach (char c in yaml)
        {
            if (c is '[' or '{')
            {
                if (++depth > MaxFlowDepth)
                {
                    return true;
                }
            }
            else if (c is ']' or '}' && depth > 0)
            {
                depth--;
            }
        }

        return false;
    }

    private static JsonObject ConvertMapping(YamlMappingNode mapping, int depth, ref int budget)
    {
        CountNode(mapping, depth, ref budget);

        var result = new JsonObject();
        foreach (KeyValuePair<YamlNode, YamlNode> entry in mapping.Children)
        {
            if (entry.Key is not YamlScalarNode key)
            {
                throw new YamlException(entry.Key.Start, entry.Key.End, "Frontmatter keys must be plain scalars.");
            }

            result[key.Value ?? string.Empty] = ConvertNode(entry.Value, depth + 1, ref budget);
        }

        return result;
    }

    private static JsonNode ConvertNode(YamlNode node, int depth, ref int budget)
    {
        switch (node)
        {
            case YamlMappingNode mapping:
                return ConvertMapping(mapping, depth, ref budget);

            case YamlSequenceNode sequence:
                CountNode(sequence, depth, ref budget);
                var array = new JsonArray();
                foreach (YamlNode child in sequence.Children)
                {
                    array.Add(ConvertNode(child, depth + 1, ref budget));
                }

                return array;

            default:
                CountNode(node, depth, ref budget);

                // Scalars stay strings: "true", "42" and "null" are not interpreted.
                return JsonValue.Create(node is YamlScalarNode scalar ? scalar.Value ?? string.Empty : string.Empty)!;
        }
    }

    // Anchors and aliases can make the node graph cyclic or exponentially large once it is expanded into a tree.
    private static void CountNode(YamlNode node, int depth, ref int budget)
    {
        if (depth > MaxNodeDepth || --budget < 0)
        {
            throw new YamlException(node.Start, node.End, "Frontmatter is too complex.");
        }
    }

    // YamlDotNet counts lines from the start of the YAML block; the file's line number is one higher because
    // the opening "---" is line 1. The message and the line that is reported next to it both come from here.
    // YamlDotNet's line is a long, but no more than MaxYamlLength characters are parsed, so it always fits an int.
    private static int FileLine(YamlException exception) => (int)exception.Start.Line + 1;

    private static string Describe(YamlException exception) =>
        $"Line {FileLine(exception)}, column {exception.Start.Column}: {exception.Message}";

    private static JsonObject ParseTopLevelEntries(string yaml)
    {
        var result = new JsonObject();
        foreach (string line in yaml.Split('\n'))
        {
            Match match = TopLevelEntry().Match(line.TrimEnd('\r'));
            if (match.Success)
            {
                result[match.Groups[1].Value] = StripQuotes(match.Groups[2].Value.Trim());
            }
        }

        return result;
    }

    private static string StripQuotes(string value) =>
        value.Length >= 2 && (value[0] is '"' or '\'') && value[^1] == value[0] ? value[1..^1] : value;
}
