namespace Pusula.IntegrationTests.Support;

/// <summary>
/// A made-up configuration folder in a scratch directory. Nothing in it comes from a real <c>~/.claude</c>. It has
/// one file or more of every kind of thing the server has to deal with, and two files (<c>settings.json</c> and
/// <c>.credentials.json</c>) whose contents must never show up in any response.
/// </summary>
internal sealed class SyntheticRoot : IDisposable
{
    /// <summary>The id the application gives to the root: the folder is always called <c>root</c>.</summary>
    public const string SourceId = "root";

    public const string SettingsSecret = "SETTINGS-SECRET-7f3a91";
    public const string CredentialsSecret = "CREDENTIALS-SECRET-c20b44";
    public const string OutsideSecret = "OUTSIDE-SECRET-9e1d05";

    private readonly TempDirectory _sandbox = new();

    public SyntheticRoot()
    {
        Path = _sandbox.Resolve("root");
        Directory.CreateDirectory(Path);

        Write(
            "CLAUDE.md",
            """
            # Synthetic config

            Read [the style rule](rules/style.md) and ~/.claude/rules/scoped.md and `skills/deploy/SKILL.md`.
            Broken: [gone](rules/gone.md) and `rules/missing.md`.
            """);
        Write("settings.json", $$"""{"outputStyle":"Terse","apiKey":"{{SettingsSecret}}"}""");
        Write(".credentials.json", $$"""{"accessToken":"{{CredentialsSecret}}"}""");

        Write(
            "rules/style.md",
            """
            ---
            description: Style rule
            ---
            # Style
            Use [[unknown-target]] for details.
            """);
        Write(
            "rules/scoped.md",
            """
            ---
            paths:
              - "src/**/*.cs"
            ---
            Only when a C# file is read.
            """);
        Write("rules/kurallar ve şablon.md", "# Şablon\n");

        Write(
            "skills/deploy/SKILL.md",
            """
            ---
            name: deploy
            description: Deploys the thing
            ---
            # Deploy
            Steps live in [notes](notes.md).
            """);
        Write("skills/deploy/notes.md", "# Notes\nOnly SKILL.md links here.\n");
        Write("skills/deploy/unlinked.md", "# Unlinked\n");
        Write("agents/reviewer.md", "---\nname: reviewer\ndescription: Reviews code\n---\nReview.\n");
        Write("output-styles/terse.md", "---\nname: Terse\n---\nBe terse.\n");
        Write("output-styles/verbose.md", "---\nname: Verbose\n---\nBe verbose.\n");

        Write("projects/demo/memory/MEMORY.md", "- [[alpha]]\n- [[ghost]]\n");
        Write("projects/demo/memory/alpha.md", "---\nname: alpha\ntype: note\n---\nAlpha memory.\n");
        Write("projects/demo/memory/lonely.md", "# Lonely\n");
        Write("projects/demo/transcript.jsonl", $$"""{"message":"{{CredentialsSecret}}"}""");

        Write("reference.md", "---\nkey: [unclosed\n---\nReference with broken frontmatter.\n");
        Write("shared/shared-note.md", "# Shared\n");

        Write("plugins/cache/ignored.md", "# Not scanned: runtime directory\n");
        Write(".hidden/secret.md", $"# Hidden\n{SettingsSecret}\n");

        // A file next to the root (not inside it): paths that climb out of the root must never reach it.
        _sandbox.Write("outside.md", $"# Outside the root\n{OutsideSecret}\n");
    }

    /// <summary>Full path of the root folder.</summary>
    public string Path { get; }

    /// <summary>The URL of an endpoint of the root's source: <c>Api("tree")</c> is <c>/api/sources/root/tree</c>.</summary>
    /// <param name="endpoint">What comes after the id, query string included.</param>
    public static string Api(string endpoint) => $"/api/sources/{SourceId}/{endpoint}";

    /// <summary>Full path of the file that sits next to the root folder.</summary>
    public string OutsideFile => _sandbox.Resolve("outside.md");

    /// <summary>Every file that the index must contain, as index keys.</summary>
    public static IReadOnlyList<string> IndexedFiles { get; } =
    [
        "CLAUDE.md",
        "agents/reviewer.md",
        "output-styles/terse.md",
        "output-styles/verbose.md",
        "projects/demo/memory/MEMORY.md",
        "projects/demo/memory/alpha.md",
        "projects/demo/memory/lonely.md",
        "reference.md",
        "rules/kurallar ve şablon.md",
        "rules/scoped.md",
        "rules/style.md",
        "shared/shared-note.md",
        "skills/deploy/SKILL.md",
        "skills/deploy/notes.md",
        "skills/deploy/unlinked.md",
    ];

    /// <summary>Writes a UTF-8 file below the root, creating directories.</summary>
    public string Write(string relativePath, string content)
    {
        string fullPath = System.IO.Path.Combine(Path, relativePath.Replace('/', System.IO.Path.DirectorySeparatorChar));
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(fullPath)!);
        File.WriteAllText(fullPath, content);
        return fullPath;
    }

    public void Dispose() => _sandbox.Dispose();
}
