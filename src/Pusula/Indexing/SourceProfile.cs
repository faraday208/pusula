namespace Pusula.Indexing;

/// <summary>What kind of folder a source is. It decides how the folder is scanned and how its links are resolved.</summary>
public enum SourceProfile
{
    /// <summary>A Claude Code configuration folder such as <c>~/.claude</c>: load layers, token shares, memory names.</summary>
    Claude,

    /// <summary>An Obsidian vault (a folder with a <c>.obsidian</c> directory): plain notes, wiki links, embeds, tags.</summary>
    Vault,

    /// <summary>Any other folder of Markdown files, scanned and linked like a vault.</summary>
    Markdown,
}
