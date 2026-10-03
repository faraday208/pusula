using System.Text.Json.Serialization;
using Pusula.Indexing;

namespace Pusula.Tree;

/// <summary>The file tree of a source.</summary>
/// <param name="Root">Full path of the source's folder.</param>
/// <param name="Version">Version of the index the tree was built from; it changes whenever the index changes.</param>
/// <param name="BuiltAt">When the index was built.</param>
/// <param name="Nodes">The top-level entries: directories first, then files, each group ordered by name (ignoring case).</param>
public sealed record TreeResponse(string Root, long Version, DateTimeOffset BuiltAt, IReadOnlyList<TreeNode> Nodes);

/// <summary>A directory or a file in the tree. The <c>type</c> property is <c>Directory</c> or <c>File</c>.</summary>
/// <param name="Name">File or directory name.</param>
/// <param name="Path">Path relative to the root, separated by <c>/</c>; for a file this is the key that <c>/api/file</c> takes.</param>
/// <param name="Tokens">Estimated tokens: of the whole file, or the sum over all files below a directory.</param>
/// <param name="EverySessionTokens">Estimated tokens that are loaded at the start of every session: of the whole file when its load mode is <c>EverySession</c>, of its name and description only when it is <c>DescriptionEverySession</c>, otherwise 0; for a directory, the sum over all files below it. <c>tokens</c> counts the whole file whatever its load mode.</param>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "type")]
[JsonDerivedType(typeof(TreeDirectory), "Directory")]
[JsonDerivedType(typeof(TreeFile), "File")]
public abstract record TreeNode(string Name, string Path, int Tokens, int EverySessionTokens);

/// <summary>A directory that contains indexed files.</summary>
/// <param name="Name">Directory name.</param>
/// <param name="Path">Path relative to the root, separated by <c>/</c>.</param>
/// <param name="Tokens">Estimated tokens of all files below the directory, at any depth.</param>
/// <param name="EverySessionTokens">Estimated tokens that all files below the directory add to every session, at any depth.</param>
/// <param name="FileCount">The number of files below the directory, at any depth.</param>
/// <param name="Children">The entries inside: directories first, then files, each group ordered by name (ignoring case).</param>
public sealed record TreeDirectory(string Name, string Path, int Tokens, int EverySessionTokens, int FileCount, IReadOnlyList<TreeNode> Children)
    : TreeNode(Name, Path, Tokens, EverySessionTokens);

/// <summary>An indexed Markdown file.</summary>
/// <param name="Name">File name, extension included.</param>
/// <param name="Path">Index key of the file: its path relative to the root, separated by <c>/</c>; the value that <c>/api/file</c> takes.</param>
/// <param name="Tokens">Estimated tokens of the whole file.</param>
/// <param name="EverySessionTokens">Estimated tokens the file adds to every session: its whole text, only its name and description, or 0, depending on its load mode.</param>
/// <param name="Layer">The role of the file.</param>
/// <param name="LoadMode">When Claude Code loads the file.</param>
/// <param name="BrokenLinks">How many of the file's links point at something that does not exist.</param>
/// <param name="Orphan">True when the file is of a kind that should be linked to, and no other file links to it. A note of a vault or a plain Markdown folder is an orphan only when it also links to no other note.</param>
/// <param name="Tags">The tags of the note, without the leading <c>#</c>; left out when it has none (and for every file of a Claude Code folder).</param>
public sealed record TreeFile(string Name, string Path, int Tokens, int EverySessionTokens, Layer Layer, LoadMode LoadMode, int BrokenLinks, bool Orphan, IReadOnlyList<string>? Tags = null)
    : TreeNode(Name, Path, Tokens, EverySessionTokens);
