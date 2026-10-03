using System.Collections.Immutable;
using System.Text.Json.Nodes;
using Pusula.Indexing;
using Pusula.Links;

namespace Pusula.UnitTests.Support;

/// <summary>Builds <see cref="ConfigIndex"/> values by hand, for code that only reads an index.</summary>
internal static class IndexFactory
{
    public static ConfigFile File(
        string path,
        Layer layer = Layer.Rule,
        LoadMode loadMode = LoadMode.EverySession,
        TokenCounts? tokens = null,
        IReadOnlyList<Link>? links = null,
        bool isOrphan = false,
        string? frontmatterError = null,
        int? frontmatterErrorLine = null,
        JsonObject? frontmatter = null,
        string body = "body",
        int bodyStartLine = 1,
        string? content = null,
        string[]? tags = null,
        DateTimeOffset? modifiedAt = null) =>
        new(
            path,
            path[(path.LastIndexOf('/') + 1)..],
            layer,
            loadMode,
            tokens ?? new TokenCounts(10, 0, 0),
            frontmatter,
            frontmatterError,
            frontmatterErrorLine,
            content ?? body,
            body,
            bodyStartLine,
            links ?? [],
            isOrphan,
            modifiedAt ?? default)
        {
            Tags = tags ?? [],
        };

    public static Link Link(LinkKind kind, string raw, int line, LinkStatus status, string? target = null, string? heading = null) =>
        new(kind, raw, line, target, heading, status);

    /// <summary>An index of the given files; backlinks are derived from their resolved links, as the real builder does.</summary>
    public static ConfigIndex Index(ConfigFile[] files, string root = "/root", long version = 1, string? outputStyle = null, SourceProfile profile = SourceProfile.Claude)
    {
        var backlinks = new Dictionary<string, List<Backlink>>(StringComparer.Ordinal);
        foreach (ConfigFile file in files.OrderBy(file => file.Path, StringComparer.Ordinal))
        {
            foreach (Link link in file.Links.Where(link => link.Status == LinkStatus.Resolved && link.Target != file.Path))
            {
                if (!backlinks.TryGetValue(link.Target!, out List<Backlink>? incoming))
                {
                    backlinks[link.Target!] = incoming = [];
                }

                incoming.Add(new Backlink(file.Path, link.Kind, link.Line));
            }
        }

        return new ConfigIndex(
            root,
            DateTimeOffset.UnixEpoch,
            outputStyle,
            files.ToImmutableSortedDictionary(file => file.Path, file => file, StringComparer.Ordinal),
            backlinks.ToImmutableSortedDictionary(entry => entry.Key, entry => (IReadOnlyList<Backlink>)entry.Value.ToArray(), StringComparer.Ordinal),
            version,
            profile);
    }
}
