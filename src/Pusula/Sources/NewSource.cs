namespace Pusula.Sources;

/// <summary>A request to add a source, after <see cref="SourceRequestValidator"/> has checked it.</summary>
/// <param name="Path">The path as the user wrote it (trimmed, without a separator at the end; <c>~</c> stays): this is what the sources file gets.</param>
/// <param name="FullPath">Full path of the folder, <c>~</c> expanded and without a separator at the end; the folder exists.</param>
/// <param name="Name">The name to show.</param>
/// <param name="Profile">The profile as the sources file gets it: <c>auto</c>, <c>claude</c>, <c>vault</c> or <c>markdown</c>.</param>
internal sealed record NewSource(string Path, string FullPath, string Name, string Profile);
