using Pusula.Indexing;
using Pusula.Links;

namespace Pusula.History;

/// <summary>The estimated tokens of one file, as a daily summary keeps them.</summary>
/// <param name="Total">Tokens of the whole file.</param>
/// <param name="EverySession">Tokens the file adds to every session.</param>
internal readonly record struct FileTokens(int Total, int EverySession);

/// <summary>
/// A link that is broken, as a daily summary keeps it. It is told apart by its file, its kind and its text, and not by its line:
/// lines move when text is added above the link. The file is compared the way the platform compares paths, as the keys of an index are.
/// </summary>
/// <param name="Source">Index key of the file that contains the link.</param>
/// <param name="Kind">The syntax the link was written in.</param>
/// <param name="Raw">The link text as written.</param>
internal readonly record struct BrokenLink(string Source, LinkKind Kind, string Raw)
{
    /// <inheritdoc />
    public bool Equals(BrokenLink other) =>
        Kind == other.Kind
        && string.Equals(Raw, other.Raw, StringComparison.Ordinal)
        && string.Equals(Source, other.Source, PathComparison.Current);

    /// <inheritdoc />
    public override int GetHashCode() =>
        HashCode.Combine(StringComparer.FromComparison(PathComparison.Current).GetHashCode(Source), Kind, Raw);
}

/// <summary>
/// What a source looked like on one day: for every indexed file its estimated tokens in all and in every session, and the links that
/// were broken. It is all that is kept of a day, and all that a comparison needs: the text of the files is not kept.
/// </summary>
/// <param name="Day">The local calendar day the summary is of.</param>
/// <param name="TakenAt">When the summary was taken.</param>
/// <param name="Files">The tokens of every file by index key, compared the way the platform compares paths.</param>
/// <param name="Broken">The links that were broken, each once, in file order and then document order.</param>
internal sealed record DailySummary(
    DateOnly Day,
    DateTimeOffset TakenAt,
    IReadOnlyDictionary<string, FileTokens> Files,
    IReadOnlyList<BrokenLink> Broken)
{
    /// <summary>The comparer of the keys of <see cref="Files"/>: the one that the keys of an index are compared with.</summary>
    public static StringComparer Keys { get; } = StringComparer.FromComparison(PathComparison.Current);

    /// <summary>Takes the summary of an index.</summary>
    /// <param name="index">The index snapshot.</param>
    /// <param name="day">The local calendar day it is taken on.</param>
    /// <param name="takenAt">When it is taken.</param>
    public static DailySummary Of(ConfigIndex index, DateOnly day, DateTimeOffset takenAt)
    {
        var files = new Dictionary<string, FileTokens>(index.Files.Count, Keys);
        var broken = new List<BrokenLink>();
        var seen = new HashSet<BrokenLink>();
        foreach ((string path, ConfigFile file) in index.Files)
        {
            files[path] = new FileTokens(file.Tokens.Total, file.Tokens.EverySession);
            foreach (Link link in file.Links.Where(link => link.Status == LinkStatus.Broken))
            {
                var key = new BrokenLink(path, link.Kind, link.Raw);
                if (seen.Add(key))
                {
                    broken.Add(key);
                }
            }
        }

        return new DailySummary(day, takenAt, files, broken);
    }
}
