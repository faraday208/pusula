namespace Pusula.Startup;

/// <summary>
/// The line pusula prints once it listens on an address that other devices can reach. pusula has no login: whoever
/// reaches the address reads the folders it shows. Nothing is printed while it listens on this computer only
/// (<c>localhost</c>, <c>127.0.0.1</c>, <c>::1</c>).
/// </summary>
internal static class NetworkWarning
{
    /// <summary>The line for the addresses the server listens on; null when every one of them is of this computer only.</summary>
    /// <param name="addresses">The addresses as the server reports them once it listens, such as <c>http://192.0.2.10:5190</c>. One that cannot be read as an address counts as reachable.</param>
    public static string? For(IEnumerable<string> addresses)
    {
        ArgumentNullException.ThrowIfNull(addresses);

        string[] reachable = [.. addresses.Where(address => !IsThisComputerOnly(address))];
        return reachable.Length == 0
            ? null
            : $"pusula: listening on {string.Join(", ", reachable)}: anyone who can reach this address can read the folders shown here; there is no login.";
    }

    private static bool IsThisComputerOnly(string address) =>
        Uri.TryCreate(address, UriKind.Absolute, out Uri? uri) && uri.IsLoopback;
}
