namespace Pusula.Browse;

/// <summary>A folder that the search for vaults starts from, and how many levels below it the search goes.</summary>
/// <param name="Path">Full path of the folder.</param>
/// <param name="MaxDepth">The deepest level that is looked at: the folder itself is level 0, the folders inside it level 1, and so on.</param>
internal sealed record SearchRoot(string Path, int MaxDepth);
