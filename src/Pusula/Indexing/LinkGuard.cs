namespace Pusula.Indexing;

/// <summary>
/// Decides whether a symbolic link (or a junction) may be followed, without opening what it leads to. On Windows, to open a file or a folder on a
/// network path (<c>\\server\share\x.md</c>) is to connect to that server and to try to log on to it with the credentials of the user, which
/// gives a hash of them to whoever runs the server. A link that someone else put on the disk (in a cloned repository, for one) can lead there, so that
/// merely looking at it would do. Everything that follows links asks here first, and a link that may not be followed is treated like an entry that
/// cannot be read: skipped, with no error and no access to the network.
/// <para>
/// What a link says is read from the link itself (<see cref="FileSystemInfo.LinkTarget"/> opens the link and not its target). A chain is taken one
/// link at a time, each time from the folder the link is in, and so is a path that has links in its folders, so that a link to <c>a/x.md</c> where
/// <c>a</c> is itself a link to a share is refused too: a folder is only opened when everything before it has been looked at. The answer is no for a link
/// that leads to a network location, one that leads somewhere that cannot be told without looking at the machine (a path with a drive but no folder
/// to start from, <c>C:x</c>, or one that starts at the root of the current drive, <c>\x</c>), and a chain of more than <see cref="MaxLinks"/> links.
/// </para>
/// <para>
/// Only Windows has the problem: on Linux and macOS a network share is mounted first, and so already logged on to, and everything is followed as before.
/// </para>
/// </summary>
internal static class LinkGuard
{
    /// <summary>The most links that one decision goes through (the ones in the folders of a path too) before it says no: a chain that long is not worth following.</summary>
    internal const int MaxLinks = 16;

    private static readonly char[] Separators = [Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar];

    // What tells a network location on this thread when something other than the system is to say: see Use.
    [ThreadStatic]
    private static Func<string, bool>? s_isNetwork;

    /// <summary>
    /// Whether a link may be followed: what it leads to (and every link on the way) is no network location. An entry that is no link may always be
    /// followed, for there is nothing to follow. The folder that the link is in is taken as safe to open, for reading the link goes through it: it was
    /// listed, and so reached by what asked here for every link on its way, or it is the folder that the user chose.
    /// </summary>
    /// <param name="link">The entry: a file or a folder, found by a listing or made from a path.</param>
    public static bool MayFollow(FileSystemInfo link) =>
        s_isNetwork is { } isNetwork ? MayFollow(link, isNetwork) : !OperatingSystem.IsWindows() || MayFollow(link, IsNetworkLocation);

    /// <summary>
    /// Whether a path below a folder may be opened: no folder on it, and nothing it ends in, is a link to a network location. For a path that is made
    /// from text that a file holds (a link of a note), and so may go through any link in the folder that the file is in. The folder itself is the one that
    /// was chosen, and is taken as it is.
    /// </summary>
    /// <param name="root">Full path of the folder.</param>
    /// <param name="relativePath">A path below it, written with <c>/</c> or with the separator of the platform, without <c>..</c>.</param>
    public static bool MayTouch(string root, string relativePath) =>
        s_isNetwork is { } isNetwork ? MayTouch(root, relativePath, isNetwork) : !OperatingSystem.IsWindows() || MayTouch(root, relativePath, IsNetworkLocation);

    /// <summary>
    /// Makes this thread take as a network location what <paramref name="isNetwork"/> says, and decide by the rules of Windows wherever it runs, until the
    /// scope is disposed. The extension point of the tests: a network cannot be made to appear in a test, and the rules would not run on Linux. Other
    /// threads are not affected.
    /// </summary>
    /// <param name="isNetwork">Whether a full path is a network location; the system's own way when null.</param>
    internal static IDisposable Use(Func<string, bool>? isNetwork) => new IsNetworkScope(isNetwork);

    /// <summary>The decision of <see cref="MayFollow(FileSystemInfo)"/>, with the way to tell a network location given.</summary>
    /// <param name="link">The entry.</param>
    /// <param name="isNetwork">Whether a full path is a network location.</param>
    internal static bool MayFollow(FileSystemInfo link, Func<string, bool> isNetwork)
    {
        try
        {
            // Most entries are no links, which their attributes say (a listing has read them already); a reparse point that is no link (a file that a cloud
            // service keeps for the user, say) has nothing to follow either.
            if ((link.Attributes & FileAttributes.ReparsePoint) == 0 || link.LinkTarget is null)
            {
                return true;
            }

            // The folder of the link is not the chosen one: it is looked at as well, from the root of its drive.
            string path = link.FullName;
            string root = Path.GetPathRoot(path) ?? string.Empty;
            return root.Length > 0 && !isNetwork(root) && Leads(root, path[root.Length..].Split(Separators, StringSplitOptions.RemoveEmptyEntries), isNetwork);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            // What cannot be looked at cannot be known to be safe.
            return false;
        }
    }

    /// <summary>The decision of <see cref="MayTouch(string, string)"/>, with the way to tell a network location given.</summary>
    /// <param name="root">Full path of the folder.</param>
    /// <param name="relativePath">A path below it.</param>
    /// <param name="isNetwork">Whether a full path is a network location.</param>
    internal static bool MayTouch(string root, string relativePath, Func<string, bool> isNetwork)
    {
        try
        {
            return Leads(root, relativePath.Split(Separators, StringSplitOptions.RemoveEmptyEntries), isNetwork);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            return false;
        }
    }

    /// <summary>
    /// Whether a path names a network location, by the way it is written and nothing else: on Windows, one that starts with two separators of either
    /// kind, as a share does (<c>\\server\share</c>, <c>//server/share</c>) and so do its long and device spellings (<c>\\?\UNC\server\share</c>,
    /// <c>\\.\UNC\server\share</c>, <c>\??\UNC\server\share</c>), and anything else in the long and device forms that is not a drive
    /// (<c>\\?\GLOBALROOT\Device\Mup\server\share</c>); a drive in them (<c>\\?\C:\x</c>) is not. Nowhere else is a path a network location by the way it is written.
    /// </summary>
    /// <param name="path">The path, as it was written.</param>
    /// <param name="windows">Whether the rules of Windows are the ones that apply.</param>
    internal static bool IsNetworkPath(string path, bool windows)
    {
        if (!windows)
        {
            return false;
        }

        ReadOnlySpan<char> text = path;
        if (text.StartsWith(@"\??\", StringComparison.Ordinal))
        {
            // The spelling that the system uses inside: \??\C:\x is a drive, \??\UNC\server\share a share.
            return !StartsWithDrive(text[4..]);
        }

        // The long and device spellings of a drive come after two separators too, and are no share.
        return text.Length >= 2 && IsSeparator(text[0]) && IsSeparator(text[1])
            && !(text.Length >= 4 && (text[2] is '?' or '.') && IsSeparator(text[3]) && StartsWithDrive(text[4..]));
    }

    // Walks the components from the folder, a folder at a time, the way the file system does, and says whether it gets through. A component that is a link is
    // replaced by what the link says, which is taken from the folder that the link is in (so that ".." is the parent of the folder as it really is, and not
    // of the path as it was written), and what it leads to is walked on from there; one that leads to a network location, or cannot be told, ends the walk.
    // Every folder that is opened has been looked at before: it is the one the walk is in, and one component more.
    private static bool Leads(string folder, string[] components, Func<string, bool> isNetwork)
    {
        var pending = new Stack<string>();
        Push(pending, components);
        string current = folder;
        int links = 0;
        while (pending.TryPop(out string? name))
        {
            if (name == ".")
            {
                continue;
            }

            if (name == "..")
            {
                current = Path.GetDirectoryName(current) ?? current;
                continue;
            }

            string next = Path.Join(current, name);
            if (TextOf(next) is not { } text)
            {
                current = next;
                continue;
            }

            if (++links > MaxLinks)
            {
                return false;
            }

            if (Path.IsPathRooted(text))
            {
                // The spelling that the system uses inside, \??\C:\x, is C:\x; what it says is asked about as it was written and as it is read.
                string written = text;
                if (text.StartsWith(@"\??\", StringComparison.Ordinal))
                {
                    text = text[4..];
                }

                // A path with a drive or a share says where it is. One that does not (\x, C:x) is somewhere that depends on where the process is.
                if (!Path.IsPathFullyQualified(text) || isNetwork(written) || isNetwork(text))
                {
                    return false;
                }

                current = Path.GetPathRoot(text) ?? string.Empty;
                text = text[current.Length..];
            }

            Push(pending, text.Split(Separators, StringSplitOptions.RemoveEmptyEntries));
        }

        return true;
    }

    // The first of the components is the first to come out.
    private static void Push(Stack<string> pending, string[] components)
    {
        for (int i = components.Length - 1; i >= 0; i--)
        {
            pending.Push(components[i]);
        }
    }

    // What the link at the path says; null when the path is no link, or is not there. Only the link is opened.
    private static string? TextOf(string path)
    {
        FileAttributes attributes = new FileInfo(path).Attributes;
        if (attributes == (FileAttributes)(-1) || (attributes & FileAttributes.ReparsePoint) == 0)
        {
            return null;
        }

        return (attributes & FileAttributes.Directory) != 0 ? new DirectoryInfo(path).LinkTarget : new FileInfo(path).LinkTarget;
    }

    private static bool IsNetworkLocation(string path) => IsNetworkPath(path, windows: true) || IsNetworkDrive(path);

    // A drive letter that stands for a network drive: C:\x, \\?\C:\x, \??\C:\x. The system says what kind a drive is without going to the network.
    private static bool IsNetworkDrive(string path)
    {
        ReadOnlySpan<char> text = path;
        if (text.StartsWith(@"\??\", StringComparison.Ordinal) || text.StartsWith(@"\\?\", StringComparison.Ordinal) || text.StartsWith(@"\\.\", StringComparison.Ordinal))
        {
            text = text[4..];
        }

        if (!StartsWithDrive(text))
        {
            return false;
        }

        try
        {
            return new DriveInfo(text[..1].ToString()).DriveType == DriveType.Network;
        }
        catch (Exception exception) when (exception is ArgumentException or IOException)
        {
            return false;
        }
    }

    private static bool IsSeparator(char character) => character is '\\' or '/';

    private static bool StartsWithDrive(ReadOnlySpan<char> text) => text.Length >= 2 && char.IsAsciiLetter(text[0]) && text[1] == ':';

    private sealed class IsNetworkScope : IDisposable
    {
        private readonly Func<string, bool>? _before = s_isNetwork;

        public IsNetworkScope(Func<string, bool>? isNetwork) => s_isNetwork = isNetwork;

        public void Dispose() => s_isNetwork = _before;
    }
}
