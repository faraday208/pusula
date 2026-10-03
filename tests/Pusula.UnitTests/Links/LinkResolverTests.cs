using Pusula.Links;
using Pusula.UnitTests.Support;
using Shouldly;
using Xunit;

namespace Pusula.UnitTests.Links;

public sealed class LinkResolverTests
{
    private static TreeFixture Tree() => new TreeFixture()
        .Index("CLAUDE.md", "rules/a.md", "rules/b.md", "rules/my note.md", "shared/x.md", "hooks/notes.md");

    private static Link Expect(Link? link)
    {
        link.ShouldNotBeNull();
        return link;
    }

    private static Link ResolveMarkdown(TreeFixture tree, string raw, string source = "rules/a.md") =>
        Expect(tree.Resolve(LinkKind.MarkdownLink, raw, source, line: 7));

    private static Link Linked(string raw, string target, string? heading = null) =>
        new(LinkKind.MarkdownLink, raw, 7, target, heading, LinkStatus.Resolved);

    // ---- Markdown links -------------------------------------------------------------------------------------

    [Fact]
    public void Resolve_MarkdownLinkToSiblingFile_IsResolved() =>
        ResolveMarkdown(Tree(), "b.md").ShouldBe(Linked("b.md", "rules/b.md"));

    [Fact]
    public void Resolve_MarkdownLinkToParentRelativeFile_IsResolved() =>
        ResolveMarkdown(Tree(), "../shared/x.md").ShouldBe(Linked("../shared/x.md", "shared/x.md"));

    [Fact]
    public void Resolve_MarkdownLinkFromTheRoot_IsResolved() =>
        ResolveMarkdown(Tree(), "rules/b.md", source: "CLAUDE.md").ShouldBe(Linked("rules/b.md", "rules/b.md"));

    [Fact]
    public void Resolve_MarkdownLinkInAngleBrackets_StripsThem() =>
        ResolveMarkdown(Tree(), "<b.md>").ShouldBe(Linked("<b.md>", "rules/b.md"));

    [Fact]
    public void Resolve_MarkdownLinkWithHeading_SplitsAndDecodesTheHeading() =>
        ResolveMarkdown(Tree(), "b.md#Some%20Heading").ShouldBe(Linked("b.md#Some%20Heading", "rules/b.md", "Some Heading"));

    [Fact]
    public void Resolve_MarkdownLinkWithEmptyHeading_HasNoHeading() =>
        ResolveMarkdown(Tree(), "b.md#").ShouldBe(Linked("b.md#", "rules/b.md"));

    [Fact]
    public void Resolve_MarkdownLinkWithEncodedSpace_DecodesThePath() =>
        ResolveMarkdown(Tree(), "my%20note.md").ShouldBe(Linked("my%20note.md", "rules/my note.md"));

    [Fact]
    public void Resolve_EncodedHashInThePath_IsNotAHeadingSeparator()
    {
        TreeFixture tree = Tree().Index("rules/c#d.md");

        ResolveMarkdown(tree, "c%23d.md").ShouldBe(Linked("c%23d.md", "rules/c#d.md"));
    }

    [Fact]
    public void Resolve_MalformedEscape_IsKeptRaw()
    {
        Link link = ResolveMarkdown(Tree(), "%zz.md");

        link.Status.ShouldBe(LinkStatus.Broken);
        link.Target.ShouldBe("rules/%zz.md");
    }

    [Fact]
    public void Resolve_MissingMarkdownTarget_IsBrokenWithTheNormalizedTarget()
    {
        Link link = ResolveMarkdown(Tree(), "missing.md");

        link.Status.ShouldBe(LinkStatus.Broken);
        link.Target.ShouldBe("rules/missing.md");
    }

    [Fact]
    public void Resolve_EncodedDotDotThatStaysInsideTheRoot_IsDecodedBeforeNormalizing() =>
        ResolveMarkdown(Tree(), "%2e%2e/shared/x.md").Target.ShouldBe("shared/x.md");

    [Fact]
    public void Resolve_DoubleEncodedDotDot_IsALiteralDirectoryName()
    {
        Link link = ResolveMarkdown(Tree(), "%252e%252e/x.md");

        link.Status.ShouldBe(LinkStatus.Broken);
        link.Target.ShouldBe("rules/%2e%2e/x.md");
    }

    [Theory]
    [InlineData("../../secret.md")]
    [InlineData("../../../etc/passwd")]
    [InlineData("%2e%2e/%2e%2e/secret.md")]
    [InlineData("%2E%2E%2F%2E%2E%2Fsecret.md")]
    [InlineData("%5C..%5Cx.md")]
    [InlineData("a%5Cb.md")]
    [InlineData("a%00b.md")]
    [InlineData("a\\b.md")]
    [InlineData("~/other/x.md")]
    [InlineData("~/x")]
    [InlineData("~/.claude/../x.md")]
    [InlineData("/etc/passwd")]
    [InlineData("/home/test/.claude/../secret.md")]
    [InlineData("/../etc/passwd")]
    [InlineData("/home/../../etc/passwd")]
    [InlineData("file:///../etc/passwd")]
    [InlineData("/home/test/.claude-other/x.md")]
    [InlineData("/home/test/.claud/x.md")]
    [InlineData("/HOME/test/.claude/rules/a.md")]
    [InlineData("file:///etc/passwd")]
    [InlineData("file://../../x.md")]
    [InlineData("file:///home/test/.claude/../x.md")]
    public void Resolve_MarkdownLinkLeavingTheRoot_IsExternalAndNeverProbed(string raw)
    {
        TreeFixture tree = Tree();

        Link link = ResolveMarkdown(tree, raw);

        link.Status.ShouldBe(LinkStatus.External);
        link.Target.ShouldBeNull();
        link.Heading.ShouldBeNull();
        tree.Probed.ShouldBeEmpty();
    }

    [Theory]
    [InlineData("file:///home/test/.claude/shared/x.md")]
    [InlineData("FILE:///home/test/.claude/shared/x.md")]
    [InlineData("/home/test/.claude/shared/x.md")]
    [InlineData("/home/test/.claude/rules/../shared/x.md")]
    [InlineData("/home/test//.claude/./shared/x.md")]
    [InlineData("~/.claude/shared/x.md")]
    [InlineData("~/.claude/rules/../shared/x.md")]
    public void Resolve_MarkdownLinkInsideTheRootByAbsoluteOrHomePath_IsResolvedRelativeToTheRoot(string raw) =>
        ResolveMarkdown(Tree(), raw).ShouldBe(Linked(raw, "shared/x.md"));

    [Theory]
    [InlineData("file:b.md")]
    [InlineData("file://b.md")]
    public void Resolve_FileSchemeWithoutAnAbsolutePath_IsRelativeToTheSource(string raw) =>
        ResolveMarkdown(Tree(), raw).ShouldBe(Linked(raw, "rules/b.md"));

    [Theory]
    [InlineData("/home/test/.claude")]
    [InlineData("/home/test/.claude/")]
    [InlineData("~/.claude/")]
    [InlineData("..")]
    [InlineData("../")]
    public void Resolve_LinkToTheRootItself_IsNonMarkdownWithoutATarget(string raw)
    {
        Link link = ResolveMarkdown(Tree(), raw);

        link.Status.ShouldBe(LinkStatus.NonMarkdown);
        link.Target.ShouldBeNull();
    }

    [Fact]
    public void Resolve_LinkToAnExistingDirectory_IsNonMarkdown()
    {
        TreeFixture tree = Tree().Exist("rules/sub");

        ResolveMarkdown(tree, "sub").ShouldBe(new Link(LinkKind.MarkdownLink, "sub", 7, "rules/sub", null, LinkStatus.NonMarkdown));
    }

    [Fact]
    public void Resolve_LinkToAnExistingNonMarkdownFile_IsNonMarkdown()
    {
        TreeFixture tree = Tree().Exist("hooks/run.sh");

        ResolveMarkdown(tree, "../hooks/run.sh#L3").ShouldBe(
            new Link(LinkKind.MarkdownLink, "../hooks/run.sh#L3", 7, "hooks/run.sh", "L3", LinkStatus.NonMarkdown));
    }

    [Theory]
    [InlineData("file:#top")]
    [InlineData("#top")]
    [InlineData("<#top>")]
    public void Resolve_LinkWithOnlyAHeading_PointsAtTheSourceItself(string raw) =>
        ResolveMarkdown(Tree(), raw).ShouldBe(Linked(raw, "rules/a.md", "top"));

    [Fact]
    public void Resolve_MarkdownLinkToItself_IsResolved() =>
        ResolveMarkdown(Tree(), "a.md").ShouldBe(Linked("a.md", "rules/a.md"));

    [Fact]
    public void Resolve_AbsolutePathWhenTheRootIsTheFileSystemRoot_IsRelativeToIt()
    {
        var tree = new TreeFixture(root: "/").Index("etc/hosts.md");

        ResolveMarkdown(tree, "/etc/hosts.md", source: "CLAUDE.md").ShouldBe(Linked("/etc/hosts.md", "etc/hosts.md"));
    }

    [Fact]
    public void Resolve_WindowsRootAndDriveLetterLinks_AreComparedIgnoringCase()
    {
        TreeFixture tree = new TreeFixture(StringComparison.OrdinalIgnoreCase, root: "C:\\Users\\Test\\.claude")
            .Index("rules/b.md", "shared/x.md");

        ResolveMarkdown(tree, "file:///c:/users/TEST/.claude/rules/B.md").Target.ShouldBe("rules/b.md");
        ResolveMarkdown(tree, "C:/Users/Test/.claude/shared/X.md").Target.ShouldBe("shared/x.md");
        ResolveMarkdown(tree, "file:///D:/other/x.md").Status.ShouldBe(LinkStatus.External);
        ResolveMarkdown(tree, "C:\\Users\\Test\\.claude\\shared\\x.md").Status.ShouldBe(LinkStatus.External);
    }

    [Fact]
    public void Resolve_RootThatCannotBeNormalized_DoesNotThrow()
    {
        var tree = new TreeFixture(root: "/../weird");

        ResolveMarkdown(tree, "/etc/x.md", source: "CLAUDE.md").Status.ShouldBe(LinkStatus.External);
    }

    // ---- Wiki links -----------------------------------------------------------------------------------------

    private static Link ResolveWiki(TreeFixture tree, string raw, string source = "rules/a.md") =>
        Expect(tree.Resolve(LinkKind.WikiLink, raw, source, line: 4));

    private static Link WikiResult(string raw, string? target, string? heading, LinkStatus status) =>
        new(LinkKind.WikiLink, raw, 4, target, heading, status);

    [Theory]
    [InlineData("b", "rules/b.md")]
    [InlineData("b.md", "rules/b.md")]
    [InlineData(" b ", "rules/b.md")]
    [InlineData("../shared/x", "shared/x.md")]
    [InlineData("../shared/x.md", "shared/x.md")]
    [InlineData("my note", "rules/my note.md")]
    public void Resolve_WikiLinkToAFileRelativeToTheSource_AddsTheMarkdownExtension(string raw, string target) =>
        ResolveWiki(Tree(), raw).ShouldBe(WikiResult(raw, target, null, LinkStatus.Resolved));

    [Theory]
    [InlineData("b#Sec|alias", "Sec")]
    [InlineData("b#Sec", "Sec")]
    [InlineData("b# Sec ", "Sec")]
    [InlineData("b#Sec#Sub|alias", "Sec#Sub")]
    [InlineData("b#|alias", null)]
    [InlineData("b|alias", null)]
    [InlineData("b|a#b", null)]
    public void Resolve_WikiLinkWithHeadingAndAlias_SeparatesThem(string raw, string? heading) =>
        ResolveWiki(Tree(), raw).ShouldBe(WikiResult(raw, "rules/b.md", heading, LinkStatus.Resolved));

    [Theory]
    [InlineData("#Sec", "Sec")]
    [InlineData("|alias", null)]
    [InlineData(" ", null)]
    public void Resolve_WikiLinkWithoutATarget_PointsAtTheSourceItself(string raw, string? heading) =>
        ResolveWiki(Tree(), raw).ShouldBe(WikiResult(raw, "rules/a.md", heading, LinkStatus.Resolved));

    [Fact]
    public void Resolve_WikiLinkToNothingOutsideMemory_IsBroken() =>
        ResolveWiki(Tree(), "nope#Sec").ShouldBe(WikiResult("nope#Sec", null, "Sec", LinkStatus.Broken));

    [Fact]
    public void Resolve_WikiLinkToNothingInsideMemory_IsPending() =>
        ResolveWiki(Tree(), "nope", source: "projects/p/memory/MEMORY.md").ShouldBe(WikiResult("nope", null, null, LinkStatus.Pending));

    private static TreeFixture MemoryTree() => new TreeFixture()
        .Index(
            "projects/p/memory/MEMORY.md",
            "projects/p/memory/role-file.md",
            "projects/p/memory/other.md",
            "projects/p/memory/user_role.md",
            "projects/q/memory/MEMORY.md",
            "rules/a.md")
        .Memory("projects/p/memory", ("user_role", "projects/p/memory/role-file.md"), ("renamed", "projects/p/memory/other.md"));

    [Theory]
    [InlineData("renamed", "projects/p/memory/other.md")]
    [InlineData(" renamed ", "projects/p/memory/other.md")]
    [InlineData("renamed#Sec|alias", "projects/p/memory/other.md")]
    public void Resolve_WikiLinkInMemory_ResolvesByFrontmatterName(string raw, string target) =>
        ResolveWiki(MemoryTree(), raw, source: "projects/p/memory/MEMORY.md").Target.ShouldBe(target);

    [Fact]
    public void Resolve_FrontmatterNameTakesPrecedenceOverFileName() =>
        ResolveWiki(MemoryTree(), "user_role", source: "projects/p/memory/MEMORY.md")
            .ShouldBe(WikiResult("user_role", "projects/p/memory/role-file.md", null, LinkStatus.Resolved));

    [Fact]
    public void Resolve_FrontmatterNameMatchIsCaseSensitive() =>
        ResolveWiki(MemoryTree(), "Renamed", source: "projects/p/memory/MEMORY.md").Status.ShouldBe(LinkStatus.Pending);

    [Fact]
    public void Resolve_WikiLinkInMemory_FallsBackToTheFileName() =>
        ResolveWiki(MemoryTree(), "other", source: "projects/p/memory/MEMORY.md").Target.ShouldBe("projects/p/memory/other.md");

    [Fact]
    public void Resolve_MemoryNamesOfAnotherProject_DoNotApply() =>
        ResolveWiki(MemoryTree(), "renamed", source: "projects/q/memory/MEMORY.md").Status.ShouldBe(LinkStatus.Pending);

    [Fact]
    public void Resolve_MemoryNamesOutsideMemory_DoNotApply() =>
        ResolveWiki(MemoryTree(), "renamed").Status.ShouldBe(LinkStatus.Broken);

    [Theory]
    [InlineData("../../x")]
    [InlineData("a\\b")]
    [InlineData("../..")]
    public void Resolve_WikiLinkLeavingTheRoot_IsExternalAndNeverProbed(string raw)
    {
        TreeFixture tree = Tree();

        ResolveWiki(tree, raw).ShouldBe(WikiResult(raw, null, null, LinkStatus.External));
        tree.Probed.ShouldBeEmpty();
    }

    [Fact]
    public void Resolve_WikiLinkToAnExistingNonMarkdownFile_IsNonMarkdown()
    {
        TreeFixture tree = Tree().Exist("rules/scripts/run.sh", "rules/sub");

        ResolveWiki(tree, "scripts/run.sh").ShouldBe(WikiResult("scripts/run.sh", "rules/scripts/run.sh", null, LinkStatus.NonMarkdown));
        ResolveWiki(tree, "sub#Top").ShouldBe(WikiResult("sub#Top", "rules/sub", "Top", LinkStatus.NonMarkdown));
    }

    [Fact]
    public void Resolve_WikiLinkThatNormalizesToTheRoot_DoesNotProbeAnEmptyPath()
    {
        TreeFixture tree = Tree();

        ResolveWiki(tree, "..").ShouldBe(WikiResult("..", null, null, LinkStatus.Broken));
        tree.Probed.ShouldBe([".md"]);
    }

    [Fact]
    public void Resolve_WikiLinkWithBothAnIndexedFileAndAnExistingDirectory_PrefersTheIndexedFile()
    {
        TreeFixture tree = Tree().Exist("rules/b");

        ResolveWiki(tree, "b").Status.ShouldBe(LinkStatus.Resolved);
    }

    // ---- Claude paths -----------------------------------------------------------------------------------------

    private static Link ResolveClaudePath(TreeFixture tree, string raw, string source = "rules/a.md") =>
        Expect(tree.Resolve(LinkKind.ClaudePath, raw, source, line: 9));

    [Fact]
    public void Resolve_ClaudePathToAnIndexedFile_IsResolved() =>
        ResolveClaudePath(Tree(), "~/.claude/rules/b.md")
            .ShouldBe(new Link(LinkKind.ClaudePath, "~/.claude/rules/b.md", 9, "rules/b.md", null, LinkStatus.Resolved));

    [Fact]
    public void Resolve_ClaudePath_MapsOntoTheRootWhateverItsName()
    {
        var tree = new TreeFixture(root: "/srv/custom-root").Index("rules/b.md");

        ResolveClaudePath(tree, "~/.claude/rules/b.md").Target.ShouldBe("rules/b.md");
    }

    [Fact]
    public void Resolve_ClaudePathToNothing_IsBrokenWithTheTarget()
    {
        Link link = ResolveClaudePath(Tree(), "~/.claude/rules/zzz.md");

        link.Status.ShouldBe(LinkStatus.Broken);
        link.Target.ShouldBe("rules/zzz.md");
    }

    [Theory]
    [InlineData("~/.claude/rules/", "rules")]
    [InlineData("~/.claude/hooks/run.sh", "hooks/run.sh")]
    public void Resolve_ClaudePathToAnExistingNonMarkdownPath_IsNonMarkdown(string raw, string target)
    {
        TreeFixture tree = Tree().Exist("rules", "hooks/run.sh");

        Link link = ResolveClaudePath(tree, raw);

        link.Status.ShouldBe(LinkStatus.NonMarkdown);
        link.Target.ShouldBe(target);
    }

    [Theory]
    [InlineData("~/.claude/../x.md")]
    [InlineData("~/.claude/a\\b")]
    [InlineData("~/elsewhere/x.md")]
    [InlineData("/etc/passwd")]
    public void Resolve_ClaudePathLeavingTheRootOrMalformed_IsExternalAndNeverProbed(string raw)
    {
        TreeFixture tree = Tree();

        ResolveClaudePath(tree, raw).Status.ShouldBe(LinkStatus.External);
        tree.Probed.ShouldBeEmpty();
    }

    // ---- Relative paths ---------------------------------------------------------------------------------------

    private static TreeFixture SkillTree() => new TreeFixture()
        .Index("CLAUDE.md", "rules/b.md", "skills/x/SKILL.md", "skills/x/refs/a.md", "skills/x/rules/b.md", "skills/refs/c.md", "shared/s.md")
        .TopLevel("rules", "skills", "hooks", "plugins");

    private static Link? ResolveRelative(TreeFixture tree, string raw, string source = "skills/x/SKILL.md") =>
        tree.Resolve(LinkKind.RelativePath, raw, source, line: 2);

    private static Link Relative(string raw, string? target, LinkStatus status) =>
        new(LinkKind.RelativePath, raw, 2, target, null, status);

    [Fact]
    public void Resolve_RelativePath_PrefersTheSourceDirectory() =>
        ResolveRelative(SkillTree(), "refs/a.md").ShouldBe(Relative("refs/a.md", "skills/x/refs/a.md", LinkStatus.Resolved));

    [Fact]
    public void Resolve_RelativePath_TriesTheNearestAncestorFirst() =>
        ResolveRelative(SkillTree(), "refs/c.md").ShouldBe(Relative("refs/c.md", "skills/refs/c.md", LinkStatus.Resolved));

    [Fact]
    public void Resolve_RelativePath_FallsBackToTheRoot() =>
        ResolveRelative(SkillTree(), "shared/s.md").ShouldBe(Relative("shared/s.md", "shared/s.md", LinkStatus.Resolved));

    [Fact]
    public void Resolve_RelativePathPresentAtSeveralLevels_TheNearestWins() =>
        ResolveRelative(SkillTree(), "rules/b.md").ShouldBe(Relative("rules/b.md", "skills/x/rules/b.md", LinkStatus.Resolved));

    [Fact]
    public void Resolve_RelativePathWithLeadingDotSlash_DropsIt() =>
        ResolveRelative(SkillTree(), "./refs/a.md").ShouldBe(Relative("./refs/a.md", "skills/x/refs/a.md", LinkStatus.Resolved));

    [Fact]
    public void Resolve_RelativePathFromARootFile_UsesTheRoot() =>
        ResolveRelative(SkillTree(), "rules/b.md", source: "CLAUDE.md").ShouldBe(Relative("rules/b.md", "rules/b.md", LinkStatus.Resolved));

    [Fact]
    public void Resolve_RelativePathToAnExistingNonMarkdownFile_IsNonMarkdown()
    {
        TreeFixture tree = SkillTree().Exist("hooks/run.sh");

        ResolveRelative(tree, "hooks/run.sh").ShouldBe(Relative("hooks/run.sh", "hooks/run.sh", LinkStatus.NonMarkdown));
    }

    [Fact]
    public void Resolve_MissingFileInATopLevelDirectory_IsBrokenRelativeToTheRoot() =>
        ResolveRelative(SkillTree(), "hooks/missing.sh").ShouldBe(Relative("hooks/missing.sh", "hooks/missing.sh", LinkStatus.Broken));

    [Fact]
    public void Resolve_MissingFileInASkippedTopLevelDirectory_IsBroken() =>
        ResolveRelative(SkillTree(), "plugins/foo/bar.md")!.Status.ShouldBe(LinkStatus.Broken);

    [Fact]
    public void Resolve_MissingFileInADirectoryNextToTheSource_IsBroken()
    {
        TreeFixture tree = SkillTree().Exist("skills/x/scripts");

        ResolveRelative(tree, "scripts/missing.sh").ShouldBe(Relative("scripts/missing.sh", "scripts/missing.sh", LinkStatus.Broken));
    }

    [Theory]
    [InlineData("src/app/main.ts")]
    [InlineData("foo/bar")]
    [InlineData("./nothing/here.md")]
    [InlineData("dist/")]
    public void Resolve_PathThatDoesNotLookLikePartOfTheTree_IsNotALink(string raw) =>
        ResolveRelative(SkillTree(), raw).ShouldBeNull();

    [Theory]
    [InlineData("rules/../../x/y.md")]
    [InlineData("./")]
    public void Resolve_RelativePathThatCannotBeNormalizedOrHasNoName_IsNotALink(string raw) =>
        ResolveRelative(new TreeFixture().Index("CLAUDE.md").TopLevel("rules"), raw, source: "CLAUDE.md").ShouldBeNull();

    [Fact]
    public void Resolve_RelativePathThatWouldEscapeFromTheRoot_StillResolvesFromANearerBase()
    {
        TreeFixture tree = SkillTree().Index("skills/x/y.md");

        // From the root base this path climbs out of the tree; from skills/x it is skills/x/y.md.
        ResolveRelative(tree, "rules/../../x/y.md").ShouldBe(Relative("rules/../../x/y.md", "skills/x/y.md", LinkStatus.Resolved));
    }

    [Fact]
    public void Resolve_RelativePathWhoseFirstSegmentIsADot_IsNotADirectoryName()
    {
        TreeFixture tree = new TreeFixture().Index("CLAUDE.md").TopLevel("rules");

        ResolveRelative(tree, "././nothing/here.md", source: "CLAUDE.md").ShouldBeNull();
    }

    [Theory]
    [InlineData("bin/obj")]
    [InlineData("src/app")]
    [InlineData("skills/x/refs")]
    [InlineData("./hooks/missing")]
    [InlineData("hooks/run")]
    [InlineData("refs")]
    public void Resolve_RelativePathWithoutAnExtensionOrTrailingSlash_IsNotALinkEvenWhenItExists(string raw)
    {
        TreeFixture tree = SkillTree().Exist("skills/x/refs", "hooks/run", "bin/obj");

        ResolveRelative(tree, raw).ShouldBeNull();
        tree.Probed.ShouldBeEmpty();
    }

    [Fact]
    public void Resolve_RelativePathWithATrailingSlash_NamesADirectory()
    {
        TreeFixture tree = SkillTree().Exist("skills/x/refs");

        ResolveRelative(tree, "refs/").ShouldBe(Relative("refs/", "skills/x/refs", LinkStatus.NonMarkdown));
        ResolveRelative(tree, "hooks/").ShouldBe(Relative("hooks/", "hooks", LinkStatus.Broken));
        ResolveRelative(tree, "bin/").ShouldBeNull();
    }

    [Theory]
    [InlineData("hooks/run.sh")]
    [InlineData("hooks/.hidden")]
    [InlineData("hooks/archive.tar.gz")]
    public void Resolve_RelativePathWhoseLastSegmentHasADot_IsStillAReference(string raw) =>
        ResolveRelative(SkillTree(), raw).ShouldBe(Relative(raw, raw, LinkStatus.Broken));

    // ---- Shared -----------------------------------------------------------------------------------------------

    [Fact]
    public void Resolve_UnknownKind_Throws() =>
        Should.Throw<ArgumentOutOfRangeException>(() => Tree().Resolve((LinkKind)99, "x", "rules/a.md"));

    [Fact]
    public void Resolve_ManyVariants_OnlyEverProbeNormalizedRootRelativePaths()
    {
        TreeFixture tree = SkillTree().Exist("hooks/run.sh");
        LinkResolver resolver = tree.CreateResolver();
        (LinkKind Kind, string Raw)[] inputs =
        [
            (LinkKind.MarkdownLink, "../../x/./y//z.md"),
            (LinkKind.MarkdownLink, "%2e%2e/%2e/q.md"),
            (LinkKind.MarkdownLink, "/home/test/.claude/a/../b.md"),
            (LinkKind.MarkdownLink, "~/.claude//hooks/./run.sh"),
            (LinkKind.WikiLink, "sub/../thing"),
            (LinkKind.WikiLink, "missing/./note#H"),
            (LinkKind.ClaudePath, "~/.claude/a/./b/../c.md"),
            (LinkKind.RelativePath, "./refs//a.md"),
            (LinkKind.RelativePath, "hooks/./run.sh"),
            (LinkKind.RelativePath, "unknown/dir/file.md"),
        ];

        foreach ((LinkKind kind, string raw) in inputs)
        {
            resolver.Resolve(new RawLink(kind, raw, 1), "skills/x/SKILL.md");
        }

        tree.Probed.ShouldNotBeEmpty();
        tree.Probed.Where(path => !IsNormalizedRelativePath(path)).ShouldBeEmpty();
    }

    private static bool IsNormalizedRelativePath(string path)
    {
        if (path.Length == 0 || path.StartsWith('/') || path.EndsWith('/') || path.Contains('\\'))
        {
            return false;
        }

        return !path.Split('/').Any(segment => segment is "" or "." or "..");
    }
}
