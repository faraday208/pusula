using System.Globalization;
using Pusula.Indexing;

namespace Pusula.Browse;

/// <summary>Which folders the browser shows, enters and leaves out (by their names and, for hidden ones on Windows, by their attributes), and in what order the shown ones come.</summary>
internal static class FolderNames
{
    /// <summary>The folder of the home directory that a listing shows even when hidden folders are left out.</summary>
    internal const string ClaudeFolder = ".claude";

    private static readonly CompareInfo Turkish = FindTurkish(CultureInfo.GetCultureInfo);

    // Folders of a project that hold what is built or fetched and not notes; the hidden ones (.git among them) are never entered anyway.
    private static readonly string[] BuildFolders = [IndexBuilder.NodeModulesDirectory, "bin", "obj"];

    /// <summary>
    /// Whether the name is that of a hidden folder: it starts with a dot. This is the rule of the index and of the count of
    /// Markdown files, which leave out hidden files and folders by their names; the folders that the browser lists and the
    /// search enters are asked <see cref="IsHidden(DirectoryInfo)"/>.
    /// </summary>
    /// <param name="name">The name of a folder, not a path.</param>
    public static bool IsHidden(string name) => name.StartsWith('.');

    /// <summary>
    /// Whether the folder is hidden: its name starts with a dot or, on Windows, its attributes include Hidden or System
    /// (that is how <c>AppData</c> and <c>ProgramData</c> are hidden, and what Explorer goes by). Other systems keep to the
    /// name alone, as before: there .NET calls a folder Hidden because of its name, which <see cref="IsHidden(string)"/>
    /// sees as well, and on macOS also because of a flag of the system (<c>~/Library</c> has it), which does not hide a
    /// folder from the browser.
    /// </summary>
    /// <param name="folder">The folder. On Windows one that came from a listing has its attributes already, so asking costs nothing.</param>
    public static bool IsHidden(DirectoryInfo folder) =>
        IsHidden(folder.Name) || (OperatingSystem.IsWindows() && HasHiddenAttribute(folder.Attributes));

    /// <summary>
    /// Whether the search for folders leaves a folder alone, wherever it is: a hidden one (see <see cref="IsHidden(DirectoryInfo)"/>),
    /// or <c>node_modules</c>, <c>bin</c> or <c>obj</c>, which hold what is built or fetched and not notes. The look into a folder
    /// that may be a folder of notes (see <see cref="NoteFolderScanner"/>) leaves out the same ones.
    /// </summary>
    /// <param name="folder">The folder, one that came from a listing.</param>
    public static bool IsLeftOutOfSearch(DirectoryInfo folder)
    {
        if (IsHidden(folder))
        {
            return true;
        }

        // Asked of every folder the search comes across, so no closure and no allocation.
        string name = folder.Name;
        foreach (string skipped in BuildFolders)
        {
            if (string.Equals(skipped, name, PathComparison.Current))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Whether the attributes of a folder make it hidden on Windows: they include Hidden or System.</summary>
    /// <param name="attributes">The attributes of the folder.</param>
    internal static bool HasHiddenAttribute(FileAttributes attributes) =>
        (attributes & (FileAttributes.Hidden | FileAttributes.System)) != 0;

    /// <summary>Whether the name is <c>node_modules</c>, a folder that nothing here lists or enters.</summary>
    /// <param name="name">The name of a folder, not a path.</param>
    public static bool IsNodeModules(string name) => string.Equals(name, IndexBuilder.NodeModulesDirectory, PathComparison.Current);

    /// <summary>Whether the name is the <c>.claude</c> folder.</summary>
    /// <param name="name">The name of a folder, not a path.</param>
    public static bool IsClaudeFolder(string name) => string.Equals(name, ClaudeFolder, PathComparison.Current);

    /// <summary>The order of names: by the Turkish alphabet, ignoring case (names that only differ in case are the same).</summary>
    /// <param name="left">A name.</param>
    /// <param name="right">Another name.</param>
    public static int CompareIgnoringCase(string? left, string? right) =>
        Turkish.Compare(left, right, CompareOptions.IgnoreCase);

    /// <summary>
    /// <see cref="CompareIgnoringCase"/>, and names that only differ in case (a file system that tells <c>Notes</c> from
    /// <c>notes</c> has both) keep a fixed order: ordinal.
    /// </summary>
    /// <param name="left">A name.</param>
    /// <param name="right">Another name.</param>
    public static int Compare(string? left, string? right)
    {
        int order = CompareIgnoringCase(left, right);
        return order != 0 ? order : string.CompareOrdinal(left, right);
    }

    /// <summary>
    /// The way names are compared: that of the Turkish culture. A runtime without it (the invariant globalization mode has the
    /// invariant culture only) compares by the invariant one: ignoring case, but not by the Turkish alphabet.
    /// </summary>
    /// <param name="getCulture">Finds a culture by its name; throws <see cref="CultureNotFoundException"/> when the runtime does not have it.</param>
    internal static CompareInfo FindTurkish(Func<string, CultureInfo> getCulture)
    {
        try
        {
            return getCulture("tr-TR").CompareInfo;
        }
        catch (CultureNotFoundException)
        {
            return CultureInfo.InvariantCulture.CompareInfo;
        }
    }
}
