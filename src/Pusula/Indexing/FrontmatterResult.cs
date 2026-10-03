using System.Text.Json.Nodes;

namespace Pusula.Indexing;

/// <summary>The outcome of splitting a Markdown file into frontmatter and body.</summary>
/// <param name="Frontmatter">
/// The parsed frontmatter. Null when the file has none or the YAML is not a mapping; the best-effort top-level
/// keys when the YAML is malformed. Scalar values are always strings.
/// </param>
/// <param name="Error">Why the frontmatter could not be parsed cleanly, if it could not.</param>
/// <param name="ErrorLine">
/// 1-based line in the file (the opening <c>---</c> is line 1) that <paramref name="Error"/> names, as the message
/// does. Null when there is no error or the error has no position (an oversized, too deeply nested or non-mapping
/// block, or YAML that failed without a position).
/// </param>
/// <param name="Body">Everything after the closing <c>---</c> line (the whole text when there is no frontmatter).</param>
/// <param name="BodyStartLine">1-based line of the first body line in the file.</param>
internal readonly record struct FrontmatterResult(JsonObject? Frontmatter, string? Error, int? ErrorLine, string Body, int BodyStartLine);
