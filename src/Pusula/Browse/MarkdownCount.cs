namespace Pusula.Browse;

/// <summary>The number of Markdown files below a folder.</summary>
/// <param name="Count">The files that were counted: at most the limit.</param>
/// <param name="More">True when there are more files than that; the counting stopped at the limit.</param>
internal readonly record struct MarkdownCount(int Count, bool More);
