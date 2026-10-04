namespace Pusula.Browse;

/// <summary>What a folder of the folder browser looks like, when it looks like something that a source can show: a vault, a Claude Code folder, or a folder of linked notes without a vault's mark. It is not the profile of a source (a folder of notes becomes a plain Markdown source when it is added).</summary>
public enum FolderKind
{
    /// <summary>An Obsidian vault: the folder has a <c>.obsidian</c> directory.</summary>
    Vault,

    /// <summary>A Claude Code configuration folder, as the <c>auto</c> profile of a source would decide it.</summary>
    Claude,

    /// <summary>
    /// A folder of linked notes that has no <c>.obsidian</c> directory and is recognized by its content: an entry note
    /// (<c>Home.md</c>, <c>index.md</c>, <c>README.md</c>, <c>MOC.md</c>, <c>_index.md</c> or <c>start.md</c>), at least 10
    /// Markdown files in it and up to 3 levels below it that are at least half of its files, and a wikilink in at least a
    /// fifth of a sample of them. Only the search for folders gives this kind, never the listing of a folder.
    /// </summary>
    Notes,
}
