namespace Pusula.Indexing;

/// <summary>The difference between two index snapshots.</summary>
/// <param name="Added">Keys present only in the new index, sorted.</param>
/// <param name="Removed">Keys present only in the old index, sorted.</param>
/// <param name="Changed">Keys in both whose content, layer or load mode differ, sorted.</param>
internal sealed record IndexDiff(
    IReadOnlyList<string> Added,
    IReadOnlyList<string> Removed,
    IReadOnlyList<string> Changed)
{
    /// <summary>True when the two snapshots are equivalent for subscribers.</summary>
    public bool IsEmpty => Added.Count == 0 && Removed.Count == 0 && Changed.Count == 0;

    /// <summary>Compares two snapshots.</summary>
    /// <param name="previous">The old index.</param>
    /// <param name="current">The new index.</param>
    public static IndexDiff Compute(ConfigIndex previous, ConfigIndex current)
    {
        string[] added = [.. current.Files.Keys.Where(path => !previous.Files.ContainsKey(path)).Order(StringComparer.Ordinal)];
        string[] removed = [.. previous.Files.Keys.Where(path => !current.Files.ContainsKey(path)).Order(StringComparer.Ordinal)];
        string[] changed =
        [
            .. current.Files
                .Where(entry => previous.Files.TryGetValue(entry.Key, out ConfigFile? old) && HasChanged(old, entry.Value))
                .Select(entry => entry.Key)
                .Order(StringComparer.Ordinal),
        ];

        return new IndexDiff(added, removed, changed);
    }

    private static bool HasChanged(ConfigFile old, ConfigFile current) =>
        !string.Equals(old.Content, current.Content, StringComparison.Ordinal)
        || old.Layer != current.Layer
        || old.LoadMode != current.LoadMode;
}
