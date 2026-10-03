using System.Text.Json.Nodes;
using Pusula.Indexing;
using Pusula.Links;

namespace Pusula.Files;

/// <summary>One indexed Markdown file: its frontmatter and body, the links it contains and the links that point at it.</summary>
/// <param name="Path">Index key of the file: its path relative to the root, separated by <c>/</c>.</param>
/// <param name="Name">File name, extension included.</param>
/// <param name="Layer">The role of the file.</param>
/// <param name="LoadMode">When Claude Code loads the file.</param>
/// <param name="Tokens">Estimated token counts.</param>
/// <param name="Body">The Markdown after the frontmatter.</param>
/// <param name="BodyStartLine">1-based line in the file of the first line of <paramref name="Body"/>.</param>
/// <param name="Links">References from this file to other files, in document order.</param>
/// <param name="Backlinks">References from other files to this file, ordered by source and line.</param>
/// <param name="Orphan">True when the file is of a kind that should be linked to, and no other file links to it. A note of a vault or a plain Markdown folder is an orphan only when it also links to no other note.</param>
/// <param name="Version">Version of the index the response was built from.</param>
/// <param name="Frontmatter">The parsed frontmatter as a JSON object (scalar values are strings); left out when the file has none.</param>
/// <param name="FrontmatterError">Why the frontmatter is malformed; left out when it is fine.</param>
/// <param name="FrontmatterErrorLine">1-based line in the file that <paramref name="FrontmatterError"/> names, counted from the top of the file (the opening <c>---</c> is line 1); left out when the frontmatter is fine or the error has no known line.</param>
/// <param name="FrontmatterErrorText">The text of that line: as written, without the whitespace around it, and at most 200 characters (a longer line is cut and ends with <c>…</c>). Left out when there is no line or the line is blank.</param>
/// <param name="Tags">The tags of the note (frontmatter first, then the body), without the leading <c>#</c>; left out when it has none (and for every file of a Claude Code folder).</param>
public sealed record FileResponse(
    string Path,
    string Name,
    Layer Layer,
    LoadMode LoadMode,
    FileTokens Tokens,
    string Body,
    int BodyStartLine,
    IReadOnlyList<FileLink> Links,
    IReadOnlyList<FileBacklink> Backlinks,
    bool Orphan,
    long Version,
    JsonObject? Frontmatter = null,
    string? FrontmatterError = null,
    int? FrontmatterErrorLine = null,
    string? FrontmatterErrorText = null,
    IReadOnlyList<string>? Tags = null);

/// <summary>Estimated token counts of one file (about one token per four characters).</summary>
/// <param name="Total">Tokens of the whole file.</param>
/// <param name="EverySession">Tokens the file adds to every session.</param>
/// <param name="ProjectSession">Tokens the file adds to every session of its own project.</param>
public sealed record FileTokens(int Total, int EverySession, int ProjectSession);

/// <summary>A reference from the file to another file or directory.</summary>
/// <param name="Kind">The syntax the reference was written in.</param>
/// <param name="Raw">The reference text as written (clients match links on <c>kind|raw</c>).</param>
/// <param name="Line">1-based line of the reference in the file, frontmatter lines included.</param>
/// <param name="Status">What the reference points at.</param>
/// <param name="Target">Path of the target relative to the root; left out when there is none.</param>
/// <param name="Heading">The heading part of the reference; left out when it has none.</param>
public sealed record FileLink(LinkKind Kind, string Raw, int Line, LinkStatus Status, string? Target = null, string? Heading = null);

/// <summary>A reference from another file to this file.</summary>
/// <param name="Source">Path of the file that contains the reference.</param>
/// <param name="Kind">The syntax the reference was written in.</param>
/// <param name="Line">1-based line of the reference in the source file.</param>
/// <param name="Excerpt">The text of that line in the source file: Markdown as written, without the whitespace around it, and at most 200 characters (a longer line is cut and ends with <c>…</c>). Left out when the source file has no such line or the line is blank.</param>
public sealed record FileBacklink(string Source, LinkKind Kind, int Line, string? Excerpt = null);
