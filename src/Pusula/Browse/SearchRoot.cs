using Pusula.Indexing;

namespace Pusula.Browse;

/// <summary>
/// A folder that the search for vaults starts from, and how many levels below it the search goes. A root can also name the
/// folders at its top that the search does not enter (<see cref="SkippedAtTop"/>): the top of a Windows drive holds the
/// system folders, and no vault is kept there. And it says whether the folders at its last level are asked for an entry note
/// (<see cref="ProbesLastLevel"/>).
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

    /// <summary>
    /// Whether the folders at the last level of the search (<see cref="MaxDepth"/> levels below the root), which the search does not read, are asked for
    /// an entry note, one by one, so that a folder of notes that has no <c>.obsidian</c> directory is found there as it is above (see
    /// <see cref="NoteFolderSearch"/>). A vault is found at the last level whatever this says. It is true for the home directory and, on Windows,
    /// for the system drive, and false by default and for the other drives (<c>/mnt</c>, <c>/media</c>, <c>/Volumes</c>, the other drives of Windows): asking means
    /// opening a folder that the search did not need to open, and an external disk may sleep or be slow, so that one folder takes seconds to open and holds
    /// the whole search back; and the folders of notes that were measured at the last level were all under the home directory. On those roots a folder of
    /// notes is found only among the folders that the search reads, down to <c>MaxDepth - 1</c> levels.
    /// </summary>
    public bool ProbesLastLevel { get; init; }

    /// <summary>Whether the search leaves a folder directly inside the root alone: its name is one of <see cref="SkippedAtTop"/>.</summary>
    /// <param name="name">The name of the folder, not a path.</param>
    public bool Skips(string name) =>
        SkippedAtTop.Any(skipped => skipped.EndsWith('*')
            ? name.AsSpan().StartsWith(skipped.AsSpan(..^1), PathComparison.Current)
            : string.Equals(skipped, name, PathComparison.Current));

    /// <summary>
    /// Whether the other root is the same: it has the same path, the same depth, skips the same names and asks its last level or not like this one.
    /// The names are compared by what they are, not by the list that holds them (a record would compare the lists by reference).
    /// </summary>
    /// <param name="other">The root to compare with.</param>
    public bool Equals(SearchRoot? other) =>
        other is not null
        && Path == other.Path
        && MaxDepth == other.MaxDepth
        && ProbesLastLevel == other.ProbesLastLevel
        && SkippedAtTop.SequenceEqual(other.SkippedAtTop);

    /// <inheritdoc />
    public override int GetHashCode() => HashCode.Combine(Path, MaxDepth, ProbesLastLevel, SkippedAtTop.Count);
}
