namespace Pusula.Links;

/// <summary>A reference found in a Markdown body, before it is resolved against the index.</summary>
/// <param name="Kind">The syntax the reference was written in.</param>
/// <param name="Raw">The reference text: the destination, the wiki link inner text, the path or the code span content.</param>
/// <param name="Line">1-based line in the file (frontmatter lines included).</param>
internal readonly record struct RawLink(LinkKind Kind, string Raw, int Line);
