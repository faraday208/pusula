using System.Text;
using Pusula.Sources;

namespace Pusula.Startup;

/// <summary>
/// The address of the pusula that opened the browser, <c>instance-address</c> in the state folder next to <see cref="InstanceKey"/>: one
/// line, an absolute http(s) address ending in <c>/</c>. A second pusula started by a double click reads it to find the first one even
/// when that listens on another port than the default (the default was taken). The address is no proof of anything: whoever reads it
/// still asks the program there for the proof of the key before it opens the page. The file is written and read by the rules of
/// the key (not into a shown folder, not into a folder that is a link or that others can write to, a file for the user alone on Unix,
/// put in place in one step), except that the newest pusula replaces the one before it. Only a pusula started by a double click (no
/// folder on the command line) records and looks it up: one started with folders shows those and not the list of sources. Only a
/// loopback address is recorded and read, so the file never sends a second pusula to another host.
/// </summary>
internal static partial class InstanceAddress
{
    /// <summary>The name of the file in the <c>pusula</c> folder of the state directory.</summary>
    internal const string FileName = "instance-address";

    // An address is a line of a few dozen characters; anything much longer is not one and is not read.
    internal const int MaxFileBytes = 512;

    /// <summary>Whether this start records its address: it opens the browser and no folder was given on the command line.</summary>
    internal static bool ShouldRecord(bool openBrowser, bool foldersOnCommandLine) => openBrowser && !foldersOnCommandLine;

    /// <summary>Whether this start looks for a running pusula by the record before it checks the port: <see cref="ShouldRecord"/> and the default address.</summary>
    internal static bool ShouldLookUp(bool openBrowser, bool urlsAreDefault, bool foldersOnCommandLine) =>
        ShouldRecord(openBrowser, foldersOnCommandLine) && urlsAreDefault;

    /// <summary>The address, read now (never kept): null when there is none, it cannot be trusted or it is not an address. Never writes.</summary>
    /// <param name="state">The state directory (<see cref="UserDirectories.State"/>).</param>
    /// <param name="maxBytes">The largest file that is read; the cap in the application, a parameter so that the boundary can be tested.</param>
    public static string? Read(string state, int maxBytes = MaxFileBytes)
    {
        if (InstanceKey.FolderOf(state) is not { } folder)
        {
            return null;
        }

        string file = Path.Join(folder, FileName);
        try
        {
            if (!InstanceKey.IsTrusted(folder, file))
            {
                return null;
            }

            long length = new FileInfo(file).Length;
            return length is 0 || length > maxBytes ? null : Parse(File.ReadAllText(file));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            return null;
        }
    }

    /// <summary>
    /// Writes the address. Nothing is written into a folder that is shown, nor into a state folder that others can write to. A failure is
    /// logged and never thrown.
    /// </summary>
    /// <param name="state">The state directory.</param>
    /// <param name="address">The address of the page, as it is opened: absolute, http or https, ending in <c>/</c>.</param>
    /// <param name="shownRoots">The folders of all sources: nothing is written into any of them.</param>
    /// <param name="logger">Where what happened is said, once.</param>
    /// <returns>Whether the address is in place now.</returns>
    public static bool Write(string state, string address, IReadOnlyCollection<string> shownRoots, ILogger logger)
    {
        ArgumentNullException.ThrowIfNull(address);
        ArgumentNullException.ThrowIfNull(shownRoots);
        ArgumentNullException.ThrowIfNull(logger);

        if (InstanceKey.FolderOf(state) is not { } folder || Parse(address) is null)
        {
            return false;
        }

        string target = Path.Join(folder, FileName);
        string temporary = Path.Join(folder, "." + FileName + "." + Guid.NewGuid().ToString("N") + ".tmp");
        try
        {
            if (SourcePaths.IsInside(folder, shownRoots))
            {
                LogInsideSource(logger, folder);
                return false;
            }

            PrivateStorage.CreateFolder(folder);
            if (InstanceKey.IsLink(folder) || !InstanceKey.OnlyTheUserCanWrite(folder))
            {
                LogFolderOpen(logger, folder);
                return false;
            }

            using (var stream = new FileStream(temporary, PrivateStorage.NewFileOptions()))
            {
                stream.Write(Encoding.ASCII.GetBytes(address + "\n"));
            }

            File.Move(temporary, target, overwrite: true);
            return true;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            PrivateStorage.TryDelete(temporary);
            LogNotWritten(logger, target, exception.Message);
            return false;
        }
    }

    /// <summary>Deletes the file when it still holds this address (a newer pusula may have written its own since). Best effort.</summary>
    /// <param name="state">The state directory.</param>
    /// <param name="address">The address that this pusula wrote.</param>
    public static void DeleteIfOurs(string state, string address)
    {
        if (InstanceKey.FolderOf(state) is { } folder && Read(state) == address)
        {
            PrivateStorage.TryDelete(Path.Join(folder, FileName));
        }
    }

    // One line, at most one line end after it: an absolute http(s) loopback address with the path "/" and nothing else (no user, query or fragment).
    internal static string? Parse(string text)
    {
        string line = text.EndsWith("\r\n", StringComparison.Ordinal) ? text[..^2] : text.EndsWith('\n') ? text[..^1] : text;
        bool plain = line.Length > 0 && line.EndsWith('/') && !line.Any(static c => char.IsWhiteSpace(c) || char.IsControl(c));
        return plain
            && Uri.TryCreate(line, UriKind.Absolute, out Uri? uri)
            && uri.Scheme is "http" or "https"
            && uri.Host is "127.0.0.1" or "[::1]" or "localhost"
            && uri.AbsolutePath == "/" && uri.Query.Length == 0 && uri.Fragment.Length == 0 && uri.UserInfo.Length == 0
            ? line
            : null;
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "The instance address was not written: {Folder} is inside a folder that is shown, and pusula never writes there")]
    private static partial void LogInsideSource(ILogger logger, string folder);

    [LoggerMessage(Level = LogLevel.Information, Message = "The instance address was not written: {Folder} is a link or can be written to by others")]
    private static partial void LogFolderOpen(ILogger logger, string folder);

    [LoggerMessage(Level = LogLevel.Information, Message = "The instance address {File} could not be written: {Problem}; a second pusula started by a double click may not find this one")]
    private static partial void LogNotWritten(ILogger logger, string file, string problem);
}
