using System.Text.Json.Nodes;
using Pusula.Indexing;
using Shouldly;
using Xunit;

namespace Pusula.UnitTests.Indexing;

public sealed class TokenEstimatorTests
{
    [Theory]
    [InlineData(0, 0)]
    [InlineData(1, 1)]
    [InlineData(4, 1)]
    [InlineData(5, 2)]
    [InlineData(8, 2)]
    [InlineData(9, 3)]
    [InlineData(4000, 1000)]
    public void Estimate_TextLength_RoundsUpToFourCharactersPerToken(int length, int expected) =>
        TokenEstimator.Estimate(new string('x', length)).ShouldBe(expected);

    [Fact]
    public void Count_EverySessionFile_CountsWholeFileAsEverySession()
    {
        TokenCounts counts = TokenEstimator.Count("rules/a.md", new string('x', 40), Layer.Rule, LoadMode.EverySession, null);

        counts.ShouldBe(new TokenCounts(Total: 10, EverySession: 10, ProjectSession: 0));
    }

    [Fact]
    public void Count_ProjectSessionFile_CountsWholeFileAsProjectSession()
    {
        TokenCounts counts = TokenEstimator.Count("projects/p/memory/MEMORY.md", new string('x', 80), Layer.MemoryIndex, LoadMode.ProjectSession, null);

        counts.ShouldBe(new TokenCounts(Total: 20, EverySession: 0, ProjectSession: 20));
    }

    [Theory]
    [InlineData("OnDemand")]
    [InlineData("Conditional")]
    [InlineData("UserInvoked")]
    [InlineData("Inactive")]
    public void Count_ModesThatLoadNothingEverySession_CountOnlyTotal(string loadMode)
    {
        TokenCounts counts = TokenEstimator.Count("x/y.md", new string('x', 40), Layer.Other, Enum.Parse<LoadMode>(loadMode), null);

        counts.ShouldBe(new TokenCounts(Total: 10, EverySession: 0, ProjectSession: 0));
    }

    [Fact]
    public void Count_DescriptionMode_CountsNameAndDescriptionOnly()
    {
        JsonObject frontmatter = FrontmatterParser.Parse("---\nname: deploy\ndescription: Ships the build\n---\n").Frontmatter!;
        string content = "---\nname: deploy\ndescription: Ships the build\n---\n" + new string('x', 400);

        TokenCounts counts = TokenEstimator.Count("skills/deploy/SKILL.md", content, Layer.Skill, LoadMode.DescriptionEverySession, frontmatter);

        // "deploy: Ships the build" is 23 characters.
        counts.EverySession.ShouldBe(6);
        counts.Total.ShouldBe(TokenEstimator.Estimate(content));
    }

    [Fact]
    public void Count_SkillWithoutFrontmatterName_UsesDirectoryName()
    {
        // "my-skill: " is 10 characters.
        TokenCounts counts = TokenEstimator.Count("skills/my-skill/SKILL.md", new string('x', 100), Layer.Skill, LoadMode.DescriptionEverySession, null);

        counts.EverySession.ShouldBe(3);
    }

    [Fact]
    public void Count_AgentWithoutFrontmatterName_UsesFileNameWithoutExtension()
    {
        // "reviewer: " is 10 characters.
        TokenCounts counts = TokenEstimator.Count("agents/team/reviewer.md", new string('x', 100), Layer.Agent, LoadMode.DescriptionEverySession, null);

        counts.EverySession.ShouldBe(3);
    }

    [Fact]
    public void Count_DescriptionThatIsNotAString_IsTreatedAsMissing()
    {
        JsonObject frontmatter = FrontmatterParser.Parse("---\nname: ab\ndescription: [x, y]\n---\n").Frontmatter!;

        // "ab: " is 4 characters.
        TokenEstimator.Count("agents/a.md", "text", Layer.Agent, LoadMode.DescriptionEverySession, frontmatter).EverySession.ShouldBe(1);
    }

    [Fact]
    public void Count_SkillAtTheRootOfSkills_UsesTheSkillsDirectoryName()
    {
        // "skills: " is 8 characters.
        TokenEstimator.Count("skills/SKILL.md", "text", Layer.Skill, LoadMode.DescriptionEverySession, null).EverySession.ShouldBe(2);
    }
}
