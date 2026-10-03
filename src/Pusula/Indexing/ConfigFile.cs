using System.Text.Json.Nodes;
using Pusula.Links;

namespace Pusula.Indexing;

/// <summary>One indexed Markdown file.</summary>
/// <param name="Path">Index key: root-relative path with <c>/</c> separators.</param>
/// <param name="Name">File name, extension included.</param>
/// <param name="Layer">The role of the file.</param>
/// <param name="LoadMode">When Claude Code loads the file.</param>
/// <param name="Tokens">Estimated token counts.</param>
/// <param name="Frontmatter">Parsed frontmatter; null when there is none (see <see cref="FrontmatterResult"/>).</param>
/// <param name="FrontmatterError">Why the frontmatter is malformed, if it is.</param>
/// <param name="FrontmatterErrorLine">1-based line in the file that <paramref name="FrontmatterError"/> names; null when there is no error or it has no known line (see <see cref="FrontmatterResult"/>).</param>
/// <param name="Content">The whole file text.</param>
/// <param name="Body">The text after the frontmatter.</param>
/// <param name="BodyStartLine">1-based line of the first body line in the file.</param>
/// <param name="Links">Outgoing references, in document order.</param>
/// <param name="IsOrphan">
/// True for a memory, reference, shared, skill-resource or other file that no other file links to. A note of a vault or a
/// plain Markdown folder is an orphan only when it has no links at all (as in the graph of Obsidian): no other note links
/// to it and it links to no other note.
/// </param>
/// <param name="ModifiedAt">When the file was last written (UTC), as the file system says. Not part of what makes an index change (see <see cref="IndexDiff"/>); the default (the year 1) for an index that was made by hand.</param>
internal sealed record ConfigFile(
    string Path,
    string Name,
    Layer Layer,
    LoadMode LoadMode,
    TokenCounts Tokens,
    JsonObject? Frontmatter,
    string? FrontmatterError,
    int? FrontmatterErrorLine,
    string Content,
    string Body,
    int BodyStartLine,
    IReadOnlyList<Link> Links,
    bool IsOrphan,
    DateTimeOffset ModifiedAt = default)
{
    /// <summary>The tags of the note (frontmatter first, then the body), without the leading <c>#</c>; empty for every file that is not a note.</summary>
    public IReadOnlyList<string> Tags { get; init; } = [];
}
