using System.Text.Json;
using Pusula.Indexing;
using Pusula.Links;
using Pusula.Overview;
using Pusula.Startup;
using Pusula.Tree;
using Pusula.UnitTests.Support;
using Shouldly;
using Xunit;

namespace Pusula.UnitTests.Tree;

public sealed class TreeMapperTests
{
    private static TreeResponse Map(params ConfigFile[] files) => TreeMapper.ToResponse(IndexFactory.Index(files));

    private static string[] Names(IEnumerable<TreeNode> nodes) => [.. nodes.Select(node => node.Name)];

    private static TreeDirectory DirectoryNode(IEnumerable<TreeNode> nodes, string name) =>
        nodes.OfType<TreeDirectory>().Single(node => node.Name == name);

    private static IEnumerable<TreeFile> Files(IEnumerable<TreeNode> nodes) =>
        nodes.SelectMany(node => node is TreeDirectory directory ? Files(directory.Children) : [(TreeFile)node]);

    [Fact]
    public void ToResponse_EmptyIndex_HasNoNodes()
    {
        TreeResponse response = Map();

        response.Nodes.ShouldBeEmpty();
        response.Root.ShouldBe("/root");
        response.Version.ShouldBe(1);
    }

    [Fact]
    public void ToResponse_CarriesRootVersionAndBuildTime()
    {
        ConfigIndex index = IndexFactory.Index([IndexFactory.File("a.md")], root: "/home/u/.claude", version: 7);

        TreeResponse response = TreeMapper.ToResponse(index);

        response.Root.ShouldBe("/home/u/.claude");
        response.Version.ShouldBe(7);
        response.BuiltAt.ShouldBe(index.BuiltAt);
    }

    [Fact]
    public void ToResponse_DirectoriesComeBeforeFiles_EachGroupOrderedIgnoringCase()
    {
        TreeResponse response = Map(
            IndexFactory.File("b.md"),
            IndexFactory.File("A.md"),
            IndexFactory.File("Zed/x.md"),
            IndexFactory.File("alpha/x.md"),
            IndexFactory.File("Beta/x.md"),
            IndexFactory.File("c.md"));

        Names(response.Nodes).ShouldBe(["alpha", "Beta", "Zed", "A.md", "b.md", "c.md"]);
        response.Nodes.Take(3).ShouldAllBe(node => node is TreeDirectory);
        response.Nodes.Skip(3).ShouldAllBe(node => node is TreeFile);
    }

    [Fact]
    public void ToResponse_NamesThatDifferOnlyInCase_AreOrderedOrdinallyAsTheTieBreaker()
    {
        Assert.SkipWhen(OperatingSystem.IsWindows(), "Windows paths are not case sensitive: one directory, not two.");
        TreeResponse response = Map(
            IndexFactory.File("rules/a.md"),
            IndexFactory.File("Rules/b.md"),
            IndexFactory.File("x.md"),
            IndexFactory.File("X.md"));

        Names(response.Nodes).ShouldBe(["Rules", "rules", "X.md", "x.md"]);
    }

    [Fact]
    public void ToResponse_NestedDirectories_CarryTheirFullPathAndAggregates()
    {
        TreeResponse response = Map(
            IndexFactory.File("CLAUDE.md", tokens: new TokenCounts(100, 100, 0)),
            IndexFactory.File("projects/p/memory/MEMORY.md", tokens: new TokenCounts(30, 0, 30)),
            IndexFactory.File("projects/p/memory/a.md", tokens: new TokenCounts(20, 0, 0)),
            IndexFactory.File("projects/q/memory/MEMORY.md", tokens: new TokenCounts(5, 0, 5)),
            IndexFactory.File("rules/r.md", tokens: new TokenCounts(7, 7, 0)));

        TreeDirectory projects = response.Nodes.OfType<TreeDirectory>().Single(node => node.Name == "projects");
        TreeDirectory p = projects.Children.OfType<TreeDirectory>().Single(node => node.Name == "p");
        TreeDirectory memory = p.Children.OfType<TreeDirectory>().Single();

        projects.Path.ShouldBe("projects");
        projects.FileCount.ShouldBe(3);
        projects.Tokens.ShouldBe(55);
        p.Path.ShouldBe("projects/p");
        p.FileCount.ShouldBe(2);
        p.Tokens.ShouldBe(50);
        memory.Path.ShouldBe("projects/p/memory");
        memory.FileCount.ShouldBe(2);
        Names(memory.Children).ShouldBe(["a.md", "MEMORY.md"]);
        Names(projects.Children).ShouldBe(["p", "q"]);
        response.Nodes.OfType<TreeFile>().Single().Tokens.ShouldBe(100);
    }

    [Fact]
    public void ToResponse_FileNode_CarriesLayerLoadModeBrokenLinksAndOrphanFlag()
    {
        TreeFile file = Map(
            IndexFactory.File(
                "shared/x.md",
                Layer.Shared,
                LoadMode.OnDemand,
                new TokenCounts(42, 0, 0),
                [
                    IndexFactory.Link(LinkKind.MarkdownLink, "a.md", 1, LinkStatus.Broken, "shared/a.md"),
                    IndexFactory.Link(LinkKind.WikiLink, "b", 2, LinkStatus.Broken),
                    IndexFactory.Link(LinkKind.WikiLink, "c", 3, LinkStatus.Pending),
                    IndexFactory.Link(LinkKind.RelativePath, "d/", 4, LinkStatus.NonMarkdown, "d"),
                    IndexFactory.Link(LinkKind.MarkdownLink, "e", 5, LinkStatus.External),
                ],
                isOrphan: true))
            .Nodes.OfType<TreeDirectory>().Single().Children.OfType<TreeFile>().Single();

        file.Name.ShouldBe("x.md");
        file.Path.ShouldBe("shared/x.md");
        file.Tokens.ShouldBe(42);
        file.Layer.ShouldBe(Layer.Shared);
        file.LoadMode.ShouldBe(LoadMode.OnDemand);
        file.BrokenLinks.ShouldBe(2);
        file.Orphan.ShouldBeTrue();
    }

    [Fact]
    public void ToResponse_Serialized_UsesTheTypeAsDiscriminatorAndOnlyTheMembersThatApply()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        ApiJson.Configure(options);
        TreeResponse response = Map(IndexFactory.File("CLAUDE.md", Layer.ClaudeMd), IndexFactory.File("rules/r.md"));

        using JsonDocument json = JsonDocument.Parse(JsonSerializer.Serialize(response, options));
        JsonElement directory = json.RootElement.GetProperty("nodes")[0];
        JsonElement file = json.RootElement.GetProperty("nodes")[1];

        directory.EnumerateObject().Select(property => property.Name).ShouldBe(["type", "name", "path", "tokens", "everySessionTokens", "fileCount", "children"], ignoreOrder: true);
        directory.GetProperty("type").GetString().ShouldBe("Directory");
        file.EnumerateObject().Select(property => property.Name).ShouldBe(["type", "name", "path", "tokens", "everySessionTokens", "layer", "loadMode", "brokenLinks", "orphan"], ignoreOrder: true);
        file.GetProperty("type").GetString().ShouldBe("File");
        file.GetProperty("layer").GetString().ShouldBe("ClaudeMd");
        file.GetProperty("loadMode").GetString().ShouldBe("EverySession");
    }

    [Fact]
    public void ToResponse_FileNode_EverySessionTokensAreTheEverySessionCountOfTheFile()
    {
        TreeResponse response = Map(
            IndexFactory.File("CLAUDE.md", Layer.ClaudeMd, LoadMode.EverySession, new TokenCounts(100, 100, 0)),
            IndexFactory.File("skills/s/SKILL.md", Layer.Skill, LoadMode.DescriptionEverySession, new TokenCounts(500, 12, 0)),
            IndexFactory.File("projects/p/memory/MEMORY.md", Layer.MemoryIndex, LoadMode.ProjectSession, new TokenCounts(30, 0, 30)),
            IndexFactory.File("reference.md", Layer.Reference, LoadMode.OnDemand, new TokenCounts(40, 0, 0)));

        Dictionary<string, TreeFile> files = Files(response.Nodes).ToDictionary(file => file.Path);

        files["CLAUDE.md"].Tokens.ShouldBe(100);
        files["CLAUDE.md"].EverySessionTokens.ShouldBe(100);
        files["skills/s/SKILL.md"].Tokens.ShouldBe(500);
        files["skills/s/SKILL.md"].EverySessionTokens.ShouldBe(12);
        files["projects/p/memory/MEMORY.md"].Tokens.ShouldBe(30);
        files["projects/p/memory/MEMORY.md"].EverySessionTokens.ShouldBe(0);
        files["reference.md"].Tokens.ShouldBe(40);
        files["reference.md"].EverySessionTokens.ShouldBe(0);
    }

    [Fact]
    public void ToResponse_Directories_SumTheEverySessionTokensOfEverythingBelowThemAtAnyDepth()
    {
        TreeResponse response = Map(
            IndexFactory.File("rules/a.md", tokens: new TokenCounts(10, 10, 0)),
            IndexFactory.File("rules/b.md", Layer.PathRule, LoadMode.Conditional, new TokenCounts(20, 0, 0)),
            IndexFactory.File("skills/x/SKILL.md", Layer.Skill, LoadMode.DescriptionEverySession, new TokenCounts(300, 7, 0)),
            IndexFactory.File("skills/x/notes.md", Layer.SkillResource, LoadMode.OnDemand, new TokenCounts(50, 0, 0)),
            IndexFactory.File("skills/y/SKILL.md", Layer.Skill, LoadMode.DescriptionEverySession, new TokenCounts(400, 9, 0)),
            IndexFactory.File("shared/s.md", Layer.Shared, LoadMode.OnDemand, new TokenCounts(5, 0, 0)));

        TreeDirectory rules = DirectoryNode(response.Nodes, "rules");
        TreeDirectory skills = DirectoryNode(response.Nodes, "skills");
        TreeDirectory x = DirectoryNode(skills.Children, "x");
        TreeDirectory y = DirectoryNode(skills.Children, "y");
        TreeDirectory shared = DirectoryNode(response.Nodes, "shared");

        (rules.Tokens, rules.EverySessionTokens).ShouldBe((30, 10));
        (x.Tokens, x.EverySessionTokens).ShouldBe((350, 7));
        (y.Tokens, y.EverySessionTokens).ShouldBe((400, 9));
        (skills.Tokens, skills.EverySessionTokens).ShouldBe((750, 16));
        (shared.Tokens, shared.EverySessionTokens).ShouldBe((5, 0));
        response.Nodes.Sum(node => node.EverySessionTokens).ShouldBe(26);
    }

    [Fact]
    public void ToResponse_EverySessionTokensOfTheTopLevel_AddUpToTheEverySessionTotalOfTheOverview()
    {
        ConfigIndex index = IndexFactory.Index(
        [
            IndexFactory.File("CLAUDE.md", Layer.ClaudeMd, LoadMode.EverySession, new TokenCounts(100, 100, 0)),
            IndexFactory.File("rules/r.md", tokens: new TokenCounts(7, 7, 0)),
            IndexFactory.File("agents/a.md", Layer.Agent, LoadMode.DescriptionEverySession, new TokenCounts(60, 5, 0)),
            IndexFactory.File("projects/p/memory/MEMORY.md", Layer.MemoryIndex, LoadMode.ProjectSession, new TokenCounts(30, 0, 30)),
            IndexFactory.File("reference.md", Layer.Reference, LoadMode.OnDemand, new TokenCounts(40, 0, 0)),
        ]);

        int fromTree = TreeMapper.ToResponse(index).Nodes.Sum(node => node.EverySessionTokens);

        fromTree.ShouldBe(112);
        fromTree.ShouldBe(OverviewMapper.ToResponse(index).EverySessionTokens);
    }

    [Fact]
    public void ToResponse_Serialized_WritesEverySessionTokensEvenWhenTheyAreZero()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        ApiJson.Configure(options);
        TreeResponse response = Map(
            IndexFactory.File("skills/s/SKILL.md", Layer.Skill, LoadMode.DescriptionEverySession, new TokenCounts(500, 12, 0)),
            IndexFactory.File("shared/s.md", Layer.Shared, LoadMode.OnDemand, new TokenCounts(5, 0, 0)));

        using JsonDocument json = JsonDocument.Parse(JsonSerializer.Serialize(response, options));
        JsonElement shared = json.RootElement.GetProperty("nodes")[0];
        JsonElement skills = json.RootElement.GetProperty("nodes")[1];

        shared.GetProperty("everySessionTokens").GetInt32().ShouldBe(0);
        shared.GetProperty("children")[0].GetProperty("everySessionTokens").GetInt32().ShouldBe(0);
        skills.GetProperty("tokens").GetInt32().ShouldBe(500);
        skills.GetProperty("everySessionTokens").GetInt32().ShouldBe(12);
        skills.GetProperty("children")[0].GetProperty("children")[0].GetProperty("everySessionTokens").GetInt32().ShouldBe(12);
    }

    [Fact]
    public void ToResponse_FileNode_CarriesTheTagsOfANoteAndNoneWhenThereAreNoTags()
    {
        TreeResponse response = Map(
            IndexFactory.File("Tagged.md", Layer.Note, tags: ["moc", "todo"]),
            IndexFactory.File("Plain.md", Layer.Note));

        Dictionary<string, TreeFile> files = Files(response.Nodes).ToDictionary(file => file.Name);
        files["Tagged.md"].Tags.ShouldBe(["moc", "todo"]);
        files["Plain.md"].Tags.ShouldBeNull();
    }
}
