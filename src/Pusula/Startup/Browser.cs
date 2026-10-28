using System.ComponentModel;
using System.Diagnostics;
using System.Net;
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

    /// <summary>How much of the answer is read: the list of sources of a pusula is a few KB; whatever holds the port may send more.</summary>
    internal const int ProbeMaxBytes = 64 * 1024;

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
    /// What the entry point says when the port is taken: when the address answers as a pusula, it is opened in the browser
    /// and this is the line to print (and pusula ends with exit code 0, as the page the person wanted is open). Null when
    /// the address is not known or what holds it is not a pusula: then the port is in use by another program.
    /// </summary>
    /// <param name="urls">The <c>urls</c> setting of the host: addresses separated by <c>;</c>.</param>
    /// <param name="isPusula">Tells whether a pusula answers at an address; <see cref="IsPusula(string)"/> in the application.</param>
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

    /// <summary>Whether a pusula answers at an address: <c>GET api/sources</c> there gives its list of sources and its version.</summary>
    /// <param name="address">The address of the page, such as <c>http://localhost:5190/</c>.</param>
    public static bool IsPusula(string address)
    {
        // No proxy: a proxy of the environment would answer for localhost, or send the request elsewhere. No redirect: the
        // answer that counts is the one of the address itself.
        using var handler = new SocketsHttpHandler { UseProxy = false, AllowAutoRedirect = false };
        return IsPusulaAsync(address, handler, ProbeTimeout).GetAwaiter().GetResult();
    }

    /// <summary>The request of <see cref="IsPusula(string)"/>, through a given handler and with a given time to answer.</summary>
    internal static async Task<bool> IsPusulaAsync(string address, HttpMessageHandler handler, TimeSpan timeout)
    {
        using var client = new HttpClient(handler, disposeHandler: false) { Timeout = timeout };
        using var deadline = new CancellationTokenSource(timeout);
        try
        {
            using HttpResponseMessage response = await client.GetAsync(
                new Uri(new Uri(address), "api/sources"), HttpCompletionOption.ResponseHeadersRead, deadline.Token).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                return false;
            }

            // At most ProbeMaxBytes, within the same time: an answer that is longer is not the list of a pusula.
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
            return root.ValueKind == JsonValueKind.Object
                && root.TryGetProperty("sources", out JsonElement sources) && sources.ValueKind == JsonValueKind.Array
                && root.TryGetProperty("version", out JsonElement version) && version.ValueKind == JsonValueKind.String;
        }
        catch (Exception exception) when (exception is HttpRequestException or OperationCanceledException or JsonException or IOException)
        {
            return false;
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
