namespace Pusula.Links;

/// <summary>Everything <see cref="LinkResolver"/> needs to know about the tree. Built from primitives and BCL types only.</summary>
/// <param name="RootFullPath">Full path of the configuration root.</param>
/// <param name="PathComparison">How index keys and directory names are compared (must match the comparer of <paramref name="IndexedPaths"/>).</param>
/// <param name="IndexedPaths">Index keys: root-relative paths of all indexed Markdown files, <c>/</c> separated.</param>
/// <param name="MemoryNames">For every memory directory (<c>projects/&lt;project&gt;/memory</c>): frontmatter <c>name</c> to file path.</param>
/// <param name="TopLevelDirectories">Names of the directories directly under the root, including ones that are not indexed.</param>
/// <param name="PathExists">
/// Existence probe (<c>File.Exists || Directory.Exists</c>). Only ever called with a normalized, non-empty,
/// root-relative path that stays inside the root.
/// </param>
internal sealed record LinkResolutionContext(
    string RootFullPath,
    StringComparison PathComparison,
    HashSet<string> IndexedPaths,
    IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>> MemoryNames,
    IReadOnlySet<string> TopLevelDirectories,
    Func<string, bool> PathExists);
