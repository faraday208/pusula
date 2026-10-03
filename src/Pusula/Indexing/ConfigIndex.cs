namespace Pusula.Indexing;

/// <summary>An immutable snapshot of a configuration folder.</summary>
/// <param name="Root">Full path of the folder.</param>
/// <param name="BuiltAt">When the snapshot was built (UTC).</param>
/// <param name="OutputStyle">The output style selected in <c>settings.json</c>, if any.</param>
/// <param name="Files">Indexed files by index key, enumerated in key order.</param>
/// <param name="Backlinks">
/// Incoming references by target key: one entry per resolved link from another file, ordered by source then line.
/// Files nothing links to have no entry.
/// </param>
/// <param name="Version">Assigned by the index host (0 as built); use <c>with { Version = n }</c>.</param>
/// <param name="Profile">How the folder was scanned and its links resolved.</param>
internal sealed record ConfigIndex(
    string Root,
    DateTimeOffset BuiltAt,
    string? OutputStyle,
    IReadOnlyDictionary<string, ConfigFile> Files,
    IReadOnlyDictionary<string, IReadOnlyList<Backlink>> Backlinks,
    long Version = 0,
    SourceProfile Profile = SourceProfile.Claude);
