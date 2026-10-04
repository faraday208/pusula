using Pusula.Indexing;

namespace Pusula.UnitTests.Support;

/// <summary>
/// A folder that stands for the network in a test: the link guard takes every path in it for a network location, and decides by the rules of Windows, whatever
/// the platform (see <see cref="LinkGuard.Use"/>). A network cannot be made to appear in a test; the links are real, and are made on every platform.
/// </summary>
internal static class FakeNetwork
{
    /// <summary>Tells the paths in the folder (and the folder) from the others.</summary>
    /// <param name="folder">Full path of the folder that stands for the network.</param>
    public static Func<string, bool> At(string folder)
    {
        string prefix = Path.TrimEndingDirectorySeparator(Path.GetFullPath(folder));
        return path => string.Equals(path, prefix, PathComparison.Current)
            || path.StartsWith(prefix + Path.DirectorySeparatorChar, PathComparison.Current)
            || path.StartsWith(prefix + Path.AltDirectorySeparatorChar, PathComparison.Current);
    }

    /// <summary>Makes this thread take the folder for the network until the scope is disposed.</summary>
    /// <param name="folder">Full path of the folder that stands for the network.</param>
    public static IDisposable In(string folder) => LinkGuard.Use(At(folder));
}
