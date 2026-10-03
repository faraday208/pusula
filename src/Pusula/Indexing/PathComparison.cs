namespace Pusula.Indexing;

/// <summary>The path comparison policy of the current platform.</summary>
internal static class PathComparison
{
    /// <summary>
    /// Ordinal on case-sensitive platforms, ordinal-ignore-case on Windows. Index keys, directory names and
    /// real paths are all compared with this policy.
    /// </summary>
    public static StringComparison Current { get; } =
        OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
}
