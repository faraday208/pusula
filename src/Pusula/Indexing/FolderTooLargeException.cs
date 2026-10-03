using System.Globalization;

namespace Pusula.Indexing;

/// <summary>
/// A folder is larger than the <see cref="ScanLimits"/> allow, and its scan was given up. The message is a sentence
/// that says which limit it went beyond; it is shown as it is (as the reason a source is not available, and on the
/// error line of the command line).
/// </summary>
internal sealed class FolderTooLargeException : Exception
{
    private const long Mebibyte = 1024 * 1024;

    private FolderTooLargeException(string folder, string beyond)
        : base($"The folder is too large to show: more than {beyond}.")
    {
        Folder = folder;
    }

    /// <summary>Full path of the folder.</summary>
    public string Folder { get; }

    /// <summary>The folder has more Markdown files than <see cref="ScanLimits.MaxFiles"/>.</summary>
    /// <param name="folder">Full path of the folder.</param>
    /// <param name="limit">The limit.</param>
    public static FolderTooLargeException ForFiles(string folder, int limit) => new(folder, $"{Count(limit)} Markdown files");

    /// <summary>The folder has more folders below it than <see cref="ScanLimits.MaxDirectories"/>.</summary>
    /// <param name="folder">Full path of the folder.</param>
    /// <param name="limit">The limit.</param>
    public static FolderTooLargeException ForDirectories(string folder, int limit) => new(folder, $"{Count(limit)} folders");

    /// <summary>The folder has more Markdown than <see cref="ScanLimits.MaxBytes"/>.</summary>
    /// <param name="folder">Full path of the folder.</param>
    /// <param name="limit">The limit, in bytes.</param>
    public static FolderTooLargeException ForBytes(string folder, long limit) =>
        new(folder, limit % Mebibyte == 0 ? $"{Count(limit / Mebibyte)} MiB of Markdown" : $"{Count(limit)} bytes of Markdown");

    private static string Count(long number) => number.ToString("N0", CultureInfo.InvariantCulture);
}
