using System.Text.Json.Nodes;

namespace Pusula.Indexing;

/// <summary>Typed reads from parsed frontmatter.</summary>
internal static class FrontmatterValues
{
    /// <summary>The value of <paramref name="key"/> when it is a string; null when missing or not a string.</summary>
    public static string? GetString(JsonObject? frontmatter, string key) =>
        frontmatter is not null
        && frontmatter.TryGetPropertyValue(key, out JsonNode? node)
        && node is JsonValue value
        && value.TryGetValue(out string? text)
            ? text
            : null;

    /// <summary>True when <paramref name="key"/> is present, whatever its value.</summary>
    public static bool Has(JsonObject? frontmatter, string key) => frontmatter?.ContainsKey(key) == true;

    /// <summary>True when the value of <paramref name="key"/> is the string <c>true</c> (case-insensitive).</summary>
    public static bool IsTrue(JsonObject? frontmatter, string key) =>
        string.Equals(GetString(frontmatter, key), "true", StringComparison.OrdinalIgnoreCase);
}
