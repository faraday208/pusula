namespace Pusula.Browse;

/// <summary>
/// The roots under which this machine keeps its drives, with how deep the search for vaults looks into each: where a vault
/// on a second drive or a memory stick is. Linux and macOS mount their drives in folders (<c>/mnt</c> and <c>/media</c>,
/// and <c>/Volumes</c> on macOS), and those folders are the roots. Windows has no such folders: there the roots are the
/// drives themselves, the root of every ready fixed or removable drive (<c>C:\</c>, <c>D:\</c>, a memory stick), and the
/// search leaves out the system folders at its top (<see cref="WindowsSystemFolders"/>). The extension point of the tests,
/// like <see cref="Pusula.Startup.UserDirectories"/>: a test gives its own (or none), so that none of them searches the
/// drives of whoever runs it.
/// </summary>
/// <param name="Roots">
/// The roots; one that does not exist is skipped. They are read each time a search starts: the drives of Windows are asked
/// then, so that a memory stick that is plugged in while the server runs is searched by the next search.
/// </param>
internal sealed record DriveFolders(IEnumerable<SearchRoot> Roots)
{
    /// <summary>How many levels below <c>/mnt</c> and <c>/media</c>, and below the root of a Windows drive, the search looks.</summary>
    internal const int MountDepth = 4;

    /// <summary>How many levels below <c>/Volumes</c> (macOS) the search looks.</summary>
    internal const int VolumesDepth = 3;

    /// <summary>
    /// The folders at the top of a Windows drive that the search does not enter: the ones of the system (<c>Windows</c>,
    /// <c>Program Files</c>, <c>Program Files (x86)</c>, <c>ProgramData</c>, <c>PerfLogs</c>, <c>Recovery</c>,
    /// <c>System Volume Information</c>, and every name that starts with a <c>$</c>, such as <c>$Recycle.Bin</c> and
    /// <c>$WinREAgent</c>), where no vault is kept, and <c>Users</c>, because the home directory is a root of its own and the
    /// profiles of the other users are not ours to read. The names are those of <see cref="SearchRoot.SkippedAtTop"/>.
    /// </summary>
    internal static IReadOnlyList<string> WindowsSystemFolders { get; } =
        ["Windows", "Program Files", "Program Files (x86)", "ProgramData", "Users", "PerfLogs", "Recovery", "System Volume Information", "$*"];

    /// <summary>No drives: the search only looks in the home directory.</summary>
    public static DriveFolders None { get; } = new([]);

    /// <summary>The drive folders of the operating system this runs on: the drives themselves on Windows, the folders of <see cref="For"/> elsewhere.</summary>
    public static DriveFolders ForThisMachine() =>
        OperatingSystem.IsWindows() ? ForWindowsDrives(ReadyDriveRoots()) : For(OperatingSystem.IsMacOS());

    /// <summary>The drive folders of a system that mounts its drives in folders: <c>/mnt</c> and <c>/media</c>, and <c>/Volumes</c> on macOS. (Where they do not exist, they are skipped.)</summary>
    /// <param name="isMacOs">Whether it is macOS.</param>
    public static DriveFolders For(bool isMacOs)
    {
        var roots = new List<SearchRoot> { new("/mnt", MountDepth), new("/media", MountDepth) };
        if (isMacOs)
        {
            roots.Add(new SearchRoot("/Volumes", VolumesDepth));
        }

        return new DriveFolders(roots);
    }

    /// <summary>
    /// The drive folders of Windows: the root of each drive, <see cref="MountDepth"/> levels deep like <c>/mnt</c>, without
    /// <see cref="WindowsSystemFolders"/> at its top. Which drives there are is asked each time the roots are read (see
    /// <see cref="Roots"/>), so a list that changes gives roots that change.
    /// </summary>
    /// <param name="driveRoots">The roots of the drives, such as <c>C:\</c>.</param>
    internal static DriveFolders ForWindowsDrives(IEnumerable<string> driveRoots) =>
        new(driveRoots.Select(root => new SearchRoot(root, MountDepth) { SkippedAtTop = WindowsSystemFolders }));

    // The drives that hold something to search: a disk or a memory stick that is ready. A network drive is left out (it can
    // take long to answer, or not answer at all), so is an optical one, and a drive that is not ready (a card reader with no
    // card) has nothing. Asked at each enumeration, so that a drive that came or went since the server started is counted.
    private static IEnumerable<string> ReadyDriveRoots()
    {
        foreach (DriveInfo drive in AllDrives())
        {
            if ((drive.DriveType is DriveType.Fixed or DriveType.Removable) && drive.IsReady)
            {
                yield return drive.Name;
            }
        }
    }

    private static DriveInfo[] AllDrives()
    {
        try
        {
            return DriveInfo.GetDrives();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // The drives cannot be listed: the home directory is searched all the same.
            return [];
        }
    }
}
