using System.Diagnostics.CodeAnalysis;
using Pusula.Indexing;
using Pusula.Startup;

namespace Pusula.Sources;

/// <summary>
/// Checks the request to add a source and turns it into the values that the sources file gets. Only the request
/// and the file system are looked at here; whether the folder is listed already is for the registry to say.
/// </summary>
internal static class SourceRequestValidator
{
    /// <summary>The most characters a name has.</summary>
    internal const int MaxNameLength = 80;

    private const string AutoProfile = "auto";

    /// <summary>
    /// Checks the request in this order: the path (<see cref="EditError.PathRequired"/>, a character that no path has
    /// is <see cref="EditError.FolderNotFound"/>, <see cref="EditError.PathNotAbsolute"/>, <see cref="EditError.TooBroad"/>,
    /// <see cref="EditError.FolderNotFound"/>), the name, the profile. Nothing a request says makes this throw.
    /// </summary>
    /// <param name="request">The request body; null when there was none.</param>
    /// <param name="homeDirectory">The user's home directory, which a leading <c>~</c> expands to.</param>
    /// <param name="source">The values to write to the sources file.</param>
    /// <param name="error">What is wrong with the request.</param>
    public static bool TryValidate(
        CreateSourceRequest? request,
        string homeDirectory,
        [NotNullWhen(true)] out NewSource? source,
        [NotNullWhen(false)] out EditError? error)
    {
        source = null;

        string typed = request?.Path?.Trim() ?? string.Empty;
        if (typed.Length == 0)
        {
            error = EditError.PathRequired;
            return false;
        }

        // A path with a null character (or, on Windows, another character no path has) is a folder that cannot exist.
        if (SourcePaths.HasInvalidCharacter(typed))
        {
            error = EditError.FolderNotFound;
            return false;
        }

        if (!TryResolveFolder(typed, homeDirectory, out string fullPath, out error))
        {
            return false;
        }

        if (!TryReadName(request?.Name, fullPath, out string name))
        {
            error = EditError.InvalidName;
            return false;
        }

        if (!TryReadProfile(request?.Profile, out string profile))
        {
            error = EditError.InvalidProfile;
            return false;
        }

        source = new NewSource(Path.TrimEndingDirectorySeparator(typed), fullPath, name, profile);
        error = null;
        return true;
    }

    private static bool TryResolveFolder(string typed, string homeDirectory, out string fullPath, [NotNullWhen(false)] out EditError? error)
    {
        fullPath = string.Empty;

        string expanded;
        try
        {
            expanded = PusulaOptions.ExpandHome(typed, homeDirectory);
        }
        catch (InvalidOperationException)
        {
            // "~" where the home directory is not known: not a path that leads anywhere.
            error = EditError.PathNotAbsolute;
            return false;
        }

        if (!Path.IsPathFullyQualified(expanded))
        {
            error = EditError.PathNotAbsolute;
            return false;
        }

        try
        {
            fullPath = Path.TrimEndingDirectorySeparator(Path.GetFullPath(expanded));
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException or IOException)
        {
            // A character that no path has (a null character, for one), or a path that is too long: no such folder.
            error = EditError.FolderNotFound;
            return false;
        }

        if (IsTooBroad(fullPath, homeDirectory))
        {
            error = EditError.TooBroad;
            return false;
        }

        if (!Directory.Exists(fullPath))
        {
            error = EditError.FolderNotFound;
            return false;
        }

        error = null;
        return true;
    }

    // The root of the file system and the home directory itself (a folder inside it is fine).
    private static bool IsTooBroad(string fullPath, string homeDirectory)
    {
        string? root = Path.GetPathRoot(fullPath);
        if (root is not null && SourcePaths.Same(fullPath, root))
        {
            return true;
        }

        return homeDirectory.Length > 0 && SourcePaths.Same(fullPath, Path.GetFullPath(homeDirectory));
    }

    // A name that is left out or blank is the name of the folder.
    private static bool TryReadName(string? requested, string fullPath, out string name)
    {
        string trimmed = requested?.Trim() ?? string.Empty;
        if (trimmed.Length == 0)
        {
            name = SourceIds.NameOf(fullPath);
            return true;
        }

        name = trimmed;
        return trimmed.Length <= MaxNameLength && !trimmed.Any(char.IsControl);
    }

    // A profile that is left out or blank is "auto"; the others are written in lowercase, whatever case was sent.
    private static bool TryReadProfile(string? requested, out string profile)
    {
        profile = AutoProfile;
        string trimmed = requested?.Trim() ?? string.Empty;
        if (trimmed.Length == 0)
        {
            return true;
        }

        if (!SourceProfiles.TryParse(trimmed, out SourceProfile? parsed))
        {
            return false;
        }

        profile = parsed is { } known ? SourceProfiles.Name(known) : AutoProfile;
        return true;
    }
}
