namespace Pusula.Startup;

/// <summary>
/// How pusula makes the folders and files of its own state folder: readable and writable by the user alone (on Unix; on Windows the
/// local application data directory is the user's own by its default permissions). Shared by <c>History/</c> and the instance key.
/// </summary>
internal static class PrivateStorage
{
    internal const UnixFileMode PrivateFolder = UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute;
    internal const UnixFileMode PrivateFile = UnixFileMode.UserRead | UnixFileMode.UserWrite;

    /// <summary>
    /// Creates a folder, and on Unix with a mode for the user alone. A mode is the mode of the last folder of a path only: the
    /// folders above it, when they do not exist, get the default mode, so a caller that wants one of them private makes it first.
    /// </summary>
    /// <param name="folder">The folder.</param>
    public static void CreateFolder(string folder)
    {
        if (OperatingSystem.IsWindows())
        {
            Directory.CreateDirectory(folder);
            return;
        }

        Directory.CreateDirectory(folder, PrivateFolder);
    }

    /// <summary>The options of a file that is made new (it is never opened if it exists) and, on Unix, is for the user alone.</summary>
    public static FileStreamOptions NewFileOptions()
    {
        var options = new FileStreamOptions { Mode = FileMode.CreateNew, Access = FileAccess.Write, Share = FileShare.None };
        if (!OperatingSystem.IsWindows())
        {
            options.UnixCreateMode = PrivateFile;
        }

        return options;
    }

    /// <summary>Deletes a file and says nothing when that fails: the error that is being handled is the one that matters.</summary>
    /// <param name="file">The file.</param>
    public static void TryDelete(string file)
    {
        try
        {
            File.Delete(file);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            // Best effort.
        }
    }
}
