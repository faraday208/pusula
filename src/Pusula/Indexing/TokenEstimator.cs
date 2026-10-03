using System.Text.Json.Nodes;

namespace Pusula.Indexing;

/// <summary>A rough token estimator: one token per four characters, rounded up. Good for ranking, not for billing.</summary>
internal static class TokenEstimator
{
    /// <summary>Estimated tokens of <paramref name="text"/>.</summary>
    public static int Estimate(string text) => (text.Length + 3) / 4;

    /// <summary>Estimated token counts of one file.</summary>
    /// <param name="path">Root-relative path with <c>/</c> separators.</param>
    /// <param name="content">The whole file text.</param>
    /// <param name="layer">The file's layer.</param>
    /// <param name="loadMode">The file's load mode.</param>
    /// <param name="frontmatter">The file's frontmatter, if any.</param>
    public static TokenCounts Count(string path, string content, Layer layer, LoadMode loadMode, JsonObject? frontmatter)
    {
        int total = Estimate(content);

        int everySession = loadMode switch
        {
            LoadMode.EverySession => total,
            LoadMode.DescriptionEverySession => Estimate($"{DisplayName(path, layer, frontmatter)}: {FrontmatterValues.GetString(frontmatter, "description") ?? string.Empty}"),
            _ => 0,
        };

        return new TokenCounts(total, everySession, loadMode == LoadMode.ProjectSession ? total : 0);
    }

    // The frontmatter name, else the skill's directory name, else the file name without extension.
    private static string DisplayName(string path, Layer layer, JsonObject? frontmatter)
    {
        string? name = FrontmatterValues.GetString(frontmatter, "name");
        if (name is not null)
        {
            return name;
        }

        return layer == Layer.Skill
            ? Path.GetFileName(Path.GetDirectoryName(path)) ?? string.Empty
            : Path.GetFileNameWithoutExtension(path);
    }
}
