using System.Reflection;

namespace Pusula.Startup;

/// <summary>
/// The version of pusula itself: the <c>Version</c> in <c>Directory.Build.props</c>, such as <c>0.1.0</c>. It is what
/// <c>pusula --version</c> prints and what <c>GET /api/sources</c> reports as <c>version</c>.
/// </summary>
internal static class AppVersion
{
    private const string Unknown = "0.0.0";

    /// <summary>
    /// The version of this build, the number only: the commit that the SDK adds to the informational version after a
    /// <c>+</c> (<c>0.1.0+1a2b3c4</c>) is left off.
    /// </summary>
    public static string Current { get; } = FromInformationalVersion(
        typeof(AppVersion).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion);

    /// <summary>The one line <c>pusula --version</c> prints: the name and the version, <c>pusula 0.1.0</c>.</summary>
    public static string Line => $"pusula {Current}";

    /// <summary>Takes the version out of an informational version: what comes before the first <c>+</c>, without the spaces around it.</summary>
    /// <param name="informationalVersion">The <c>AssemblyInformationalVersion</c> of the assembly, or null when it has none.</param>
    /// <returns>The version; <c>0.0.0</c> when there is nothing before the <c>+</c> or no informational version at all.</returns>
    internal static string FromInformationalVersion(string? informationalVersion)
    {
        if (informationalVersion is null)
        {
            return Unknown;
        }

        int plus = informationalVersion.IndexOf('+');
        string version = (plus < 0 ? informationalVersion : informationalVersion[..plus]).Trim();
        return version.Length > 0 ? version : Unknown;
    }
}
