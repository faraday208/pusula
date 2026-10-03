namespace Pusula.Indexing;

/// <summary>A change between two consecutive index versions.</summary>
/// <param name="Version">The version the index has after the change.</param>
/// <param name="Added">Index keys that appeared.</param>
/// <param name="Removed">Index keys that disappeared.</param>
/// <param name="Changed">Index keys whose content, layer or load mode changed.</param>
internal sealed record IndexChange(
    long Version,
    IReadOnlyList<string> Added,
    IReadOnlyList<string> Removed,
    IReadOnlyList<string> Changed);
