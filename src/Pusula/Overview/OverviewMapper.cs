using Pusula.Indexing;
using Pusula.Links;

namespace Pusula.Overview;

/// <summary>Builds the <see cref="OverviewResponse"/> for an index.</summary>
internal static class OverviewMapper
{
    private const int HeaviestCount = 10;
    private const int NoteListCount = 8;

    // The names, in order of preference, of the note that a vault is entered by (without ".md", ignoring case).
    private static readonly string[] EntryNames = ["home", "index", "readme", "moc", "start"];

    // The tags of a note that is the entry note when no note has one of those names.
    private static readonly string[] EntryTags = ["moc", "home"];

    /// <summary>Summarizes <paramref name="index"/>.</summary>
    /// <param name="index">The index snapshot.</param>
    public static OverviewResponse ToResponse(ConfigIndex index)
    {
        ConfigFile[] files = [.. index.Files.Values];
        bool notes = index.Profile != SourceProfile.Claude;

        return new OverviewResponse(
            index.Root,
            index.Version,
            index.BuiltAt,
            files.Length,
            files.Sum(file => file.Tokens.EverySession),
            [.. files
                .GroupBy(file => file.Layer)
                .OrderBy(group => group.Key)
                .Select(group => new OverviewLayer(
                    group.Key,
                    group.Count(),
                    group.Sum(file => file.Tokens.Total),
                    group.Sum(file => file.Tokens.EverySession)))],
            [.. files
                .Where(file => file.Tokens.EverySession > 0)
                .OrderByDescending(file => file.Tokens.EverySession)
                .ThenBy(file => file.Path, StringComparer.Ordinal)
                .Take(HeaviestCount)
                .Select(file => new OverviewHeaviestFile(file.Path, file.Layer, file.LoadMode, file.Tokens.EverySession))],
            [.. files
                .Where(file => file.Layer == Layer.MemoryIndex)
                .OrderByDescending(file => file.Tokens.Total)
                .ThenBy(file => file.Path, StringComparer.Ordinal)
                .Select(file => new OverviewProjectMemory(file.Path, ProjectOf(file.Path), file.Tokens.Total))],
            [.. LinksWith(files, LinkStatus.Broken).Select(item => new OverviewBrokenLink(item.Source, item.Link.Line, item.Link.Kind, item.Link.Raw, item.Link.Target))],
            [.. LinksWith(files, LinkStatus.Pending).Select(item => new OverviewPendingLink(item.Source, item.Link.Line, item.Link.Kind, item.Link.Raw))],
            [.. files.Where(file => file.IsOrphan).Select(file => new OverviewOrphan(file.Path, file.Layer))],
            [.. files.Where(file => file.FrontmatterError is not null).Select(file => new OverviewFrontmatterError(file.Path, file.FrontmatterError!, file.FrontmatterErrorLine))],
            index.Profile,
            TagsOf(files),
            notes ? RecentOf(files) : [],
            notes ? MostLinkedOf(index) : [],
            index.OutputStyle,
            notes ? EntryOf(files) : null);
    }

    // Every tag once, under its first spelling (the files are in path order), with the number of notes that carry it.
    // Most used first; the same count by name.
    private static List<OverviewTag> TagsOf(ConfigFile[] files)
    {
        var counts = new Dictionary<string, (string Name, int Count)>(StringComparer.OrdinalIgnoreCase);
        foreach (ConfigFile file in files)
        {
            foreach (string tag in file.Tags)
            {
                counts[tag] = counts.TryGetValue(tag, out (string Name, int Count) known) ? (known.Name, known.Count + 1) : (tag, 1);
            }
        }

        return
        [
            .. counts.Values
                .OrderByDescending(entry => entry.Count)
                .ThenBy(entry => entry.Name, StringComparer.OrdinalIgnoreCase)
                .ThenBy(entry => entry.Name, StringComparer.Ordinal)
                .Select(entry => new OverviewTag(entry.Name, entry.Count)),
        ];
    }

    // The notes written last, newest first; the same time by path.
    private static List<OverviewRecentNote> RecentOf(ConfigFile[] files) =>
    [
        .. files
            .OrderByDescending(file => file.ModifiedAt)
            .ThenBy(file => file.Path, StringComparer.Ordinal)
            .Take(NoteListCount)
            .Select(file => new OverviewRecentNote(file.Path, file.ModifiedAt)),
    ];

    // The notes with the most backlinks (one for every link, as the file endpoint lists them), the same number by path.
    // Only notes that something links to have an entry in the backlinks.
    private static List<OverviewMostLinkedNote> MostLinkedOf(ConfigIndex index) =>
    [
        .. index.Backlinks
            .OrderByDescending(entry => entry.Value.Count)
            .ThenBy(entry => entry.Key, StringComparer.Ordinal)
            .Take(NoteListCount)
            .Select(entry => new OverviewMostLinkedNote(entry.Key, entry.Value.Count)),
    ];

    // Where to start reading a vault. First the note without a folder whose name is the first of EntryNames that any such
    // note has; the files are in path order, so of two names that differ in case the first one in path order wins. Failing
    // that, of the notes tagged moc or home the one that links to the most other notes, the same number by path.
    private static OverviewEntryNote? EntryOf(ConfigFile[] files)
    {
        ConfigFile[] atTheRoot = [.. files.Where(file => !file.Path.Contains('/', StringComparison.Ordinal))];
        foreach (string name in EntryNames)
        {
            ConfigFile? named = Array.Find(atTheRoot, file => string.Equals(TitleOf(file), name, StringComparison.OrdinalIgnoreCase));
            if (named is not null)
            {
                return new OverviewEntryNote(named.Path, TitleOf(named));
            }
        }

        ConfigFile? hub = files
            .Where(file => file.Tags.Any(tag => EntryTags.Contains(tag, StringComparer.OrdinalIgnoreCase)))
            .OrderByDescending(LinksToOtherNotes)
            .ThenBy(file => file.Path, StringComparer.Ordinal)
            .FirstOrDefault();
        return hub is null ? null : new OverviewEntryNote(hub.Path, TitleOf(hub));
    }

    // The name of the note: the file name without ".md" (every indexed file has that extension, in any case).
    private static string TitleOf(ConfigFile file) => Path.GetFileNameWithoutExtension(file.Name);

    // The links of a note that lead to another note: the ones that become backlinks of that one.
    private static int LinksToOtherNotes(ConfigFile file) =>
        file.Links.Count(link => link.Status == LinkStatus.Resolved && !string.Equals(link.Target, file.Path, PathComparison.Current));

    // projects/<project>/memory/MEMORY.md -> <project>
    private static string ProjectOf(string memoryIndexPath) => memoryIndexPath.Split('/')[1];

    // In file order, then document order.
    private static IEnumerable<(string Source, Link Link)> LinksWith(ConfigFile[] files, LinkStatus status) =>
        files.SelectMany(file => file.Links.Where(link => link.Status == status).Select(link => (file.Path, link)));
}
