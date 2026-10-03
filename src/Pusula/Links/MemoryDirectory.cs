using System.Diagnostics.CodeAnalysis;

namespace Pusula.Links;

/// <summary>Recognizes Claude Code per-project memory directories: <c>projects/&lt;project&gt;/memory/</c>.</summary>
internal static class MemoryDirectory
{
    /// <summary>
    /// Returns the memory directory (<c>projects/&lt;project&gt;/memory</c>) that contains the file, if any.
    /// </summary>
    /// <param name="relativePath">Root-relative path with <c>/</c> separators.</param>
    /// <param name="comparison">How directory names are compared.</param>
    /// <param name="directory">The memory directory, spelled as in <paramref name="relativePath"/>.</param>
    public static bool TryGetFor(string relativePath, StringComparison comparison, [NotNullWhen(true)] out string? directory)
    {
        string[] parts = relativePath.Split('/');
        if (parts.Length >= 4
            && string.Equals(parts[0], "projects", comparison)
            && parts[1].Length > 0
            && string.Equals(parts[2], "memory", comparison))
        {
            directory = $"{parts[0]}/{parts[1]}/{parts[2]}";
            return true;
        }

        directory = null;
        return false;
    }
}
