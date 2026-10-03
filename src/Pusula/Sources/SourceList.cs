using System.Diagnostics.CodeAnalysis;
using Pusula.Startup;

namespace Pusula.Sources;

/// <summary>The folders the server shows, and where the list came from.</summary>
/// <param name="Sources">The sources, in order.</param>
/// <param name="SourcesFile">Full path of the sources file that the list was read from, or that was looked for and not found; null when the folders were named on the command line or in <c>Pusula:Root</c>.</param>
internal sealed record SourceList(IReadOnlyList<SourceDefinition> Sources, string? SourcesFile)
{
    /// <summary>
    /// Decides what to show, in this order: the folders given on the command line (<see cref="PusulaOptions.Roots"/>),
    /// <see cref="PusulaOptions.Root"/>, the sources file (<see cref="PusulaOptions.SourcesFile"/>, or
    /// <c>pusula/sources.json</c> in the application data directory, which is <c>~/.config</c> on Linux), and last
    /// <see cref="PusulaOptions.DefaultRoot"/>.
    /// </summary>
    /// <param name="options">The settings.</param>
    /// <param name="homeDirectory">The user's home directory, which a leading <c>~</c> expands to.</param>
    /// <param name="applicationData">The application data directory of the user; empty when there is none.</param>
    /// <param name="list">What to show.</param>
    /// <param name="error">The line to print when the sources file cannot be used.</param>
    /// <exception cref="InvalidOperationException">A folder starts with <c>~</c> but <paramref name="homeDirectory"/> is empty.</exception>
    public static bool TryResolve(
        PusulaOptions options,
        string homeDirectory,
        string applicationData,
        [NotNullWhen(true)] out SourceList? list,
        [NotNullWhen(false)] out string? error)
    {
        error = null;
        if (options.Roots is { Length: > 0 } roots)
        {
            list = new SourceList(FromFolders(roots, homeDirectory), SourcesFile: null);
            return true;
        }

        if (!string.IsNullOrWhiteSpace(options.Root))
        {
            list = new SourceList(FromFolders([options.Root], homeDirectory), SourcesFile: null);
            return true;
        }

        string? file = FilePath(options, homeDirectory, applicationData);
        if (file is not null && File.Exists(file))
        {
            if (SourcesFileReader.TryRead(file, homeDirectory, out IReadOnlyList<SourceDefinition>? sources, out string? reason))
            {
                list = new SourceList(sources, file);
                return true;
            }

            list = null;
            error = $"pusula: sources file {file}: {reason}";
            return false;
        }

        list = new SourceList(FromFolders([PusulaOptions.DefaultRoot], homeDirectory), file);
        return true;
    }

    // The folders the user named: the application does not start without them.
    private static SourceDefinition[] FromFolders(string[] folders, string homeDirectory)
    {
        var taken = new HashSet<string>(StringComparer.Ordinal);
        var sources = new SourceDefinition[folders.Length];
        for (int i = 0; i < sources.Length; i++)
        {
            string path = PusulaOptions.ResolveRoot(folders[i], homeDirectory);
            string name = SourceIds.NameOf(path);
            sources[i] = new SourceDefinition(SourceIds.MakeUnique(SourceIds.Derive(name), taken), name, path, SourceProfiles.Detect(path), IsRequired: true);
        }

        return sources;
    }

    private static string? FilePath(PusulaOptions options, string homeDirectory, string applicationData)
    {
        if (!string.IsNullOrWhiteSpace(options.SourcesFile))
        {
            return Path.GetFullPath(PusulaOptions.ExpandHome(options.SourcesFile.Trim(), homeDirectory));
        }

        return string.IsNullOrEmpty(applicationData) ? null : Path.Join(applicationData, "pusula", "sources.json");
    }
}
