using System.Net;
using System.Net.Sockets;
using Microsoft.AspNetCore.Server.Kestrel.Transport.Sockets;
using Microsoft.Extensions.Configuration.Json;
using Microsoft.Extensions.Configuration.Memory;

namespace Pusula.Startup;

/// <summary>
/// The address pusula listens on when nobody chose one (<c>http://localhost:5190</c>), and what to do when that port is not free: a
/// port that something holds, or that the system keeps back, would otherwise stop pusula from starting at all. A port that was
/// chosen (<c>--urls</c>, the environment) is never changed.
/// </summary>
internal static class DefaultUrls
{
    /// <summary>The address to listen on instead: every loopback address, any free port. Kestrel takes port 0 for an IP address only, not for <c>localhost</c>.</summary>
    internal const string FallbackAddress = "http://127.0.0.1:0";

    private static readonly string[] EnvironmentVariables = ["ASPNETCORE_URLS", "DOTNET_URLS", "URLS"];

    /// <summary>
    /// Whether the <c>urls</c> setting is the default and not a choice: the provider that wins for it (the last one that has it) is the
    /// built-in settings (memory) or a JSON settings file. The command line, the environment (<c>ASPNETCORE_URLS</c>,
    /// <c>DOTNET_URLS</c>, <c>URLS</c>, and <c>launchSettings</c> sets the environment) and any other kind of provider mean it was chosen.
    /// </summary>
    /// <param name="environment">Reads an environment variable. The host's own settings come under a <c>appsettings.json</c> in the content root, so the
    /// provider alone cannot tell that <c>ASPNETCORE_URLS</c>, <c>DOTNET_URLS</c> or <c>URLS</c> was set: such a variable always makes the urls a choice.</param>
    /// <param name="providers">The providers of the configuration, the one with the least weight first (<c>IConfigurationRoot.Providers</c>).</param>
    public static bool IsDefault(Func<string, string?> environment, IEnumerable<IConfigurationProvider> providers)
    {
        ArgumentNullException.ThrowIfNull(providers);
        ArgumentNullException.ThrowIfNull(environment);

        if (EnvironmentVariables.Any(name => !string.IsNullOrEmpty(environment(name))))
        {
            return false;
        }

        IConfigurationProvider? winner = providers.LastOrDefault(provider => provider.TryGet(WebHostDefaults.ServerUrlsKey, out _));
        return winner is MemoryConfigurationProvider or JsonConfigurationProvider or JsonStreamConfigurationProvider;
    }

    /// <summary>
    /// The address to listen on instead of the default, when the port of the default cannot be used; null when the default stays.
    /// </summary>
    /// <param name="isDefault">Whether the urls are the default (<see cref="IsDefault"/>); if not, there is nothing to change.</param>
    /// <param name="urls">The <c>urls</c> setting.</param>
    /// <param name="canBind">Tells whether a port can be bound; <see cref="CanBind"/> in the application.</param>
    public static string? Fallback(bool isDefault, string? urls, Func<int, bool> canBind)
    {
        ArgumentNullException.ThrowIfNull(canBind);

        if (!isDefault || PortOf(urls) is not { } port)
        {
            return null;
        }

        return canBind(port) ? null : FallbackAddress;
    }

    /// <summary>The port of the one address of the setting; null when there is not exactly one address, or it has no usable port.</summary>
    internal static int? PortOf(string? urls)
    {
        string[] addresses = urls?.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries) ?? [];
        return addresses.Length == 1 && Uri.TryCreate(addresses[0], UriKind.Absolute, out Uri? uri) && uri.Port > 0 ? uri.Port : null;
    }

    /// <summary>
    /// Whether a port can be bound on the loopback addresses that Kestrel binds for <c>localhost</c>: IPv4 and, where there is IPv6, IPv6. The
    /// sockets are made the way Kestrel makes its own and closed at once. A port that is taken or that may not be used is not free; an
    /// address family that the computer does not have does not count (Kestrel leaves it out as well).
    /// </summary>
    /// <param name="port">The port.</param>
    public static bool CanBind(int port)
    {
        IPAddress[] addresses = Socket.OSSupportsIPv6 ? [IPAddress.Loopback, IPAddress.IPv6Loopback] : [IPAddress.Loopback];
        foreach (IPAddress address in addresses)
        {
            try
            {
                using Socket socket = SocketTransportOptions.CreateDefaultBoundListenSocket(new IPEndPoint(address, port));
            }
            catch (SocketException exception) when (exception.SocketErrorCode is SocketError.AddressAlreadyInUse or SocketError.AccessDenied)
            {
                return false;
            }
            catch (SocketException)
            {
                // Not this address (no IPv6 on the loopback, for one): the other address decides.
            }
        }

        return true;
    }
}
