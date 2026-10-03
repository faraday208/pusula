namespace Pusula.Browse;

/// <summary>The folders inside one folder, as far as they are listed.</summary>
/// <param name="Folders">The folders, sorted by name; at most the limit.</param>
/// <param name="Truncated">True when there are more folders than the limit and the rest is left out.</param>
internal sealed record FolderListing(IReadOnlyList<BrowseFolder> Folders, bool Truncated);
