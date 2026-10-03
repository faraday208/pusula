namespace Pusula.Indexing;

/// <summary>The role a Markdown file plays inside a Claude Code configuration folder.</summary>
public enum Layer
{
    /// <summary>The root <c>CLAUDE.md</c>.</summary>
    ClaudeMd,

    /// <summary>A rule under <c>rules/</c> without a <c>paths</c> frontmatter key.</summary>
    Rule,

    /// <summary>A rule under <c>rules/</c> scoped by a <c>paths</c> frontmatter key.</summary>
    PathRule,

    /// <summary>A <c>SKILL.md</c> under <c>skills/</c>.</summary>
    Skill,

    /// <summary>Any other Markdown file under <c>skills/</c>.</summary>
    SkillResource,

    /// <summary>A sub-agent definition under <c>agents/</c>.</summary>
    Agent,

    /// <summary>A slash command under <c>commands/</c>.</summary>
    Command,

    /// <summary>An output style under <c>output-styles/</c>.</summary>
    OutputStyle,

    /// <summary>A <c>projects/&lt;project&gt;/memory/MEMORY.md</c> index.</summary>
    MemoryIndex,

    /// <summary>Any other file under <c>projects/&lt;project&gt;/memory/</c>.</summary>
    Memory,

    /// <summary>Any other Markdown file at the root.</summary>
    Reference,

    /// <summary>A file under <c>shared/</c>.</summary>
    Shared,

    /// <summary>Everything else.</summary>
    Other,

    /// <summary>A note of an Obsidian vault or of a plain folder of Markdown files (every file of those sources).</summary>
    Note,
}
