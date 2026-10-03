namespace Pusula.Browse;

/// <summary>What a search for vaults found.</summary>
/// <param name="Folders">The folders, sorted by <see cref="BrowseFolder.Name"/> and, for the same name, by <see cref="BrowseFolder.Display"/>; none of them is <see cref="BrowseFolder.Listed"/> (that is told at the time of the answer).</param>
/// <param name="Complete">False when the search stopped at its budget before it had looked everywhere.</param>
internal sealed record FoundFolders(IReadOnlyList<BrowseFolder> Folders, bool Complete);
