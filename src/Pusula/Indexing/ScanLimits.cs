namespace Pusula.Indexing;

/// <summary>
/// How much of a folder is read before the scan gives up, so that a folder far larger than anyone reads (a home
/// directory, a whole drive) cannot take all the memory and time of the server. A folder beyond any of the three
/// limits is too large to show (<see cref="FolderTooLargeException"/>).
/// </summary>
/// <param name="MaxFiles">The most Markdown files (<c>.md</c>, however large) the scan comes across.</param>
/// <param name="MaxDirectories">The most folders below the root the scan enters. The ones it does not enter (hidden ones, runtime folders, <c>node_modules</c>) do not count.</param>
/// <param name="MaxBytes">The most bytes of Markdown the scan reads in all. A file larger than <see cref="IndexBuilder.MaxFileBytes"/> is not read and does not count.</param>
internal sealed record ScanLimits(int MaxFiles, int MaxDirectories, long MaxBytes)
{
    /// <summary>The limits of the application: 20,000 Markdown files, 50,000 folders and 256 MiB of Markdown.</summary>
    public static ScanLimits Default { get; } = new(20_000, 50_000, 256L * 1024 * 1024);
}
