using System.Diagnostics.CodeAnalysis;
using Pusula.Indexing;
using Pusula.Sources;

namespace Pusula.UnitTests.Support;

/// <summary>A registry whose sources are given by their folders: none of them is started (they have no index), which is all that the code that only looks at the folders needs.</summary>
internal sealed class FakeSourceRegistry : ISourceRegistry
{
    private readonly SourceEntry[] _entries;

    public FakeSourceRegistry(params string[] folders)
    {
        _entries =
        [
            .. folders.Select((folder, number) => new SourceEntry(
                new SourceDefinition($"source-{number}", $"Source {number}", folder, SourceProfile.Markdown),
                Index: null,
                Error: "Not started.")),
        ];
    }

    public string? SourcesFile => null;

    public string? SourcesFileError => null;

    public IReadOnlyList<SourceEntry> Sources => _entries;

    public bool TryGet(string id, [NotNullWhen(true)] out SourceEntry? source)
    {
        source = Array.Find(_entries, entry => entry.Definition.Id == id);
        return source is not null;
    }
}
