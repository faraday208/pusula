using System.Diagnostics.CodeAnalysis;
using Pusula.Indexing;
using Pusula.Sources;
using Pusula.Startup;

namespace Pusula.Browse;

/// <summary>The paths of the folder browser: which folder a request names, how a path is written for a person, where a page starts.</summary>
internal static class BrowsePaths
{
    /// <summary>
    /// The full path of the home directory, without a separator at the end; null when the user has none (it is not known,
    /// or not an absolute path).
    /// </summary>
    /// <param name="directories">The directories of the user.</param>
    public static string? HomeFolder(UserDirectories directories)
    {
        string home = directories.Home;
        return !string.IsNullOrWhiteSpace(home) && Path.IsPathFullyQualified(home) && TryNormalize(home, out string? fullPath) ? fullPath : null;
    }

    /// <summary>The folder a page starts in when it names none: the home directory, the root of the file system when there is none.</summary>
    /// <param name="directories">The directories of the user.</param>
    public static string StartFolder(UserDirectories directories) =>
        HomeFolder(directories) ?? Path.GetFullPath(Path.DirectorySeparatorChar.ToString());

    /// <summary>
    /// Finds the folder a request names, and checks that it is one: <c>~</c> (or <c>~/</c>) at the start is the home
    /// directory, the path has to be absolute and to have no character that no path has (<see cref="BrowseError.PathNotAbsolute"/>),
    /// <c>..</c> and a separator at the end are resolved, and it has to be a directory that exists
    /// (<see cref="BrowseError.FolderNotFound"/>). A path that is left out or blank names the start folder
    /// (<see cref="StartFolder"/>). Nothing a request says makes this throw.
    /// </summary>
    /// <param name="typed">The path as the request has it; it is used as it is, without trimming it (a folder may have a space at its end).</param>
    /// <param name="directories">The directories of the user: <c>~</c> expands to the home directory of these.</param>
    /// <param name="fullPath">Full path of the folder, without a separator at the end (the root of the file system keeps its own).</param>
    /// <param name="error">What is wrong with the path.</param>
    public static bool TryResolve(
        string? typed,
        UserDirectories directories,
        [NotNullWhen(true)] out string? fullPath,
        [NotNullWhen(false)] out BrowseError? error)
    {
        fullPath = null;

        string resolved;
        if (string.IsNullOrWhiteSpace(typed))
        {
            resolved = StartFolder(directories);
        }
        else if (!TryExpand(typed, directories.Home, out resolved, out error))
        {
            return false;
        }

        if (!Directory.Exists(resolved))
        {
            error = BrowseError.FolderNotFound;
            return false;
        }

        fullPath = resolved;
        error = null;
        return true;
    }

    /// <summary>
    /// The path as a person writes it: <c>~</c> for the home directory, <c>~/...</c> below it, the full path anywhere else
    /// (also where there is no home directory).
    /// </summary>
    /// <param name="fullPath">Full path of a folder, without a separator at the end.</param>
    /// <param name="home">Full path of the home directory (see <see cref="HomeFolder"/>); null when there is none.</param>
    public static string Display(string fullPath, string? home)
    {
        if (home is null)
        {
            return fullPath;
        }

        if (SourcePaths.Same(fullPath, home))
        {
            return "~";
        }

        // Below the home directory means the next character is a separator: /home/user2 is not below /home/user.
        string prefix = Path.TrimEndingDirectorySeparator(home);
        bool below = fullPath.Length > prefix.Length
            && fullPath.StartsWith(prefix, PathComparison.Current)
            && (fullPath[prefix.Length] == Path.DirectorySeparatorChar || fullPath[prefix.Length] == Path.AltDirectorySeparatorChar);
        return below ? "~" + fullPath[prefix.Length..] : fullPath;
    }

    /// <summary>The folder above a folder; null for the root of the file system.</summary>
    /// <param name="fullPath">Full path of a folder, without a separator at the end.</param>
    public static string? Parent(string fullPath) => Path.GetDirectoryName(fullPath);

    private static bool TryExpand(string typed, string homeDirectory, out string fullPath, [NotNullWhen(false)] out BrowseError? error)
    {
        fullPath = string.Empty;

        // A path with a null character (or, on Windows, another character no path has) cannot be a path.
        if (SourcePaths.HasInvalidCharacter(typed))
        {
            error = BrowseError.PathNotAbsolute;
            return false;
        }

        string expanded;
        try
        {
            expanded = PusulaOptions.ExpandHome(typed, homeDirectory);
        }
        catch (InvalidOperationException)
        {
            // "~" where the home directory is not known: not a path that leads anywhere.
            error = BrowseError.PathNotAbsolute;
            return false;
        }

        if (!Path.IsPathFullyQualified(expanded))
        {
            error = BrowseError.PathNotAbsolute;
            return false;
        }

        // A path that cannot be resolved (a character that no path has in the home directory, one that is too long): no such folder.
        if (!TryNormalize(expanded, out string? normalized))
        {
            error = BrowseError.FolderNotFound;
            return false;
        }

        fullPath = normalized;
        error = null;
        return true;
    }

    // The full path, without a separator at the end (the root of the file system keeps its own); false for a path the framework cannot resolve.
    private static bool TryNormalize(string path, [NotNullWhen(true)] out string? fullPath)
    {
        try
        {
            fullPath = Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
            return true;
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException or IOException)
        {
            fullPath = null;
            return false;
        }
    }
}
