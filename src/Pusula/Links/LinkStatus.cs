namespace Pusula.Links;

/// <summary>What a link points at.</summary>
public enum LinkStatus
{
    /// <summary>An indexed Markdown file.</summary>
    Resolved,

    /// <summary>Exists under the root but is not indexed (a directory, a script, a skipped folder, ...).</summary>
    NonMarkdown,

    /// <summary>Does not exist.</summary>
    Broken,

    /// <summary>A wiki link inside a memory directory that matches nothing: a memory that is not written yet.</summary>
    Pending,

    /// <summary>Points outside the root or uses another scheme.</summary>
    External,
}
