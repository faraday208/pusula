using Pusula.Indexing;

namespace Pusula.Sources;

/// <summary>Which <see cref="SourceProfile"/> a folder gets: the one that is written down, or the one its content suggests.</summary>
internal static class SourceProfiles
{
    // A folder that has a CLAUDE.md and one of these looks like a Claude Code configuration folder.
    private static readonly string[] ClaudeDirectories = ["rules", "skills", "projects", "agents", "commands"];

    /// <summary>
    /// The profile of a folder that is left to "auto": a vault when it has a <c>.obsidian</c> directory; a Claude Code
    /// folder when it is called <c>.claude</c>, or has a <c>CLAUDE.md</c> next to <c>rules</c>, <c>skills</c>,
    /// <c>projects</c>, <c>agents</c> or <c>commands</c>; otherwise a plain folder of Markdown files. A folder that
    /// does not exist is judged by its name alone.
    /// </summary>
    /// <param name="fullPath">Full path of the folder.</param>
    public static SourceProfile Detect(string fullPath)
    {
        if (Directory.Exists(Path.Join(fullPath, ".obsidian")))
        {
            return SourceProfile.Vault;
        }

        if (string.Equals(SourceIds.NameOf(fullPath), ".claude", PathComparison.Current))
        {
            return SourceProfile.Claude;
        }

        return File.Exists(Path.Join(fullPath, "CLAUDE.md")) && Array.Exists(ClaudeDirectories, name => Directory.Exists(Path.Join(fullPath, name)))
            ? SourceProfile.Claude
            : SourceProfile.Markdown;
    }

    /// <summary>The profile as a sources file spells it: <c>claude</c>, <c>vault</c> or <c>markdown</c>.</summary>
    /// <param name="profile">The profile.</param>
    public static string Name(SourceProfile profile) => profile switch
    {
        SourceProfile.Claude => "claude",
        SourceProfile.Vault => "vault",
        SourceProfile.Markdown => "markdown",
        _ => throw new ArgumentOutOfRangeException(nameof(profile), profile, "Unknown profile."),
    };

    /// <summary>
    /// Reads the <c>profile</c> of a sources file: <c>auto</c>, <c>claude</c>, <c>vault</c> or <c>markdown</c>, in any
    /// case.
    /// </summary>
    /// <param name="text">What is written.</param>
    /// <param name="profile">The profile; null for <c>auto</c>.</param>
    /// <returns><c>false</c> when the text is none of the four.</returns>
    public static bool TryParse(string text, out SourceProfile? profile)
    {
        profile = null;
        if (string.Equals(text, "auto", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        foreach (SourceProfile candidate in Enum.GetValues<SourceProfile>())
        {
            if (string.Equals(text, candidate.ToString(), StringComparison.OrdinalIgnoreCase))
            {
                profile = candidate;
                return true;
            }
        }

        return false;
    }
}
