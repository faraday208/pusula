using System.Net;
using System.Net.Sockets;

namespace Pusula.Security;

/// <summary>
/// The decision behind <see cref="HostGuardMiddleware"/>: may a request with this <c>Host</c> header be served? The
/// server only answers to names that lead to the machine it runs on. A malicious web page that rebinds its own DNS
/// name to this server can reach it from the browser, but its requests still carry the page's name in the header.
/// </summary>
internal static class HostGuard
{
    private const string LocalhostName = "localhost";
    private const string LocalhostSuffix = ".localhost";
    private const string TailnetSuffix = ".ts.net";
    private const int MaxHostLength = 253;
    private const int MaxPort = 65535;

    /// <summary>
    /// True when the host part of <paramref name="hostHeader"/> is an IP literal (IPv4 in dotted-quad form, IPv6 in
    /// brackets), <c>localhost</c> or a <c>*.localhost</c> name, the name of this machine, a <c>*.ts.net</c> name, or
    /// one of <paramref name="allowedHosts"/>. Names are compared ignoring case and the port is ignored. A missing,
    /// empty or malformed header is never allowed.
    /// </summary>
    /// <param name="hostHeader">The raw <c>Host</c> header value, such as <c>localhost:5190</c> or <c>[::1]:5190</c>.</param>
    /// <param name="machineName">The name of this machine (<c>Dns.GetHostName()</c>).</param>
    /// <param name="allowedHosts">Extra names from configuration; host names without a port.</param>
    public static bool IsAllowed(string? hostHeader, string machineName, IReadOnlyCollection<string> allowedHosts)
    {
        if (!TryParse(hostHeader, out string host, out bool isIpAddress))
        {
            return false;
        }

        return isIpAddress
            || IsLocalhost(host)
            || IsTailnetName(host)
            || SameName(host, machineName)
            || allowedHosts.Any(allowed => SameName(host, allowed));
    }

    /// <summary>
    /// Splits a <c>Host</c> header into its host part and validates it: an optional port of 1 to 5 digits, and a
    /// host that is a bracketed IPv6 address, a canonical IPv4 address, or a DNS name made of letters, digits,
    /// <c>-</c> and <c>_</c> in non-empty dot-separated labels.
    /// </summary>
    /// <param name="hostHeader">The raw header value.</param>
    /// <param name="host">The host without the port (IPv6 addresses keep their brackets).</param>
    /// <param name="isIpAddress">True when <paramref name="host"/> is an IP literal.</param>
    internal static bool TryParse(string? hostHeader, out string host, out bool isIpAddress)
    {
        host = string.Empty;
        isIpAddress = false;

        if (string.IsNullOrEmpty(hostHeader))
        {
            return false;
        }

        ReadOnlySpan<char> value = hostHeader;
        if (value[0] == '[')
        {
            int close = value.IndexOf(']');
            if (close < 0 || !IsEmptyOrPort(value[(close + 1)..]) || !IsIpv6Address(value[1..close]))
            {
                return false;
            }

            host = hostHeader[..(close + 1)];
            isIpAddress = true;
            return true;
        }

        int colon = value.LastIndexOf(':');
        ReadOnlySpan<char> name = colon < 0 ? value : value[..colon];
        if (colon >= 0 && !IsPort(value[(colon + 1)..]))
        {
            return false;
        }

        if (IsIpv4Address(name))
        {
            host = name.ToString();
            isIpAddress = true;
            return true;
        }

        if (!IsDnsName(name))
        {
            return false;
        }

        host = name.ToString();
        return true;
    }

    private static bool IsLocalhost(string host) =>
        SameName(host, LocalhostName) || HasLabelSuffix(host, LocalhostSuffix);

    private static bool IsTailnetName(string host) => HasLabelSuffix(host, TailnetSuffix);

    // "*.suffix": at least one label in front of the suffix, so the bare suffix itself does not match.
    private static bool HasLabelSuffix(string host, string suffix) =>
        host.Length > suffix.Length && host.EndsWith(suffix, StringComparison.OrdinalIgnoreCase);

    private static bool SameName(string host, string name) => string.Equals(host, name, StringComparison.OrdinalIgnoreCase);

    // "" (no port separator) or ":" followed by a port.
    private static bool IsEmptyOrPort(ReadOnlySpan<char> rest) =>
        rest.IsEmpty || (rest[0] == ':' && IsPort(rest[1..]));

    private static bool IsPort(ReadOnlySpan<char> text) =>
        text.Length is >= 1 and <= 5
        && text.IndexOfAnyExceptInRange('0', '9') < 0
        && int.Parse(text, System.Globalization.CultureInfo.InvariantCulture) <= MaxPort;

    // Only the canonical dotted-quad spelling counts: "127.1", "0x7f.1" and "2130706433" are not IP literals here.
    private static bool IsIpv4Address(ReadOnlySpan<char> text) =>
        IPAddress.TryParse(text, out IPAddress? address)
        && address.AddressFamily == AddressFamily.InterNetwork
        && text.SequenceEqual(address.ToString());

    private static bool IsIpv6Address(ReadOnlySpan<char> text) =>
        !text.Contains('%')
        && IPAddress.TryParse(text, out IPAddress? address)
        && address.AddressFamily == AddressFamily.InterNetworkV6;

    private static bool IsDnsName(ReadOnlySpan<char> name)
    {
        if (name.IsEmpty || name.Length > MaxHostLength)
        {
            return false;
        }

        bool labelIsEmpty = true;
        foreach (char c in name)
        {
            if (c == '.')
            {
                if (labelIsEmpty)
                {
                    return false;
                }

                labelIsEmpty = true;
            }
            else if (char.IsAsciiLetterOrDigit(c) || c is '-' or '_')
            {
                labelIsEmpty = false;
            }
            else
            {
                return false;
            }
        }

        return !labelIsEmpty;
    }
}
