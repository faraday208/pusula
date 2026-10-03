using Pusula.Indexing;

namespace Pusula.Files;

/// <summary>Builds the <see cref="FileResponse"/> for an indexed file.</summary>
internal static class FileMapper
{
    /// <summary>Maps <paramref name="file"/> and the backlinks that <paramref name="index"/> holds for it.</summary>
    /// <param name="index">The index snapshot that <paramref name="file"/> comes from.</param>
    /// <param name="file">The file.</param>
    public static FileResponse ToResponse(ConfigIndex index, ConfigFile file)
    {
        IReadOnlyList<Backlink> backlinks = index.Backlinks.TryGetValue(file.Path, out IReadOnlyList<Backlink>? incoming) ? incoming : [];

        return new FileResponse(
            file.Path,
            file.Name,
            file.Layer,
            file.LoadMode,
            new FileTokens(file.Tokens.Total, file.Tokens.EverySession, file.Tokens.ProjectSession),
            file.Body,
            file.BodyStartLine,
            [.. file.Links.Select(link => new FileLink(link.Kind, link.Raw, link.Line, link.Status, link.Target, link.Heading))],
            MapBacklinks(index, backlinks),
            file.IsOrphan,
            index.Version,
            file.Frontmatter,
            file.FrontmatterError,
            file.FrontmatterErrorLine,
            ErrorText(file),
            file.Tags.Count > 0 ? file.Tags : null);
    }

    // The line of the frontmatter error, read from the whole content because the line is counted from the top of the file.
    private static string? ErrorText(ConfigFile file) =>
        file.FrontmatterErrorLine is int line ? new LineExcerpts(file.Content).Of(line) : null;

    // Each backlink carries the line of its source that holds the link. The lines of a source are found once, however
    // many links it has to this file: one file can link here from thousands of lines.
    private static List<FileBacklink> MapBacklinks(ConfigIndex index, IReadOnlyList<Backlink> backlinks)
    {
        var sources = new Dictionary<string, LineExcerpts?>(StringComparer.Ordinal);
        var result = new List<FileBacklink>(backlinks.Count);
        foreach (Backlink backlink in backlinks)
        {
            if (!sources.TryGetValue(backlink.Source, out LineExcerpts? lines))
            {
                sources[backlink.Source] = lines = index.Files.TryGetValue(backlink.Source, out ConfigFile? source)
                    ? new LineExcerpts(source.Content)
                    : null;
            }

            result.Add(new FileBacklink(backlink.Source, backlink.Kind, backlink.Line, lines?.Of(backlink.Line)));
        }

        return result;
    }
}
