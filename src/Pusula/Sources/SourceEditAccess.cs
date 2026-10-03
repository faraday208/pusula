using System.Net;
using Microsoft.Extensions.Primitives;

namespace Pusula.Sources;

/// <summary>
/// Who may add and remove sources: only a request that comes from the machine the server runs on, was made by a page
/// of this server, and only while the sources come from a sources file. Other machines (the tailnet included) can
/// read; they cannot change what is shown. A request that a reverse proxy forwarded does not count as coming from this
/// machine, even when the proxy runs on it. <see cref="Pusula.Startup.PusulaOptions.AllowRemoteEdit"/> lifts the first
/// condition and only that one: the sources file and the page of this server are asked for in any case. The Host check
/// of <see cref="Pusula.Security.HostGuard"/> still comes first. The folder browser that adding a source is done with
/// (<c>/api/browse</c>) asks for the same, except that it lets a request through that the user made by hand
/// (<c>Sec-Fetch-Site: none</c>): it only reads.
/// </summary>
internal static class SourceEditAccess
{
    private const string SameOriginSite = "same-origin";

    // A request that the user made by hand: an address typed into the browser, a bookmark, a link of another application.
    private const string UserInitiatedSite = "none";

    // What a reverse proxy (nginx, cloudflared, ...) adds to the request it passes on: the connection then shows the
    // address of the proxy, which is on this machine, and not the one of the client.
    private static readonly string[] ForwardingHeaders = ["Forwarded", "X-Forwarded-For", "X-Forwarded-Host", "X-Real-IP"];

    /// <summary>
    /// Whether the connection is from the machine the server runs on: the remote address is a loopback address, or the
    /// very address the connection arrived at (a browser on this machine that uses the machine's own IP address). An
    /// IPv4 address that is written as an IPv6 one (<c>::ffff:127.0.0.1</c>) counts as the IPv4 address. No remote
    /// address (the connection has none, as in the test server) is not this machine.
    /// </summary>
    /// <param name="remote">The address of the client.</param>
    /// <param name="local">The address of the server the client connected to.</param>
    public static bool IsSameMachine(IPAddress? remote, IPAddress? local)
    {
        if (remote is null)
        {
            return false;
        }

        IPAddress client = Unmap(remote);
        return IPAddress.IsLoopback(client) || (local is not null && client.Equals(Unmap(local)));
    }

    /// <summary>
    /// Whether the request is from the machine the server runs on: its connection is (see
    /// <see cref="IsSameMachine(IPAddress?, IPAddress?)"/>) and it was not forwarded by a reverse proxy (see
    /// <see cref="IsForwarded"/>).
    /// </summary>
    /// <param name="http">The request.</param>
    public static bool IsSameMachine(HttpContext http) =>
        !IsForwarded(http.Request) && IsSameMachine(http.Connection.RemoteIpAddress, http.Connection.LocalIpAddress);

    /// <summary>
    /// Whether a reverse proxy forwarded the request: it has a <c>Forwarded</c>, <c>X-Forwarded-For</c>,
    /// <c>X-Forwarded-Host</c> or <c>X-Real-IP</c> header, whatever the header says (even an empty one). A proxy on this
    /// machine would make every client look as if it were on this machine; the headers say that it was not.
    /// </summary>
    /// <param name="request">The request.</param>
    public static bool IsForwarded(HttpRequest request) =>
        Array.Exists(ForwardingHeaders, header => request.Headers.ContainsKey(header));

    /// <summary>
    /// Whether the request was made by a page of this server, which is what stops another web page, in the same
    /// browser, from changing the sources. An <c>Origin</c> header has to be this server's own (scheme, host and port
    /// of the request; <c>null</c> and anything else is not), and a <c>Sec-Fetch-Site</c> header has to say
    /// <c>same-origin</c> (or <c>none</c>, for a request that only reads: see <paramref name="allowUserInitiated"/>). A request
    /// without either (<c>curl</c>, a script) is not from a web page and passes.
    /// </summary>
    /// <param name="request">The request.</param>
    /// <param name="allowUserInitiated">True for a request that only reads: <c>Sec-Fetch-Site: none</c> (the user typed the address, followed a bookmark) passes too. False (the default) for one that changes something: that has to come from a page of this server.</param>
    public static bool IsSameOrigin(HttpRequest request, bool allowUserInitiated = false)
    {
        StringValues site = request.Headers["Sec-Fetch-Site"];
        if (site.Count > 0 && !IsAllowedSite(site.ToString(), allowUserInitiated))
        {
            return false;
        }

        StringValues origin = request.Headers.Origin;
        if (origin.Count == 0)
        {
            return true;
        }

        return Uri.TryCreate(origin.ToString(), UriKind.Absolute, out Uri? uri)
            && string.Equals(uri.Scheme, request.Scheme, StringComparison.OrdinalIgnoreCase)
            && string.Equals(uri.Host, request.Host.Host, StringComparison.OrdinalIgnoreCase)
            && uri.Port == (request.Host.Port ?? DefaultPort(request.Scheme));
    }

    /// <summary>
    /// Why this request cannot change the sources, or null when it can: not from this machine (see
    /// <see cref="IsSameMachine(HttpContext)"/>) first, unless <paramref name="allowRemote"/> says that every machine may,
    /// then a list that is not a sources file.
    /// </summary>
    /// <param name="http">The request.</param>
    /// <param name="sourcesFile">The sources file the list comes from; null when it comes from the command line or <c>Pusula:Root</c>.</param>
    /// <param name="allowRemote">The setting <c>Pusula:AllowRemoteEdit</c>: true when a request from another machine may change the sources too; false (the default) when only one from this machine may.</param>
    public static EditBlock? Blocked(HttpContext http, string? sourcesFile, bool allowRemote = false)
    {
        if (!allowRemote && !IsSameMachine(http))
        {
            return EditBlock.Remote;
        }

        return sourcesFile is null ? EditBlock.CommandLine : null;
    }

    /// <summary>Why this request is refused, or null when it may add and remove sources: <see cref="Blocked"/>, then the origin of the request (which <paramref name="allowRemote"/> does not touch).</summary>
    /// <param name="http">The request.</param>
    /// <param name="sourcesFile">The sources file the list comes from; null when it comes from the command line or <c>Pusula:Root</c>.</param>
    /// <param name="allowRemote">The setting <c>Pusula:AllowRemoteEdit</c> (see <see cref="Blocked"/>).</param>
    /// <param name="allowUserInitiated">True for a request that only reads (see <see cref="IsSameOrigin(HttpRequest, bool)"/>); false (the default) for one that changes the sources.</param>
    public static EditError? Refusal(HttpContext http, string? sourcesFile, bool allowRemote = false, bool allowUserInitiated = false)
    {
        EditBlock? blocked = Blocked(http, sourcesFile, allowRemote);
        if (blocked is { } block)
        {
            return block == EditBlock.Remote ? EditError.Remote : EditError.CommandLine;
        }

        return IsSameOrigin(http.Request, allowUserInitiated) ? null : EditError.CrossOrigin;
    }

    private static bool IsAllowedSite(string site, bool allowUserInitiated) =>
        string.Equals(site, SameOriginSite, StringComparison.OrdinalIgnoreCase)
        || (allowUserInitiated && string.Equals(site, UserInitiatedSite, StringComparison.OrdinalIgnoreCase));

    private static IPAddress Unmap(IPAddress address) => address.IsIPv4MappedToIPv6 ? address.MapToIPv4() : address;

    private static int DefaultPort(string scheme) => string.Equals(scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase) ? 443 : 80;
}
