using System.ComponentModel;
using System.Diagnostics;
using System.Buffers.Text;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text.Json;

namespace Pusula.Startup;

/// <summary>
/// Opening the page in the browser once pusula listens, so that the downloaded file, started with a double click, shows
/// something instead of a console window alone; and when the port is taken by a pusula that is already running, opening
/// that one instead of stopping with an error. Off with <c>--no-browser</c> (<c>Pusula:OpenBrowser false</c>).
/// </summary>
internal static class Browser
{
    /// <summary>How long the pusula that may hold the port has to answer.</summary>
    internal static readonly TimeSpan ProbeTimeout = TimeSpan.FromSeconds(2);

    /// <summary>How much of the answer is read: the proof of a pusula is under 100 bytes; whatever holds the port may send more.</summary>
    internal const int ProbeMaxBytes = 4 * 1024;

    /// <summary>
    /// Whether to open the browser. <paramref name="setting"/> (<c>Pusula:OpenBrowser</c>) decides when it is set; unset,
    /// the browser is opened outside Development only: the downloaded file is started by people, <c>dotnet run</c> by
    /// contributors who start it again and again; and not in an SSH session (<c>SSH_CONNECTION</c> or <c>SSH_TTY</c>), where
    /// the browser would open on the screen of the remote computer, not in front of the person. On Linux and the other Unix
    /// systems a desktop session is needed whatever the setting says (<c>DISPLAY</c> or <c>WAYLAND_DISPLAY</c>): on a server
    /// or in CI there is no browser to open.
    /// </summary>
    /// <param name="setting">The value of <c>Pusula:OpenBrowser</c>; null when it is not set.</param>
    /// <param name="isDevelopment">Whether the host runs in the Development environment.</param>
    /// <param name="isWindows">Whether this is Windows.</param>
    /// <param name="isMacOS">Whether this is macOS.</param>
    /// <param name="environment">Reads an environment variable; <see cref="Environment.GetEnvironmentVariable(string)"/> in the application.</param>
    public static bool ShouldOpen(bool? setting, bool isDevelopment, bool isWindows, bool isMacOS, Func<string, string?> environment)
    {
        ArgumentNullException.ThrowIfNull(environment);

        bool overSsh = !string.IsNullOrEmpty(environment("SSH_CONNECTION")) || !string.IsNullOrEmpty(environment("SSH_TTY"));
        if (!(setting ?? (!isDevelopment && !overSsh)))
        {
            return false;
        }

        return isWindows || isMacOS
            || !string.IsNullOrEmpty(environment("DISPLAY"))
            || !string.IsNullOrEmpty(environment("WAYLAND_DISPLAY"));
    }

    /// <summary>
    /// The address of the page to open among the addresses the server listens on (or is set to listen on): the first one
    /// of this computer, otherwise the first one. An address of every network interface (<c>0.0.0.0</c>, <c>[::]</c>,
    /// <c>*</c>, <c>+</c>) is opened as <c>localhost</c>. Only <c>http</c> and <c>https</c> addresses count.
    /// </summary>
    /// <param name="addresses">The addresses, such as <c>http://localhost:5190</c>.</param>
    /// <returns>The address of the page, ending in <c>/</c>; null when there is none to open.</returns>
    public static string? PageAddress(IEnumerable<string> addresses)
    {
        ArgumentNullException.ThrowIfNull(addresses);

        Uri[] read = [.. addresses.Select(Read).OfType<Uri>()];
        Uri? chosen = read.FirstOrDefault(static uri => uri.IsLoopback) ?? read.FirstOrDefault();
        return chosen is null ? null : new UriBuilder(chosen.Scheme, chosen.Host, chosen.Port, "/").Uri.ToString();
    }

    private static Uri? Read(string address)
    {
        // A host of every interface in the settings (http://*:5190, http://+:5190) is not a host a URI can have.
        string text = address.Trim().Replace("://*", "://localhost", StringComparison.Ordinal).Replace("://+", "://localhost", StringComparison.Ordinal);
        if (!Uri.TryCreate(text, UriKind.Absolute, out Uri? uri) || uri.Scheme is not ("http" or "https"))
        {
            return null;
        }

        bool everyInterface = IPAddress.TryParse(uri.Host.Trim('[', ']'), out IPAddress? ip)
            && (ip.Equals(IPAddress.Any) || ip.Equals(IPAddress.IPv6Any));
        return everyInterface ? new UriBuilder(uri) { Host = "localhost" }.Uri : uri;
    }

    /// <summary>
    /// What the entry point says when the port is taken: when a pusula of the same user answers at the address, it is opened in the browser
    /// and this is the line to print (and pusula ends with exit code 0, as the page the person wanted is open). Null when
    /// the address is not known or what holds it is not a pusula: then the port is in use by another program.
    /// </summary>
    /// <param name="urls">The <c>urls</c> setting of the host: addresses separated by <c>;</c>.</param>
    /// <param name="isPusula">Tells whether a pusula of the same user answers at an address; <see cref="IsPusula(string, string)"/> in the application.</param>
    /// <param name="open">Opens an address in the browser and tells whether that worked; <see cref="Open(string)"/> in the application.</param>
    public static string? AlreadyRunning(string? urls, Func<string, bool> isPusula, Func<string, bool> open)
    {
        ArgumentNullException.ThrowIfNull(isPusula);
        ArgumentNullException.ThrowIfNull(open);

        string? page = urls is null ? null : PageAddress(urls.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
        if (page is null || !isPusula(page))
        {
            return null;
        }

        return open(page)
            ? $"pusula: already running at {page}; opened it in the browser."
            : $"pusula: already running at {page}; open that address in your browser.";
    }

    /// <summary>
    /// Whether a pusula of the same user answers at an address: it has to prove it (see <see cref="InstanceKey"/>). The key is read
    /// first (never written: that is for the pusula that opens the browser); without a trusted key nothing is asked. Then a fresh
    /// challenge goes to <c>GET api/instance</c>, and the proof has to be the one for the endpoint that the connection really went to.
    /// What only looks like a pusula, whatever it answers, is not one.
    /// </summary>
    /// <param name="address">The address of the page, such as <c>http://localhost:5190/</c>.</param>
    /// <param name="state">The state directory (<see cref="UserDirectories.State"/>) the key is in.</param>
    public static bool IsPusula(string address, string state)
    {
        // No proxy: a proxy of the environment would answer for localhost, or send the request elsewhere. No redirect: the
        // answer that counts is the one of the address itself. The socket is connected here, so that the endpoint that the
        // proof is checked against is the one the connection went to, not one that a name or a header says.
        var connection = new RecordedConnection();
        using var handler = new SocketsHttpHandler { UseProxy = false, AllowAutoRedirect = false, ConnectCallback = connection.ConnectAsync };
        return IsPusulaAsync(address, state, handler, () => (connection.First, connection.Count), ProbeTimeout).GetAwaiter().GetResult();
    }

    /// <summary>The request of <see cref="IsPusula(string, string)"/>, through a given handler and with a given time to answer.</summary>
    /// <param name="address">The address of the page.</param>
    /// <param name="state">The state directory the key is in.</param>
    /// <param name="handler">Sends the request.</param>
    /// <param name="connectedTo">The first endpoint that the handler connected to (null when it did not: then nothing can be proved) and how many connections it made.</param>
    /// <param name="timeout">The time the whole probe has.</param>
    internal static async Task<bool> IsPusulaAsync(string address, string state, HttpMessageHandler handler, Func<(IPEndPoint? First, int Count)> connectedTo, TimeSpan timeout)
    {
        byte[]? key = InstanceKey.Read(state);
        if (key is null)
        {
            return false;
        }

        string challenge = InstanceKey.NewChallenge();
        using var client = new HttpClient(handler, disposeHandler: false) { Timeout = timeout };
        using var deadline = new CancellationTokenSource(timeout);
        try
        {
            using HttpResponseMessage response = await client.GetAsync(
                new Uri(new Uri(address), "api/instance?challenge=" + challenge), HttpCompletionOption.ResponseHeadersRead, deadline.Token).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                return false;
            }

            // At most ProbeMaxBytes, within the same time: an answer that is longer is not the proof of a pusula.
            await using Stream body = await response.Content.ReadAsStreamAsync(deadline.Token).ConfigureAwait(false);
            byte[] buffer = new byte[ProbeMaxBytes + 1];
            int read = 0;
            int count;
            while (read < buffer.Length && (count = await body.ReadAsync(buffer.AsMemory(read), deadline.Token).ConfigureAwait(false)) > 0)
            {
                read += count;
            }

            if (read > ProbeMaxBytes)
            {
                return false;
            }

            using JsonDocument json = JsonDocument.Parse(buffer.AsMemory(0, read));
            JsonElement root = json.RootElement;
            if (root.ValueKind != JsonValueKind.Object
                || !root.TryGetProperty("proof", out JsonElement proof) || proof.ValueKind != JsonValueKind.String
                || connectedTo() is not ({ } endpoint, var connections))
            {
                return false;
            }

            // The proof is worth something only for the one connection to the address that was asked: a second connection means that the
            // request was sent on (a redirect), and the endpoint has to be the port of the page (a proof for another port is not for it).
            if (connections != 1 || endpoint.Port != new Uri(address).Port)
            {
                return false;
            }

            Span<byte> given = stackalloc byte[HMACSHA256.HashSizeInBytes];
            string text = proof.GetString()!;
            if (!Base64Url.IsValid(text) || !Base64Url.TryDecodeFromChars(text, given, out int written) || written != given.Length)
            {
                return false;
            }

            byte[] expected = InstanceKey.Proof(key, challenge, InstanceKey.EndpointOf(endpoint.Address, endpoint.Port));
            return CryptographicOperations.FixedTimeEquals(given, expected);
        }
        catch (Exception exception) when (exception is HttpRequestException or OperationCanceledException or JsonException or IOException)
        {
            return false;
        }
    }

    /// <summary>Connects like the handler does itself, remembers the first endpoint that it connected to and counts the connections.</summary>
    internal sealed class RecordedConnection
    {
        private IPEndPoint? _first;
        private int _count;

        /// <summary>The endpoint of the first connection (an IPv4 address written as IPv6 is the IPv4 one); null when there was none.</summary>
        public IPEndPoint? First => Volatile.Read(ref _first);

        /// <summary>How many connections were made.</summary>
        public int Count => Volatile.Read(ref _count);

        public async ValueTask<Stream> ConnectAsync(SocketsHttpConnectionContext context, CancellationToken cancellationToken)
        {
            var socket = new Socket(SocketType.Stream, ProtocolType.Tcp) { NoDelay = true };
            try
            {
                await socket.ConnectAsync(context.DnsEndPoint, cancellationToken).ConfigureAwait(false);
                if (Interlocked.Increment(ref _count) == 1)
                {
                    Volatile.Write(ref _first, (IPEndPoint?)socket.RemoteEndPoint);
                }

                return new NetworkStream(socket, ownsSocket: true);
            }
            catch
            {
                socket.Dispose();
                throw;
            }
        }
    }

    /// <summary>Opens an address in the default browser of this computer; false when that could not be done.</summary>
    /// <param name="address">An <c>http</c> or <c>https</c> address (from <see cref="PageAddress(IEnumerable{string})"/>).</param>
    public static bool Open(string address)
    {
        try
        {
            using Process? process = Process.Start(StartInfo(address, OperatingSystem.IsWindows(), OperatingSystem.IsMacOS()));
            return true;
        }
        catch (Exception exception) when (exception is Win32Exception or InvalidOperationException or PlatformNotSupportedException)
        {
            return false;
        }
    }

    /// <summary>
    /// How the browser is started: on Windows the shell opens the address, on macOS <c>open</c> and elsewhere
    /// <c>xdg-open</c> with the address as its one argument (never a command line that a shell reads).
    /// </summary>
    internal static ProcessStartInfo StartInfo(string address, bool isWindows, bool isMacOS)
    {
        if (isWindows)
        {
            return new ProcessStartInfo(address) { UseShellExecute = true };
        }

        var start = new ProcessStartInfo(isMacOS ? "open" : "xdg-open") { UseShellExecute = false };
        start.ArgumentList.Add(address);
        return start;
    }
}
