namespace Pusula.Startup;

/// <summary>Settings in the <c>Pusula</c> configuration section, for example <c>--Pusula:AllowedHosts "a;b"</c>.</summary>
internal sealed class PusulaOptions
{
    /// <summary>Name of the configuration section.</summary>
    internal const string SectionName = "Pusula";

    /// <summary>Configuration key of <see cref="Roots"/>; the folders are <c>Pusula:Roots:0</c>, <c>Pusula:Roots:1</c>, ...</summary>
    internal const string RootsKey = SectionName + ":Roots";

    /// <summary>The folder that is shown when nothing else says what to show.</summary>
    internal const string DefaultRoot = "~/.claude";

    /// <summary>
    /// The one folder to show; a leading <c>~</c> stands for the user's home directory. It is used when no folder was
    /// given on the command line (<see cref="Roots"/>); when it is empty too, the sources file says what to show, and
    /// without one <see cref="DefaultRoot"/> is shown. <see cref="ResolveRoot(string?, string)"/> gives the folder.
    /// </summary>
    public string? Root { get; set; }

    /// <summary>
    /// The folders given on the command line, <c>pusula &lt;folder&gt; [&lt;folder&gt; ...]</c>; they win over everything
    /// else. Set by the command line itself (see <c>CommandLine</c>), not meant to be configured elsewhere.
    /// </summary>
    public string[]? Roots { get; set; }

    /// <summary>
    /// The file that lists the sources, used when neither <see cref="Roots"/> nor <see cref="Root"/> is set; a leading
    /// <c>~</c> stands for the user's home directory. When empty, <c>pusula/sources.json</c> in the application data
    /// directory of the user (<c>$XDG_CONFIG_HOME</c> or <c>~/.config</c> on Linux) is looked for.
    /// </summary>
    public string? SourcesFile { get; set; }

    /// <summary>Additional host names the server answers to, separated by <c>;</c> (for example the name of a reverse proxy).</summary>
    public string? AllowedHosts { get; set; }

    /// <summary>
    /// Whether a request from another machine may add and remove sources (<c>POST /api/sources</c>,
    /// <c>DELETE /api/sources/{id}</c>), <c>false</c> by default: only a request from the machine the server runs on
    /// may. When it is <c>true</c> that check (a loopback address, the address the request arrived at, no reverse proxy
    /// header) is not made, so every machine that can reach the server can show any folder of this machine, and read the
    /// Markdown in it, through it. Nothing else is relaxed: the sources still have to come from a sources file, the
    /// request still has to come from a page of this server (its <c>Origin</c> and <c>Sec-Fetch-Site</c> headers), and the
    /// <c>Host</c> check still comes first.
    /// </summary>
    public bool AllowRemoteEdit { get; set; }

    /// <summary>The names in <see cref="AllowedHosts"/>: trimmed, without empty entries.</summary>
    public string[] ParseAllowedHosts() =>
        AllowedHosts?.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries) ?? [];

    /// <summary>Resolves a configured root against a home directory.</summary>
    /// <param name="root">The configured value; null or blank selects <see cref="DefaultRoot"/>.</param>
    /// <param name="homeDirectory">The user's home directory, which <c>~</c> expands to.</param>
    /// <exception cref="InvalidOperationException">The root starts with <c>~</c> but there is no home directory.</exception>
    internal static string ResolveRoot(string? root, string homeDirectory)
    {
        string configured = string.IsNullOrWhiteSpace(root) ? DefaultRoot : root.Trim();
        return Path.GetFullPath(ExpandHome(configured, homeDirectory));
    }

    /// <summary>Replaces a leading <c>~</c>, <c>~/</c> or <c>~\</c> by the home directory; anything else is returned as it is.</summary>
    /// <param name="path">A path that may start with <c>~</c>.</param>
    /// <param name="homeDirectory">The user's home directory.</param>
    /// <exception cref="InvalidOperationException">The path starts with <c>~</c> but <paramref name="homeDirectory"/> is empty.</exception>
    internal static string ExpandHome(string path, string homeDirectory)
    {
        bool isHome = path == "~";
        bool isUnderHome = path.StartsWith("~/", StringComparison.Ordinal) || path.StartsWith("~\\", StringComparison.Ordinal);
        if (!isHome && !isUnderHome)
        {
            return path;
        }

        if (string.IsNullOrEmpty(homeDirectory))
        {
            throw new InvalidOperationException($"Cannot expand '~' in '{path}': the home directory of the current user is unknown.");
        }

        return isHome ? homeDirectory : Path.Join(homeDirectory, path[2..]);
    }
}
