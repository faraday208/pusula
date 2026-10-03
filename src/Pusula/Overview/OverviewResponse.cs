using Pusula.Indexing;
using Pusula.Links;

namespace Pusula.Overview;

/// <summary>A summary of a whole source: where the tokens go, and what is broken or unused.</summary>
/// <param name="Root">Full path of the source's folder.</param>
/// <param name="Version">Version of the index the overview was built from.</param>
/// <param name="BuiltAt">When the index was built.</param>
/// <param name="FileCount">Number of indexed files.</param>
/// <param name="EverySessionTokens">Estimated tokens that are loaded at the start of every session, over all files.</param>
/// <param name="Layers">One entry per layer that has files, in layer order.</param>
/// <param name="Heaviest">The ten files that add the most tokens to every session (files that add none are left out).</param>
/// <param name="ProjectMemory">The memory index of every project, largest first.</param>
/// <param name="Broken">Links to something that does not exist.</param>
/// <param name="Pending">Wiki links that match nothing yet: memories (Claude Code folder) or notes (vault, Markdown folder) that are not written yet. Not an error.</param>
/// <param name="Orphans">Files that should be linked to and are not. A note of a vault or a plain Markdown folder is listed only when it has no links at all: no other note links to it and it links to no other note.</param>
/// <param name="FrontmatterErrors">Files whose frontmatter is malformed.</param>
/// <param name="Profile">How the folder was scanned: <c>Claude</c>, <c>Vault</c> or <c>Markdown</c>.</param>
/// <param name="Tags">The tags of all notes with the number of notes that carry each, most used first (ties by name); a tag spelled in several ways appears once, under its first spelling in path order. Empty for a Claude Code folder.</param>
/// <param name="Recent">The notes that were written last, newest first (ties by path); at most 8. Empty for a Claude Code folder.</param>
/// <param name="MostLinked">The notes that the most links point at (the backlinks the file endpoint lists), most first (ties by path); at most 8, and a note that nothing links to is not listed. Empty for a Claude Code folder.</param>
/// <param name="OutputStyle">The output style selected in <c>settings.json</c>; left out when none is.</param>
/// <param name="Entry">The note to start reading from, for a vault or a plain Markdown folder: the first note without a folder whose name (without <c>.md</c>, ignoring case) is <c>home</c>, <c>index</c>, <c>readme</c>, <c>moc</c> or <c>start</c>, in that order; else, of the notes tagged <c>moc</c> or <c>home</c>, the one with the most links that lead to other notes (ties by path). Left out when there is none, and for a Claude Code folder.</param>
public sealed record OverviewResponse(
    string Root,
    long Version,
    DateTimeOffset BuiltAt,
    int FileCount,
    int EverySessionTokens,
    IReadOnlyList<OverviewLayer> Layers,
    IReadOnlyList<OverviewHeaviestFile> Heaviest,
    IReadOnlyList<OverviewProjectMemory> ProjectMemory,
    IReadOnlyList<OverviewBrokenLink> Broken,
    IReadOnlyList<OverviewPendingLink> Pending,
    IReadOnlyList<OverviewOrphan> Orphans,
    IReadOnlyList<OverviewFrontmatterError> FrontmatterErrors,
    SourceProfile Profile,
    IReadOnlyList<OverviewTag> Tags,
    IReadOnlyList<OverviewRecentNote> Recent,
    IReadOnlyList<OverviewMostLinkedNote> MostLinked,
    string? OutputStyle = null,
    OverviewEntryNote? Entry = null);

/// <summary>The files of one layer.</summary>
/// <param name="Layer">The layer.</param>
/// <param name="Files">Number of files in the layer.</param>
/// <param name="Tokens">Estimated tokens of all files in the layer.</param>
/// <param name="EverySessionTokens">Estimated tokens the layer adds to every session.</param>
public sealed record OverviewLayer(Layer Layer, int Files, int Tokens, int EverySessionTokens);

/// <summary>A file that adds many tokens to every session.</summary>
/// <param name="Path">Index key of the file.</param>
/// <param name="Layer">The role of the file.</param>
/// <param name="LoadMode">When Claude Code loads the file.</param>
/// <param name="EverySessionTokens">Estimated tokens the file adds to every session.</param>
public sealed record OverviewHeaviestFile(string Path, Layer Layer, LoadMode LoadMode, int EverySessionTokens);

/// <summary>The memory index (<c>MEMORY.md</c>) of one project.</summary>
/// <param name="Path">Index key of the file.</param>
/// <param name="Project">Name of the project directory under <c>projects/</c>.</param>
/// <param name="Tokens">Estimated tokens of the file, which every session of the project loads.</param>
public sealed record OverviewProjectMemory(string Path, string Project, int Tokens);

/// <summary>A link to something that does not exist.</summary>
/// <param name="Source">Index key of the file that contains the link.</param>
/// <param name="Line">1-based line of the link in the source file.</param>
/// <param name="Kind">The syntax the link was written in.</param>
/// <param name="Raw">The link text as written.</param>
/// <param name="Target">Where the link would point, relative to the root; left out when unknown.</param>
public sealed record OverviewBrokenLink(string Source, int Line, LinkKind Kind, string Raw, string? Target = null);

/// <summary>A wiki link in a memory file that matches no memory: a memory that is not written yet.</summary>
/// <param name="Source">Index key of the file that contains the link.</param>
/// <param name="Line">1-based line of the link in the source file.</param>
/// <param name="Kind">The syntax the link was written in.</param>
/// <param name="Raw">The link text as written.</param>
public sealed record OverviewPendingLink(string Source, int Line, LinkKind Kind, string Raw);

/// <summary>A file that no other file links to although it should be linked to.</summary>
/// <param name="Path">Index key of the file.</param>
/// <param name="Layer">The role of the file.</param>
public sealed record OverviewOrphan(string Path, Layer Layer);

/// <summary>A tag and the number of notes that carry it.</summary>
/// <param name="Name">The tag, without the leading <c>#</c>.</param>
/// <param name="Count">The number of notes that carry the tag.</param>
public sealed record OverviewTag(string Name, int Count);

/// <summary>The note to start reading a vault from.</summary>
/// <param name="Path">Index key of the note.</param>
/// <param name="Title">The name of the file without <c>.md</c>.</param>
public sealed record OverviewEntryNote(string Path, string Title);

/// <summary>A note that was written recently.</summary>
/// <param name="Path">Index key of the note.</param>
/// <param name="ModifiedAt">When the file was last written (UTC).</param>
public sealed record OverviewRecentNote(string Path, DateTimeOffset ModifiedAt);

/// <summary>A note that many links point at.</summary>
/// <param name="Path">Index key of the note.</param>
/// <param name="Count">The number of links from other notes that point at the note: the length of <c>backlinks</c> of the file endpoint.</param>
public sealed record OverviewMostLinkedNote(string Path, int Count);

/// <summary>A file with malformed frontmatter.</summary>
/// <param name="Path">Index key of the file.</param>
/// <param name="Error">What is wrong with the frontmatter.</param>
/// <param name="Line">1-based line in the file that <paramref name="Error"/> names, counted from the top of the file (the opening <c>---</c> is line 1); left out when the error has no known line.</param>
public sealed record OverviewFrontmatterError(string Path, string Error, int? Line = null);
