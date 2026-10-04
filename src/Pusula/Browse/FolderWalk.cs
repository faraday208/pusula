using Pusula.Indexing;

namespace Pusula.Browse;

/// <summary>Reading the folders of the file system, for the scans of the browser: the way they list a folder, and how they tell one folder from another when symbolic links lead back.</summary>
internal static class FolderWalk
{
    /// <summary>Compares the paths of folders the way the platform does.</summary>
    internal static readonly StringComparer PathComparer = StringComparer.FromComparison(PathComparison.Current);

    // Every entry is listed, hidden ones too: the scans decide what is hidden, by name and, for the folders that the browser lists
    // and the search enters, on Windows by attribute too (see FolderNames). What cannot be read is left out.
    private static readonly EnumerationOptions ListingOptions = new()
    {
        AttributesToSkip = 0,
        IgnoreInaccessible = true,
        RecurseSubdirectories = false,
        ReturnSpecialDirectories = false,
    };

    // How the calls of this thread list a folder, when something other than the file system is to answer: see Use.
    [ThreadStatic]
    private static Func<string, IEnumerable<FileSystemInfo>>? s_listing;

    /// <summary>
    /// Lists the entries of a folder, files and folders: a symbolic link that leads to a folder is one of the folders. The
    /// listing is lazy and can throw <see cref="IOException"/> or <see cref="UnauthorizedAccessException"/> while it is read.
    /// </summary>
    /// <param name="folder">Full path of the folder.</param>
    public static IEnumerable<FileSystemInfo> Entries(string folder) => s_listing is { } listing ? listing(folder) : SystemEntries(folder);

    /// <summary>The listing of <see cref="Entries"/> as the file system gives it, whatever <see cref="Use"/> says.</summary>
    /// <param name="folder">Full path of the folder.</param>
    internal static IEnumerable<FileSystemInfo> SystemEntries(string folder) => new DirectoryInfo(folder).EnumerateFileSystemInfos("*", ListingOptions);

    /// <summary>
    /// Makes <see cref="Entries"/> list folders by <paramref name="listing"/> on this thread until the scope is disposed. The extension point of
    /// the tests, for a file system that does not answer (a disk that sleeps, a drive that is gone): the search for folders runs on a thread of its
    /// own, and nothing else makes a call of the file system stall. Other threads are not affected, so tests that run at the same time are not.
    /// </summary>
    /// <param name="listing">How to list a folder; the file system's way when null.</param>
    internal static IDisposable Use(Func<string, IEnumerable<FileSystemInfo>>? listing) => new ListingScope(listing);

    private sealed class ListingScope : IDisposable
    {
        private readonly Func<string, IEnumerable<FileSystemInfo>>? _before = s_listing;

        public ListingScope(Func<string, IEnumerable<FileSystemInfo>>? listing) => s_listing = listing;

        public void Dispose() => s_listing = _before;
    }

    /// <summary>Lists the folders inside a folder; like <see cref="Entries"/>, lazy.</summary>
    /// <param name="folder">Full path of the folder.</param>
    public static IEnumerable<DirectoryInfo> Folders(string folder) => new DirectoryInfo(folder).EnumerateDirectories("*", ListingOptions);

    /// <summary>
    /// The real path of a folder: where a symbolic link leads to, and the path of a folder that is not one below the real
    /// path of the folder it is in. A scan that has seen a real path does not go in again: that is what ends a link that
    /// leads back to a folder the scan is in. (The scan of an index does the same.)
    /// </summary>
    /// <param name="folder">The folder.</param>
    /// <param name="parentRealPath">The real path of the folder that <paramref name="folder"/> is in; null for the folder a scan starts with.</param>
    /// <returns>The real path; null when the folder cannot be resolved (a chain of links that never ends, one that cannot be read).</returns>
    public static string? RealPath(DirectoryInfo folder, string? parentRealPath)
    {
        try
        {
            if (folder.LinkTarget is not null)
            {
                return folder.ResolveLinkTarget(returnFinalTarget: true)?.FullName ?? folder.FullName;
            }

            return parentRealPath is null ? folder.FullName : Path.Join(parentRealPath, folder.Name);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }
}
