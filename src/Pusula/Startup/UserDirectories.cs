namespace Pusula.Startup;

/// <summary>
/// The directories of the user that the sources depend on. The extension point of the tests: a test gives its own, so
/// that none of them reads or touches the home directory of whoever runs it.
/// </summary>
/// <param name="Home">The home directory of the user, which a leading <c>~</c> expands to; empty when it is not known.</param>
/// <param name="ApplicationData">The application data directory of the user (<c>~/.config</c> on Linux), where <c>pusula/sources.json</c> is looked for; empty when there is none.</param>
internal sealed record UserDirectories(string Home, string ApplicationData)
{
    /// <summary>The directories of the user the application runs as.</summary>
    public static UserDirectories FromEnvironment() => new(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData));
}
