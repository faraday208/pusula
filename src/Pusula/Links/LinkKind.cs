namespace Pusula.Links;

/// <summary>The syntax a reference to another file was written in.</summary>
public enum LinkKind
{
    /// <summary>A Markdown link: <c>[text](target)</c>.</summary>
    MarkdownLink,

    /// <summary>An Obsidian-style wiki link: <c>[[target#heading|alias]]</c>.</summary>
    WikiLink,

    /// <summary>A reference of the form <c>~/.claude/...</c>, mapped onto the configuration root.</summary>
    ClaudePath,

    /// <summary>A relative path written as an inline code span, such as <c>rules/example.md</c>.</summary>
    RelativePath,

    /// <summary>An Obsidian-style embed: <c>![[target#heading|size]]</c>. Resolved like a wiki link.</summary>
    Embed,
}
