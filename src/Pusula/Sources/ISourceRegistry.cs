using System.Diagnostics.CodeAnalysis;

namespace Pusula.Sources;

/// <summary>The sources the server shows. The extension point of the tests, like <see cref="Pusula.Indexing.IIndexProvider"/> before it.</summary>
internal interface ISourceRegistry
{
    /// <summary>Full path of the sources file the list was read from, or looked for and not found; null when the folders were named on the command line or in <c>Pusula:Root</c>.</summary>
    string? SourcesFile { get; }

    /// <summary>Why the sources file cannot be used any more, when it was changed after the server started into something the reader rejects (the last valid list is still shown); null otherwise.</summary>
    string? SourcesFileError { get; }

    /// <summary>The sources, in the order they were configured; empty until the registry has started.</summary>
    IReadOnlyList<SourceEntry> Sources { get; }

    /// <summary>Finds a source by its id.</summary>
    /// <param name="id">The id, as it is written in the URL.</param>
    /// <param name="source">The source.</param>
    bool TryGet(string id, [NotNullWhen(true)] out SourceEntry? source);
}
