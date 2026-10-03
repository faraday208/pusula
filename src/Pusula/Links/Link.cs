namespace Pusula.Links;

/// <summary>A reference from one file to another, resolved against the index.</summary>
/// <param name="Kind">The syntax the reference was written in.</param>
/// <param name="Raw">The reference text exactly as found (clients match on <c>Kind + "|" + Raw</c>).</param>
/// <param name="Line">1-based line in the file (frontmatter lines included).</param>
/// <param name="Target">Root-relative normalized path with <c>/</c> separators; null when there is none.</param>
/// <param name="Heading">The part after <c>#</c>, if any.</param>
/// <param name="Status">What the link points at.</param>
internal sealed record Link(LinkKind Kind, string Raw, int Line, string? Target, string? Heading, LinkStatus Status);
