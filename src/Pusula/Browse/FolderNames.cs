using System.Globalization;
using Pusula.Indexing;

namespace Pusula.Browse;

/// <summary>Which folder names the browser shows, enters and leaves out, and in what order the shown ones come.</summary>
internal static class FolderNames
{
    /// <summary>The folder of the home directory that a listing shows even when hidden folders are left out.</summary>
    internal const string ClaudeFolder = ".claude";

    private static readonly CompareInfo Turkish = FindTurkish(CultureInfo.GetCultureInfo);

    /// <summary>Whether the name is that of a hidden folder: it starts with a dot.</summary>
    /// <param name="name">The name of a folder, not a path.</param>
    public static bool IsHidden(string name) => name.StartsWith('.');

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
