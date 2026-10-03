using System.Text.Json.Nodes;
using Pusula.Indexing;
using Shouldly;
using Xunit;

namespace Pusula.UnitTests.Indexing;

public sealed class ClaudeLayoutTests
{
    [Theory]
    [InlineData("CLAUDE.md", null, null, "ClaudeMd", "EverySession")]
    [InlineData("rules/a.md", null, null, "Rule", "EverySession")]
    [InlineData("rules/nested/deep/a.md", "description: x", null, "Rule", "EverySession")]
    [InlineData("rules/a.md", "paths:\n  - src/**", null, "PathRule", "Conditional")]
    [InlineData("rules/nested/a.md", "paths: \"**/*.cs\"", null, "PathRule", "Conditional")]
    [InlineData("rules/CLAUDE.md", null, null, "Rule", "EverySession")]
    [InlineData("skills/foo/SKILL.md", "name: foo", null, "Skill", "DescriptionEverySession")]
    [InlineData("skills/foo/SKILL.md", null, null, "Skill", "DescriptionEverySession")]
    [InlineData("skills/foo/SKILL.md", "disable-model-invocation: true", null, "Skill", "UserInvoked")]
    [InlineData("skills/foo/SKILL.md", "disable-model-invocation: TRUE", null, "Skill", "UserInvoked")]
    [InlineData("skills/foo/SKILL.md", "disable-model-invocation: false", null, "Skill", "DescriptionEverySession")]
    [InlineData("skills/group/foo/SKILL.md", null, null, "Skill", "DescriptionEverySession")]
    [InlineData("skills/SKILL.md", null, null, "Skill", "DescriptionEverySession")]
    [InlineData("skills/foo/reference.md", null, null, "SkillResource", "OnDemand")]
    [InlineData("skills/foo/docs/SKILL.txt.md", null, null, "SkillResource", "OnDemand")]
    [InlineData("agents/reviewer.md", "name: reviewer", null, "Agent", "DescriptionEverySession")]
    [InlineData("agents/team/reviewer.md", null, null, "Agent", "DescriptionEverySession")]
    [InlineData("commands/deploy.md", null, null, "Command", "DescriptionEverySession")]
    [InlineData("commands/sub/deploy.md", "disable-model-invocation: true", null, "Command", "UserInvoked")]
    [InlineData("output-styles/learning.md", null, "learning", "OutputStyle", "EverySession")]
    [InlineData("output-styles/learning.md", null, "LEARNING", "OutputStyle", "EverySession")]
    [InlineData("output-styles/file-name.md", "name: Friendly Name", "friendly name", "OutputStyle", "EverySession")]
    [InlineData("output-styles/learning.md", null, "other", "OutputStyle", "Inactive")]
    [InlineData("output-styles/learning.md", null, null, "OutputStyle", "Inactive")]
    [InlineData("output-styles/learning.md", null, "", "OutputStyle", "Inactive")]
    [InlineData("output-styles/learning.md", null, "  ", "OutputStyle", "Inactive")]
    [InlineData("projects/-home-ai/memory/MEMORY.md", null, null, "MemoryIndex", "ProjectSession")]
    [InlineData("projects/-home-ai/memory/user-role.md", "name: user-role", null, "Memory", "OnDemand")]
    [InlineData("projects/-home-ai/memory/sub/note.md", null, null, "Memory", "OnDemand")]
    [InlineData("projects/-home-ai/memory/sub/MEMORY.md", null, null, "Memory", "OnDemand")]
    [InlineData("projects/-home-ai/memory/memory.md", null, null, "Memory", "OnDemand")]
    [InlineData("README.md", null, null, "Reference", "OnDemand")]
    [InlineData("TOOLS.md", "paths: x", null, "Reference", "OnDemand")]
    [InlineData("shared/dotnet-rules.md", null, null, "Shared", "OnDemand")]
    [InlineData("shared/nested/x.md", null, null, "Shared", "OnDemand")]
    [InlineData("hooks/README.md", null, null, "Other", "OnDemand")]
    [InlineData("references/guide.md", null, null, "Other", "OnDemand")]
    [InlineData("projects/-home-ai/notes.md", null, null, "Other", "OnDemand")]
    [InlineData("projects/-home-ai/memory", null, null, "Other", "OnDemand")]
    [InlineData("projects/MEMORY.md", null, null, "Other", "OnDemand")]
    public void Classify_PathTable_ReturnsLayerAndLoadMode(string path, string? frontmatterYaml, string? outputStyle, string layer, string loadMode)
    {
        JsonObject? frontmatter = frontmatterYaml is null
            ? null
            : FrontmatterParser.Parse($"---\n{frontmatterYaml}\n---\n").Frontmatter;

        (Layer actualLayer, LoadMode actualLoadMode) = ClaudeLayout.Classify(path, frontmatter, outputStyle, StringComparison.Ordinal);

        actualLayer.ToString().ShouldBe(layer);
        actualLoadMode.ToString().ShouldBe(loadMode);
    }

    [Theory]
    [InlineData("claude.md", "Reference")]
    [InlineData("Rules/a.md", "Other")]
    [InlineData("Skills/foo/skill.md", "Other")]
    public void Classify_DifferentCaseOnCaseSensitivePlatform_DoesNotMatch(string path, string layer) =>
        ClaudeLayout.Classify(path, null, null, StringComparison.Ordinal).Layer.ToString().ShouldBe(layer);

    [Theory]
    [InlineData("claude.md", "ClaudeMd")]
    [InlineData("Rules/a.md", "Rule")]
    [InlineData("SKILLS/foo/skill.MD", "Skill")]
    [InlineData("Projects/p/Memory/memory.md", "MemoryIndex")]
    [InlineData("Output-Styles/x.md", "OutputStyle")]
    public void Classify_DifferentCaseOnCaseInsensitivePlatform_Matches(string path, string layer) =>
        ClaudeLayout.Classify(path, null, null, StringComparison.OrdinalIgnoreCase).Layer.ToString().ShouldBe(layer);

    [Fact]
    public void Classify_DisableModelInvocationThatIsNotAString_IsIgnored()
    {
        JsonObject frontmatter = FrontmatterParser.Parse("---\ndisable-model-invocation: [true]\n---\n").Frontmatter!;

        ClaudeLayout.Classify("skills/a/SKILL.md", frontmatter, null, StringComparison.Ordinal).LoadMode
            .ShouldBe(LoadMode.DescriptionEverySession);
    }
}
