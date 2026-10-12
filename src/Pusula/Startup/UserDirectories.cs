namespace Pusula.Startup;

/// <summary>
/// The directories of the user that the sources depend on. The extension point of the tests: a test gives its own, so
/// that none of them reads or touches the home directory of whoever runs it.
/// </summary>
/// <param name="Home">The home directory of the user, which a leading <c>~</c> expands to; empty when it is not known.</param>
/// <param name="ApplicationData">The application data directory of the user (<c>~/.config</c> on Linux), where <c>pusula/sources.json</c> is looked for, whether it exists yet or not; empty when there is none.</param>
/// <param name="State">The directory that the state folder of pusula goes in (<c>$XDG_STATE_HOME</c> or <c>~/.local/state</c> on Linux and macOS, <c>%LOCALAPPDATA%</c> on Windows), where <c>pusula/history</c> keeps the daily summaries of what changed, whether it exists yet or not; empty when there is none, which turns the history off.</param>
internal sealed record UserDirectories(string Home, string ApplicationData, string State = "")
{
    private const string StateVariable = "XDG_STATE_HOME";

    /// <summary>The directories of the user the application runs as.</summary>
    public static UserDirectories FromEnvironment() =>
        FromEnvironment(Environment.GetFolderPath, Environment.GetEnvironmentVariable, OperatingSystem.IsWindows());

    /// <summary>
    /// The directories that <paramref name="folderPath"/> gives (<see cref="Environment.GetFolderPath(Environment.SpecialFolder, Environment.SpecialFolderOption)"/>
    /// for the real ones). The application data directory is taken even when it does not exist yet, as on a new Mac without
    /// <c>~/.config</c>: verified, it would be empty, the sources file would have no place, and no folder could be added on the page.
    /// </summary>
    /// <param name="folderPath">Gives the path of a special folder.</param>
    internal static UserDirectories FromEnvironment(Func<Environment.SpecialFolder, Environment.SpecialFolderOption, string> folderPath) => new(
        folderPath(Environment.SpecialFolder.UserProfile, Environment.SpecialFolderOption.None),
        folderPath(Environment.SpecialFolder.ApplicationData, Environment.SpecialFolderOption.DoNotVerify));

    /// <summary>
    /// The directories that <paramref name="folderPath"/> gives, as above, and the state directory: where pusula's own state folder goes. It is taken
    /// even when it does not exist yet: nothing creates it before the first summary is written.
    /// </summary>
    /// <param name="folderPath">Gives the path of a special folder.</param>
    /// <param name="environmentVariable">Gives the value of an environment variable, null when it is not set.</param>
    /// <param name="isWindows">Whether this is Windows, where the state directory is the local application data directory.</param>
    internal static UserDirectories FromEnvironment(
        Func<Environment.SpecialFolder, Environment.SpecialFolderOption, string> folderPath,
        Func<string, string?> environmentVariable,
        bool isWindows)
    {
        UserDirectories directories = FromEnvironment(folderPath);
        return directories with { State = StateOf(directories.Home, folderPath, environmentVariable, isWindows) };
    }

    // Windows: the local application data directory. Elsewhere $XDG_STATE_HOME when it is an absolute path (the specification says a
    // relative one is invalid and must be ignored), else ~/.local/state; none when the home directory is not known.
    private static string StateOf(
        string home,
        Func<Environment.SpecialFolder, Environment.SpecialFolderOption, string> folderPath,
        Func<string, string?> environmentVariable,
        bool isWindows)
    {
        if (isWindows)
        {
            return folderPath(Environment.SpecialFolder.LocalApplicationData, Environment.SpecialFolderOption.DoNotVerify);
        }

        string? configured = environmentVariable(StateVariable);
        if (!string.IsNullOrEmpty(configured) && Path.IsPathFullyQualified(configured))
        {
            return configured;
        }

        return home.Length == 0 ? string.Empty : Path.Join(home, ".local", "state");
    }
}
