namespace Pusula.Links;

/// <summary>Everything <see cref="NoteLinkResolver"/> needs to know about a folder of notes. Built from primitives and BCL types only.</summary>
/// <param name="RootFullPath">Full path of the folder.</param>
/// <param name="NotePaths">Index keys: root-relative paths of the Markdown files that are indexed as notes, <c>/</c> separated.</param>
/// <param name="FilePaths">
/// The root-relative paths of every file of the folder that is not hidden, notes included, <c>/</c> separated. What is
/// in here and is not a note (an image, a PDF, a Markdown file that was too large to index) is an attachment.
/// </param>
internal sealed record NoteLinkContext(string RootFullPath, IReadOnlyCollection<string> NotePaths, IReadOnlyCollection<string> FilePaths);
