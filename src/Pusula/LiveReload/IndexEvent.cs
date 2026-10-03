namespace Pusula.LiveReload;

/// <summary>
/// The data of a server-sent event on <c>/api/events</c>. The <c>ready</c> event carries the current version and
/// empty lists; every <c>changed</c> event carries the new version and what changed since the previous one.
/// </summary>
/// <param name="Version">The index version after the change.</param>
/// <param name="Added">Paths of files that appeared.</param>
/// <param name="Removed">Paths of files that disappeared.</param>
/// <param name="Changed">Paths of files whose content, layer or load mode changed.</param>
public sealed record IndexEvent(
    long Version,
    IReadOnlyList<string> Added,
    IReadOnlyList<string> Removed,
    IReadOnlyList<string> Changed);
