using System.Text.Json.Nodes;
using Pusula.Links;

namespace Pusula.Indexing;

/// <summary>Maps a file's location and frontmatter to its <see cref="Layer"/> and <see cref="LoadMode"/>.</summary>
internal static class ClaudeLayout
{
    /// <summary>
    /// Classifies an indexed Markdown file. The first matching rule wins; see the layer table in the project spec.
    /// </summary>
    /// <param name="path">Root-relative path with <c>/</c> separators.</param>
    /// <param name="frontmatter">The file's frontmatter, if any.</param>
    /// <param name="outputStyle">The output style selected in <c>settings.json</c>, if any.</param>
    /// <param name="comparison">How directory and file names are compared.</param>
    public static (Layer Layer, LoadMode LoadMode) Classify(
        string path,
        JsonObject? frontmatter,
        string? outputStyle,
        StringComparison comparison)
    {
        string[] parts = path.Split('/');
        string first = parts[0];
        string fileName = parts[^1];

        if (parts.Length == 1)
        {
            return string.Equals(fileName, "CLAUDE.md", comparison)
                ? (Layer.ClaudeMd, LoadMode.EverySession)
                : (Layer.Reference, LoadMode.OnDemand);
        }

        if (string.Equals(first, "rules", comparison))
        {
            return FrontmatterValues.Has(frontmatter, "paths")
                ? (Layer.PathRule, LoadMode.Conditional)
                : (Layer.Rule, LoadMode.EverySession);
        }

        if (string.Equals(first, "skills", comparison))
        {
            return string.Equals(fileName, "SKILL.md", comparison)
                ? (Layer.Skill, InvocationMode(frontmatter))
                : (Layer.SkillResource, LoadMode.OnDemand);
        }

        if (string.Equals(first, "agents", comparison))
        {
            return (Layer.Agent, LoadMode.DescriptionEverySession);
        }

        if (string.Equals(first, "commands", comparison))
        {
            return (Layer.Command, InvocationMode(frontmatter));
        }

        if (string.Equals(first, "output-styles", comparison))
        {
            return (Layer.OutputStyle, IsSelectedOutputStyle(path, frontmatter, outputStyle) ? LoadMode.EverySession : LoadMode.Inactive);
        }

        if (MemoryDirectory.TryGetFor(path, comparison, out _))
        {
            return parts.Length == 4 && string.Equals(fileName, "MEMORY.md", comparison)
                ? (Layer.MemoryIndex, LoadMode.ProjectSession)
                : (Layer.Memory, LoadMode.OnDemand);
        }

        return string.Equals(first, "shared", comparison)
            ? (Layer.Shared, LoadMode.OnDemand)
            : (Layer.Other, LoadMode.OnDemand);
    }

    // Skills and commands: the description is always loaded, unless the model may not invoke them at all.
    private static LoadMode InvocationMode(JsonObject? frontmatter) =>
        FrontmatterValues.IsTrue(frontmatter, "disable-model-invocation")
            ? LoadMode.UserInvoked
            : LoadMode.DescriptionEverySession;

    // The selected style matches the frontmatter name or the file name (without extension), ignoring case.
    private static bool IsSelectedOutputStyle(string path, JsonObject? frontmatter, string? selected) =>
        !string.IsNullOrWhiteSpace(selected)
        && (string.Equals(selected, FrontmatterValues.GetString(frontmatter, "name"), StringComparison.OrdinalIgnoreCase)
            || string.Equals(selected, Path.GetFileNameWithoutExtension(path), StringComparison.OrdinalIgnoreCase));
}
