using Pusula.Indexing;
using Pusula.Links;

namespace Pusula.History;

/// <summary>Compares a source as it is now with a daily summary of an earlier day.</summary>
internal static class ChangeCalculator
{
    /// <summary>
    /// Tells how <paramref name="now"/> differs from <paramref name="before"/>. Files are compared by index key and by their
    /// estimated tokens, so a file whose text changed but whose counts did not is not listed. A broken link is told apart by its file,
    /// its kind and its text, and not by its line.
    /// </summary>
    /// <param name="before">The summary of an earlier day.</param>
    /// <param name="now">The index of the source as it is now.</param>
    public static ChangesSince Compare(DailySummary before, ConfigIndex now)
    {
        var added = new List<ChangedFile>();
        var resized = new List<ResizedFile>();
        foreach ((string path, ConfigFile file) in now.Files)
        {
            TokenCounts tokens = file.Tokens;
            if (!before.Files.TryGetValue(path, out FileTokens known))
            {
                added.Add(new ChangedFile(path, tokens.Total, tokens.EverySession, file.LoadMode));
            }
            else if (known.Total != tokens.Total || known.EverySession != tokens.EverySession)
            {
                resized.Add(new ResizedFile(path, file.LoadMode, known.Total, tokens.Total, known.EverySession, tokens.EverySession));
            }
        }

        var removed = new List<ChangedFile>();
        foreach ((string path, FileTokens known) in before.Files)
        {
            if (!now.Files.ContainsKey(path))
            {
                removed.Add(new ChangedFile(path, known.Total, known.EverySession));
            }
        }

        (List<ChangedLink> newlyBroken, List<ChangedLink> noLongerBroken) = CompareBrokenLinks(before, now);

        return new ChangesSince(
            before.Day,
            before.TakenAt,
            before.Files.Count,
            now.Files.Count,
            Sum(before.Files.Values.Select(file => file.EverySession)),
            Sum(now.Files.Values.Select(file => file.Tokens.EverySession)),
            LargestFirst(added),
            LargestFirst(removed),
            [.. resized
                .OrderByDescending(file => Math.Abs((long)file.EverySessionTokensAfter - file.EverySessionTokensBefore))
                .ThenByDescending(file => Math.Abs((long)file.TokensAfter - file.TokensBefore))
                .ThenBy(file => file.Path, StringComparer.Ordinal)],
            [.. newlyBroken.OrderBy(link => link.Source, StringComparer.Ordinal).ThenBy(link => link.Line)],
            [.. noLongerBroken
                .OrderBy(link => link.Source, StringComparer.Ordinal)
                .ThenBy(link => link.Kind.ToString(), StringComparer.Ordinal)
                .ThenBy(link => link.Raw, StringComparer.Ordinal)]);
    }

    // The largest contributor to every session first, then the largest file, then by path.
    private static List<ChangedFile> LargestFirst(List<ChangedFile> files) =>
    [
        .. files
            .OrderByDescending(file => file.EverySessionTokens)
            .ThenByDescending(file => file.Tokens)
            .ThenBy(file => file.Path, StringComparer.Ordinal),
    ];

    // Broken now and not in the summary (each once, with the line of its first occurrence now), and in the summary and not broken now.
    private static (List<ChangedLink> NewlyBroken, List<ChangedLink> NoLongerBroken) CompareBrokenLinks(DailySummary before, ConfigIndex now)
    {
        var brokenBefore = new HashSet<BrokenLink>(before.Broken);
        var brokenNow = new HashSet<BrokenLink>();
        var newlyBroken = new List<ChangedLink>();
        foreach ((string path, ConfigFile file) in now.Files)
        {
            foreach (Link link in file.Links.Where(link => link.Status == LinkStatus.Broken))
            {
                var key = new BrokenLink(path, link.Kind, link.Raw);
                if (brokenNow.Add(key) && !brokenBefore.Contains(key))
                {
                    newlyBroken.Add(new ChangedLink(path, link.Kind, link.Raw, link.Line));
                }
            }
        }

        var listed = new HashSet<BrokenLink>();
        List<ChangedLink> noLongerBroken =
        [
            .. before.Broken
                .Where(link => !brokenNow.Contains(link) && listed.Add(link))
                .Select(link => new ChangedLink(link.Source, link.Kind, link.Raw)),
        ];

        return (newlyBroken, noLongerBroken);
    }

    // A summary can come from a file that someone edited: the sum does not wrap around.
    private static int Sum(IEnumerable<int> values) => (int)Math.Min(values.Sum(value => (long)value), int.MaxValue);
}
