using Pusula.Links;

namespace Pusula.UnitTests.Support;

/// <summary>An in-memory description of a configuration tree for <see cref="LinkResolver"/> tests; nothing touches the disk.</summary>
internal sealed class TreeFixture
{
    public TreeFixture(StringComparison comparison = StringComparison.Ordinal, string root = "/home/test/.claude")
    {
        Comparison = comparison;
        Root = root;
        StringComparer comparer = StringComparer.FromComparison(comparison);
        Indexed = new HashSet<string>(comparer);
        Existing = new HashSet<string>(comparer);
        TopLevelDirectories = new HashSet<string>(comparer);
    }

    public string Root { get; }

    public StringComparison Comparison { get; }

    /// <summary>Indexed Markdown files.</summary>
    public HashSet<string> Indexed { get; }

    /// <summary>Paths that exist on disk but are not indexed (directories, scripts, ...).</summary>
    public HashSet<string> Existing { get; }

    public HashSet<string> TopLevelDirectories { get; }

    public Dictionary<string, IReadOnlyDictionary<string, string>> MemoryNames { get; } = [];

    /// <summary>Every path the resolver asked the existence probe about.</summary>
    public List<string> Probed { get; } = [];

    public TreeFixture Index(params string[] paths)
    {
        Indexed.UnionWith(paths);
        return this;
    }

    public TreeFixture Exist(params string[] paths)
    {
        Existing.UnionWith(paths);
        return this;
    }

    public TreeFixture TopLevel(params string[] names)
    {
        TopLevelDirectories.UnionWith(names);
        return this;
    }

    public TreeFixture Memory(string directory, params (string Name, string Path)[] names)
    {
        MemoryNames[directory] = names.ToDictionary(entry => entry.Name, entry => entry.Path, StringComparer.Ordinal);
        return this;
    }

    public Link? Resolve(LinkKind kind, string raw, string source, int line = 1) =>
        CreateResolver().Resolve(new RawLink(kind, raw, line), source);

    public LinkResolver CreateResolver() =>
        new(new LinkResolutionContext(
            Root,
            Comparison,
            Indexed,
            MemoryNames,
            TopLevelDirectories,
            path =>
            {
                Probed.Add(path);
                return Existing.Contains(path) || Indexed.Contains(path);
            }));
}
