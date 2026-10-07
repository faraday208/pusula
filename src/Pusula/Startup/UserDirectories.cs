namespace Pusula.Startup;

/// <summary>
/// The directories of the user that the sources depend on. The extension point of the tests: a test gives its own, so
/// that none of them reads or touches the home directory of whoever runs it.
/// </summary>
/// <param name="Home">The home directory of the user, which a leading <c>~</c> expands to; empty when it is not known.</param>
/// <param name="ApplicationData">The application data directory of the user (<c>~/.config</c> on Linux), where <c>pusula/sources.json</c> is looked for, whether it exists yet or not; empty when there is none.</param>
internal sealed record UserDirectories(string Home, string ApplicationData)
{
    /// <summary>The directories of the user the application runs as.</summary>
    public static UserDirectories FromEnvironment() => FromEnvironment(Environment.GetFolderPath);

    /// <summary>
    /// The directories that <paramref name="folderPath"/> gives (<see cref="Environment.GetFolderPath(Environment.SpecialFolder, Environment.SpecialFolderOption)"/>
    /// for the real ones). The application data directory is taken even when it does not exist yet, as on a new Mac without
    /// <c>~/.config</c>: verified, it would be empty, the sources file would have no place, and no folder could be added on the page.
    /// </summary>
    /// <param name="folderPath">Gives the path of a special folder.</param>
    internal static UserDirectories FromEnvironment(Func<Environment.SpecialFolder, Environment.SpecialFolderOption, string> folderPath) => new(
        folderPath(Environment.SpecialFolder.UserProfile, Environment.SpecialFolderOption.None),
        folderPath(Environment.SpecialFolder.ApplicationData, Environment.SpecialFolderOption.DoNotVerify));
}
