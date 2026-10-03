namespace Pusula.Browse;

/// <summary>
/// The folders under which this machine mounts its drives, with how deep the search for vaults looks into each: where a
/// vault on a second drive or a memory stick is. The extension point of the tests, like
/// <see cref="Pusula.Startup.UserDirectories"/>: a test gives its own (or none), so that none of them searches the drives
/// of whoever runs it.
/// </summary>
/// <param name="Roots">The folders; one that does not exist is skipped.</param>
internal sealed record DriveFolders(IReadOnlyList<SearchRoot> Roots)
{
    /// <summary>How many levels below <c>/mnt</c> and <c>/media</c> the search looks.</summary>
    internal const int MountDepth = 4;

    /// <summary>How many levels below <c>/Volumes</c> (macOS) the search looks.</summary>
    internal const int VolumesDepth = 3;

    /// <summary>No drives: the search only looks in the home directory.</summary>
    public static DriveFolders None { get; } = new([]);

    /// <summary>The drive folders of the operating system this runs on.</summary>
    public static DriveFolders ForThisMachine() => For(OperatingSystem.IsMacOS());

    /// <summary>The drive folders of an operating system: <c>/mnt</c> and <c>/media</c>, and <c>/Volumes</c> on macOS. (Where they do not exist, they are skipped.)</summary>
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
}
