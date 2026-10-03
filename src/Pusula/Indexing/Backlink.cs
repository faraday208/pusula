using Pusula.Links;

namespace Pusula.Indexing;

/// <summary>An incoming reference: another file links to the file this backlink is stored under.</summary>
/// <param name="Source">Index key of the file that contains the link.</param>
/// <param name="Kind">Kind of the link.</param>
/// <param name="Line">1-based line of the link in <paramref name="Source"/>.</param>
internal readonly record struct Backlink(string Source, LinkKind Kind, int Line);
