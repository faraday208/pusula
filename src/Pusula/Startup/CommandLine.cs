namespace Pusula.Startup;

/// <summary>
/// The command line <c>pusula [folder ...] [host options]</c>, split into the folders to show and the arguments that
/// belong to the ASP.NET Core host (<c>--urls</c>, <c>--Pusula:AllowedHosts</c>, ...).
/// </summary>
/// <param name="Roots">The folders: every argument at the start that is not empty and does not start with <c>-</c>. Empty when there is none.</param>
/// <param name="HostArguments">The remaining arguments, in their original order.</param>
internal sealed record CommandLine(string[] Roots, string[] HostArguments)
{
    /// <summary>The option that asks for the version: <c>pusula --version</c>.</summary>
    internal const string VersionOption = "--version";

    /// <summary>
    /// Whether the command line asks for the version: <see cref="VersionOption"/> is among the host arguments, whatever
    /// stands before it (the folders, other options). It is answered before anything else, so no folder is looked at and
    /// no host is built.
    /// </summary>
    public bool WantsVersion => HostArguments.Contains(VersionOption);

    /// <summary>
    /// Takes the arguments at the start as the folders, up to the first one that is empty or starts with <c>-</c>
    /// (the rest, an option's value such as the one of <c>--urls</c> included, belongs to the host). The folders must
    /// not be handed to the host: its command-line configuration provider reads a leading <c>/</c> as a switch prefix,
    /// so an absolute path such as <c>/home/me/.claude</c> would be parsed as an option instead of a path.
    /// </summary>
    /// <param name="args">The process arguments.</param>
    public static CommandLine Parse(string[] args)
    {
        ArgumentNullException.ThrowIfNull(args);

        int count = 0;
        while (count < args.Length && args[count].Length > 0 && args[count][0] != '-')
        {
            count++;
        }

        return new CommandLine(args[..count], args[count..]);
    }

    /// <summary>
    /// Checks the folders that were given at the start of the command line, before the host is built, so that a
    /// mistyped folder ends with one line on the error stream instead of a stack trace. Only those are checked: a root
    /// that comes from the settings, the environment or <c>--Pusula:Root</c> is still reported by the index host when it starts.
    /// </summary>
    /// <param name="homeDirectory">The user's home directory, which a leading <c>~</c> expands to.</param>
    /// <param name="directoryExists">Tells whether a folder exists; <see cref="Directory.Exists(string?)"/> in the application.</param>
    /// <returns>The line to print for the first folder that does not exist; null when they all exist or when no folder was given.</returns>
    /// <exception cref="InvalidOperationException">A folder starts with <c>~</c> but <paramref name="homeDirectory"/> is empty.</exception>
    public string? FindRootError(string homeDirectory, Func<string, bool> directoryExists)
    {
        foreach (string root in Roots)
        {
            // The same resolution the sources apply later, so that the folder that is checked is the folder that would be served.
            string fullPath = PusulaOptions.ResolveRoot(root, homeDirectory);
            if (!directoryExists(fullPath))
            {
                return $"pusula: root folder not found: {fullPath}";
            }
        }

        return null;
    }

    /// <summary>
    /// Makes the folders given on the command line the <c>Pusula:Roots</c> setting. They are added as the last (highest
    /// priority) source, so they win over the settings file, the environment, <c>--Pusula:Root</c> and the sources file.
    /// Does nothing when no folder was given.
    /// </summary>
    /// <param name="configuration">The application configuration.</param>
    public void AddRootsTo(IConfigurationBuilder configuration)
    {
        if (Roots.Length > 0)
        {
            configuration.AddInMemoryCollection(Roots.Select((root, index) =>
                new KeyValuePair<string, string?>($"{PusulaOptions.RootsKey}:{index}", root)));
        }
    }
}
