using Pusula.Indexing;

namespace Pusula.Browse;

/// <summary>
/// A folder that the search for vaults starts from, and how many levels below it the search goes. A root can also name the
/// folders at its top that the search does not enter (<see cref="SkippedAtTop"/>): the top of a Windows drive holds the
/// system folders, and no vault is kept there.
/// </summary>
/// <param name="Path">Full path of the folder.</param>
/// <param name="MaxDepth">The deepest level that is looked at: the folder itself is level 0, the folders inside it level 1, and so on.</param>
internal sealed record SearchRoot(string Path, int MaxDepth)
{
    /// <summary>
    /// The names of the folders directly inside the root that the search does not enter, besides the ones that no search
    /// enters (hidden folders, <c>node_modules</c>, <c>bin</c> and <c>obj</c>). A name is compared the way the platform
    /// compares paths. A name that ends with <c>*</c> stands for every name that starts with what comes before the star:
    /// <c>$*</c> is <c>$Recycle.Bin</c> and every other name that starts with a dollar sign. Only the top of the root is
    /// meant: a folder with the same name further down is searched. None by default.
    /// </summary>
    public IReadOnlyList<string> SkippedAtTop { get; init; } = [];

    /// <summary>Whether the search leaves a folder directly inside the root alone: its name is one of <see cref="SkippedAtTop"/>.</summary>
    /// <param name="name">The name of the folder, not a path.</param>
    public bool Skips(string name) =>
        SkippedAtTop.Any(skipped => skipped.EndsWith('*')
            ? name.AsSpan().StartsWith(skipped.AsSpan(..^1), PathComparison.Current)
            : string.Equals(skipped, name, PathComparison.Current));

    /// <summary>
    /// Whether the other root is the same: it has the same path, the same depth and skips the same names. The names are
    /// compared by what they are, not by the list that holds them (a record would compare the lists by reference).
    /// </summary>
    /// <param name="other">The root to compare with.</param>
    public bool Equals(SearchRoot? other) =>
        other is not null && Path == other.Path && MaxDepth == other.MaxDepth && SkippedAtTop.SequenceEqual(other.SkippedAtTop);

    /// <inheritdoc />
    public override int GetHashCode() => HashCode.Combine(Path, MaxDepth, SkippedAtTop.Count);
}
