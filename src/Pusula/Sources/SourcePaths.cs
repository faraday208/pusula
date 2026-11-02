using System.Buffers;
using Pusula.Indexing;

namespace Pusula.Sources;

/// <summary>Comparing the folders of sources, and telling the paths that cannot be one.</summary>
internal static class SourcePaths
{
    private static readonly SearchValues<char> InvalidCharacters = SearchValues.Create(Path.GetInvalidPathChars());

    /// <summary>
    /// Whether the path has a character that no path has: the null character, and on Windows the other characters of
    /// <see cref="Path.GetInvalidPathChars"/>. Such a path names no folder, and the framework refuses to resolve it.
    /// </summary>
    /// <param name="path">The path as it was written.</param>
    public static bool HasInvalidCharacter(string path) => path.AsSpan().ContainsAny(InvalidCharacters);

    /// <summary>
    /// Whether two full paths name the same folder: compared the way the platform compares paths, and without the
    /// separator at the end (a sources file may say <c>/a/b/</c> where a request says <c>/a/b</c>).
    /// </summary>
    /// <param name="left">A full path.</param>
    /// <param name="right">Another full path.</param>
    public static bool Same(string left, string right) =>
        string.Equals(Path.TrimEndingDirectorySeparator(left), Path.TrimEndingDirectorySeparator(right), PathComparison.Current);

    /// <summary>
    /// Whether a folder is a folder that is shown, or inside one: compared as full paths, the way the platform compares paths.
    /// pusula never writes into such a folder.
    /// </summary>
    /// <param name="folder">The folder pusula would write into.</param>
    /// <param name="shownRoots">The folders of all sources.</param>
    public static bool IsInside(string folder, IReadOnlyCollection<string> shownRoots)
    {
        string full = Path.TrimEndingDirectorySeparator(Path.GetFullPath(folder));
        foreach (string shownRoot in shownRoots)
        {
            string shown = Path.TrimEndingDirectorySeparator(Path.GetFullPath(shownRoot));
            string below = Path.EndsInDirectorySeparator(shown) ? shown : shown + Path.DirectorySeparatorChar;
            if (Same(full, shown) || full.StartsWith(below, PathComparison.Current))
            {
                return true;
            }
        }

        return false;
    }
}
