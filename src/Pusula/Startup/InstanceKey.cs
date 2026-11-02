using System.Buffers.Text;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using Pusula.Sources;

namespace Pusula.Startup;

/// <summary>
/// The proof that what answers on a port is a pusula of the same user. A pusula that opens the browser keeps a random key in a file of
/// its own, <c>instance-key</c> in its state folder, which only the user can read. When a second pusula finds the port taken, it asks
/// the program that holds it for the proof of a fresh challenge (<c>GET /api/instance</c>): only a program that can read the key can give
/// it. Without it, anything that listens first on the port could answer like a pusula and have the page it serves opened.
/// </summary>
/// <remarks>
/// The key is one line, the base64 of 32 random bytes: exactly 44 characters, then at most one line end, and nothing else. A key counts
/// only when the content is exactly that and, on Unix, when the file is not a link and has no group or other permission at all, and
/// the folder above it is not a link and cannot be written by group or others: whoever could write there could replace the key. On
/// Windows the state folder is below <c>%LOCALAPPDATA%</c>, which only the user can reach by its default ACL, so there is no mode to look
/// at. The owner of the file and of the folder is not checked (it needs a native call), and nothing requires the state directory to be
/// below the home directory: <c>$XDG_STATE_HOME</c> can be any absolute path, so the modes are all there is. Only <see cref="EnsureExists"/> writes the key, and only a pusula that opens the browser calls it; everything
/// else reads (<see cref="Read"/>), so that nothing is made in the state folder of whoever runs a test or a second pusula.
/// </remarks>
internal static partial class InstanceKey
{
    /// <summary>The length of the key, and of a challenge, in bytes.</summary>
    internal const int Length = 32;

    /// <summary>The name of the key file in the <c>pusula</c> folder of the state directory.</summary>
    internal const string FileName = "instance-key";

    // A key file is one line of 44 characters; anything much longer is not one and is not read.
    private const int MaxFileBytes = 256;

    private static readonly System.Buffers.SearchValues<char> Base64Alphabet =
        System.Buffers.SearchValues.Create("ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789+/");

    private const string ProofPrefix = "pusula-instance-proof-v1\n";

    private const UnixFileMode OtherAccess =
        UnixFileMode.GroupRead | UnixFileMode.GroupWrite | UnixFileMode.GroupExecute
        | UnixFileMode.OtherRead | UnixFileMode.OtherWrite | UnixFileMode.OtherExecute;

    private const UnixFileMode OtherWrite = UnixFileMode.GroupWrite | UnixFileMode.OtherWrite;

    /// <summary>The <c>pusula</c> folder of a state directory; null when there is no state directory (the proof is off).</summary>
    /// <param name="state">The state directory (<see cref="UserDirectories.State"/>).</param>
    internal static string? FolderOf(string state) => state.Length == 0 ? null : Path.Join(state, "pusula");

    /// <summary>
    /// The key, read now (never kept): null when there is none or it cannot be trusted. Never writes.
    /// </summary>
    /// <param name="state">The state directory (<see cref="UserDirectories.State"/>).</param>
    public static byte[]? Read(string state)
    {
        if (FolderOf(state) is not { } folder)
        {
            return null;
        }

        string file = Path.Join(folder, FileName);
        try
        {
            if (!IsTrusted(folder, file))
            {
                return null;
            }

            var info = new FileInfo(file);
            if (info.Length is 0 or > MaxFileBytes)
            {
                return null;
            }

            return Decode(File.ReadAllText(file));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            return null;
        }
    }

    /// <summary>
    /// Makes sure that a trusted key is in place, writing one when there is none. Nothing is written into a folder that is shown, nor
    /// into a state folder that others can write to; a file that is no key (wrong content, too open a mode) is replaced. The key is written
    /// to a file of its own first and put in place in one step, so that two pusulas that start together end up with the same key. A failure
    /// is logged and never thrown.
    /// </summary>
    /// <param name="state">The state directory (<see cref="UserDirectories.State"/>).</param>
    /// <param name="shownRoots">The folders of all sources: nothing is written into any of them.</param>
    /// <param name="logger">Where what happened is said, once.</param>
    /// <param name="beforePublish">Called just before the key is put in place; the tests make the other pusula's key appear here.</param>
    /// <returns>Whether a trusted key is in place now.</returns>
    public static bool EnsureExists(string state, IReadOnlyCollection<string> shownRoots, ILogger logger, Action? beforePublish = null)
    {
        ArgumentNullException.ThrowIfNull(shownRoots);
        ArgumentNullException.ThrowIfNull(logger);

        if (FolderOf(state) is not { } folder)
        {
            return false;
        }

        string target = Path.Join(folder, FileName);
        string temporary = Path.Join(folder, "." + FileName + "." + Guid.NewGuid().ToString("N") + ".tmp");
        try
        {
            // Compared as strings, so a link below that leads into a shown folder is the job of the check on links further down.
            if (SourcePaths.IsInside(folder, shownRoots))
            {
                LogInsideSource(logger, folder);
                return false;
            }

            if (Read(state) is not null)
            {
                return true;
            }

            PrivateStorage.CreateFolder(folder);
            if (IsLink(folder) || !OnlyTheUserCanWrite(folder))
            {
                LogFolderOpen(logger, folder);
                return false;
            }

            // A file that is there and is no key is replaced; where there is none, the first to put one in place keeps it.
            bool replace = File.Exists(target);
            using (var stream = new FileStream(temporary, PrivateStorage.NewFileOptions()))
            {
                stream.Write(Encoding.ASCII.GetBytes(Convert.ToBase64String(RandomNumberGenerator.GetBytes(Length)) + "\n"));
            }

            beforePublish?.Invoke();
            try
            {
                File.Move(temporary, target, overwrite: replace);
            }
            catch (IOException) when (!replace && File.Exists(target))
            {
                // Another pusula was faster: what is there now is the key.
                PrivateStorage.TryDelete(temporary);
                return Read(state) is not null;
            }

            LogCreated(logger, target);
            return true;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            PrivateStorage.TryDelete(temporary);
            LogNotWritten(logger, target, exception.Message);
            return false;
        }
    }

    /// <summary>
    /// The proof of a challenge: the HMAC-SHA256, with the key, of a fixed prefix, the challenge as it was sent and the endpoint of the
    /// connection it was asked on. The endpoint ties the proof to the address of the pusula that gives it: a program that holds one port
    /// and passes the challenge on to a pusula on another cannot use the answer.
    /// </summary>
    /// <param name="key">The key.</param>
    /// <param name="challenge">The challenge as it was sent (base64url).</param>
    /// <param name="endpoint">The endpoint as <see cref="EndpointOf(IPAddress, int)"/> writes it.</param>
    public static byte[] Proof(byte[] key, string challenge, string endpoint) =>
        HMACSHA256.HashData(key, Encoding.UTF8.GetBytes(ProofPrefix + challenge + "\n" + endpoint));

    /// <summary>
    /// The endpoint of a connection written the same way on both sides: <c>127.0.0.1:5190</c>, <c>[::1]:5190</c>; an IPv4 address that
    /// is written as an IPv6 one (<c>::ffff:127.0.0.1</c>) is the IPv4 address.
    /// </summary>
    /// <param name="address">The address of the server end of the connection.</param>
    /// <param name="port">Its port.</param>
    public static string EndpointOf(IPAddress address, int port) =>
        new IPEndPoint(address.IsIPv4MappedToIPv6 ? address.MapToIPv4() : address, port).ToString();

    /// <summary>Whether a challenge is the base64url of exactly 32 bytes.</summary>
    /// <param name="challenge">The challenge as it was sent.</param>
    public static bool IsChallenge(string? challenge) =>
        challenge is { Length: 43 } && Base64Url.IsValid(challenge, out int decoded) && decoded == Length;

    /// <summary>A fresh challenge: 32 random bytes as base64url.</summary>
    public static string NewChallenge() => Base64Url.EncodeToString(RandomNumberGenerator.GetBytes(Length));

    // Exactly 44 characters of base64 (32 bytes), then at most one line end: no spaces, no line breaks inside, nothing after.
    private static byte[]? Decode(string text)
    {
        string line = text.EndsWith("\r\n", StringComparison.Ordinal) ? text[..^2] : text.EndsWith('\n') ? text[..^1] : text;
        if (line.Length != 44 || line[43] != '=' || line.AsSpan(0, 43).ContainsAnyExcept(Base64Alphabet))
        {
            return null;
        }

        Span<byte> buffer = stackalloc byte[Length + 4];
        return Convert.TryFromBase64String(line, buffer, out int written) && written == Length ? buffer[..written].ToArray() : null;
    }

    // The key file and the folder above it, by their modes. Windows has none to look at (see the remarks).
    internal static bool IsTrusted(string folder, string file)
    {
        if (OperatingSystem.IsWindows())
        {
            return File.Exists(file);
        }

        // A link is not the file itself: whoever made it chose where the key comes from.
        if (!File.Exists(file) || new FileInfo(file).LinkTarget is not null)
        {
            return false;
        }

        return !IsLink(folder) && OnlyTheUserCanWrite(folder) && (File.GetUnixFileMode(file) & OtherAccess) == 0;
    }

    // A folder that is a link leads wherever whoever made it chose (a shown folder, a folder of others). Windows has none of this to look at.
    internal static bool IsLink(string folder) =>
        !OperatingSystem.IsWindows() && new DirectoryInfo(folder).LinkTarget is not null;

    internal static bool OnlyTheUserCanWrite(string folder) =>
        OperatingSystem.IsWindows() || (File.GetUnixFileMode(folder) & OtherWrite) == 0;

    [LoggerMessage(Level = LogLevel.Information, Message = "Made the instance key {File}: a second pusula started by a double click can find this one")]
    private static partial void LogCreated(ILogger logger, string file);

    [LoggerMessage(Level = LogLevel.Information, Message = "The instance key was not written: {Folder} is inside a folder that is shown, and pusula never writes there")]
    private static partial void LogInsideSource(ILogger logger, string folder);

    [LoggerMessage(Level = LogLevel.Information, Message = "The instance key was not written: {Folder} is a link or can be written to by others")]
    private static partial void LogFolderOpen(ILogger logger, string folder);

    [LoggerMessage(Level = LogLevel.Information, Message = "The instance key {File} could not be written: {Problem}; a second pusula started by a double click will not find this one")]
    private static partial void LogNotWritten(ILogger logger, string file, string problem);
}
