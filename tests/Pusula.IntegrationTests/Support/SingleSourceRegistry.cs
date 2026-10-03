using System.Diagnostics.CodeAnalysis;
using Pusula.Indexing;
using Pusula.Sources;

namespace Pusula.IntegrationTests.Support;

/// <summary>A registry with one source whose index is the given provider: the way tests put a fake index behind the endpoints.</summary>
internal sealed class SingleSourceRegistry : ISourceRegistry
{
    private readonly SourceEntry _entry;

    public SingleSourceRegistry(string id, IIndexProvider provider)
    {
        _entry = new SourceEntry(new SourceDefinition(id, id, "/root", SourceProfile.Claude), provider, Error: null);
    }

    public string? SourcesFile => null;

    public string? SourcesFileError => null;

    public IReadOnlyList<SourceEntry> Sources => [_entry];

    public bool TryGet(string id, [NotNullWhen(true)] out SourceEntry? source)
    {
        source = string.Equals(id, _entry.Definition.Id, StringComparison.Ordinal) ? _entry : null;
        return source is not null;
    }
}
