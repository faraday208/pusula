using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Pusula.Indexing;
using Pusula.Links;
using Pusula.UnitTests.Support;
using Shouldly;
using Xunit;

namespace Pusula.UnitTests.Indexing;

public sealed class IndexBuilderTests
{
    private static readonly string[] RuntimeDirectories =
    [
        "plugins", "cache", "file-history", "sessions", "debug", "backups", "paste-cache", "chrome", "ide", "jobs",
        "daemon", "downloads", "session-env", "security", "usage-data", "todos", "shell-snapshots", "statsig",
        "telemetry", "state",
    ];

    private static IndexBuilder CreateBuilder(ILogger<IndexBuilder>? logger = null, StringComparison? comparison = null) =>
        new(logger ?? NullLogger<IndexBuilder>.Instance, comparison ?? StringComparison.Ordinal);

    private static ConfigIndex Build(TempDirectory temp, ILogger<IndexBuilder>? logger = null) =>
        CreateBuilder(logger).Build(temp.Path);

    private static string[] Keys(ConfigIndex index) => [.. index.Files.Keys];

    private static string[] Describe(ConfigFile file) =>
        [.. file.Links.Select(link => $"{link.Kind}|{link.Raw}|{link.Line}|{link.Status}|{link.Target}")];

    // ---- Root handling ----------------------------------------------------------------------------------------

    [Fact]
    public void Build_MissingRoot_ThrowsDirectoryNotFoundExceptionNamingThePath()
    {
        using var temp = new TempDirectory();
        string missing = Path.Combine(temp.Path, "does-not-exist");

        DirectoryNotFoundException exception = Should.Throw<DirectoryNotFoundException>(() => CreateBuilder().Build(missing));

        exception.Message.ShouldContain(missing);
    }

    [Fact]
    public void Build_RootThatIsAFile_ThrowsDirectoryNotFoundException()
    {
        using var temp = new TempDirectory();
        string file = temp.Write("file.md", "x");

        Should.Throw<DirectoryNotFoundException>(() => CreateBuilder().Build(file));
    }

    [Fact]
    public void Build_BlankRoot_ThrowsArgumentException()
    {
        Should.Throw<ArgumentException>(() => CreateBuilder().Build("   "));
        Should.Throw<ArgumentNullException>(() => CreateBuilder().Build(null!));
    }

    [Fact]
    public void Build_EmptyRoot_ReturnsAnEmptyIndexAtVersionZero()
    {
        using var temp = new TempDirectory();

        ConfigIndex index = CreateBuilder().Build(temp.Path + Path.DirectorySeparatorChar);

        index.Root.ShouldBe(temp.Path);
        index.Files.ShouldBeEmpty();
        index.Backlinks.ShouldBeEmpty();
        index.OutputStyle.ShouldBeNull();
        index.Version.ShouldBe(0);
        index.BuiltAt.ShouldBe(DateTimeOffset.UtcNow, tolerance: TimeSpan.FromMinutes(1));
    }

    [Fact]
    public void Build_PublicConstructor_UsesThePlatformPathComparison()
    {
        using var temp = new TempDirectory();
        temp.Write("Rules/a.md", "x");

        ConfigIndex index = new IndexBuilder(NullLogger<IndexBuilder>.Instance).Build(temp.Path);

        index.Files["Rules/a.md"].Layer.ShouldBe(OperatingSystem.IsWindows() ? Layer.Rule : Layer.Other);
    }

    // ---- What gets scanned ------------------------------------------------------------------------------------

    [Fact]
    public void Build_RuntimeDirectoriesAtTheRoot_AreSkippedButNestedOnesAreNot()
    {
        using var temp = new TempDirectory();
        temp.Write("CLAUDE.md", "root");
        foreach (string directory in RuntimeDirectories)
        {
            temp.Write($"{directory}/x.md", "runtime");
            temp.Write($"{directory}/deep/y.md", "runtime");
        }

        temp.Write("rules/cache/y.md", "nested");
        temp.Write("rules/plugins/z.md", "nested");

        ConfigIndex index = Build(temp);

        Keys(index).ShouldBe(["CLAUDE.md", "rules/cache/y.md", "rules/plugins/z.md"]);
    }

    [Theory]
    [InlineData("plugins", "Ordinal", true)]
    [InlineData("plugins", "OrdinalIgnoreCase", true)]
    [InlineData("Plugins", "Ordinal", false)]
    [InlineData("Plugins", "OrdinalIgnoreCase", true)]
    [InlineData("projects", "Ordinal", false)]
    [InlineData("rules", "OrdinalIgnoreCase", false)]
    [InlineData("evals", "Ordinal", false)]
    [InlineData("pluginsx", "Ordinal", false)]
    public void IsSkippedTopLevelDirectory_Name_FollowsTheComparison(string name, string comparison, bool expected) =>
        IndexBuilder.IsSkippedTopLevelDirectory(name, Enum.Parse<StringComparison>(comparison)).ShouldBe(expected);

    [Fact]
    public void Build_DotDirectoriesAndDotFiles_AreSkippedAtAnyDepth()
    {
        using var temp = new TempDirectory();
        temp.Write("keep.md", "x");
        temp.Write(".git/x.md", "x");
        temp.Write(".obsidian/x.md", "x");
        temp.Write(".hidden.md", "x");
        temp.Write(".credentials.json", "{}");
        temp.Write("rules/.draft.md", "x");
        temp.Write("rules/.cache/x.md", "x");
        temp.Write("rules/visible.md", "x");

        Keys(Build(temp)).ShouldBe(["keep.md", "rules/visible.md"]);
    }

    [Fact]
    public void Build_NonMarkdownFiles_AreNeverIndexed()
    {
        using var temp = new TempDirectory();
        temp.Write("a.md", "x");
        temp.Write("b.MD", "x");
        temp.Write("c.Md", "x");
        temp.Write("c.txt", "x");
        temp.Write("d.json", "{}");
        temp.Write("settings.json", "{}");
        temp.Write("e.md.bak", "x");
        temp.Write("f", "x");
        temp.Write("hooks/run.sh", "echo");
        temp.Write("rules/dir.md/inner.md", "a directory named like a file");

        Keys(Build(temp)).ShouldBe(["a.md", "b.MD", "c.Md", "rules/dir.md/inner.md"]);
    }

    [Fact]
    public void Build_ProjectsDirectory_OnlyEntersMemorySubtrees()
    {
        using var temp = new TempDirectory();
        temp.Write("projects/p/memory/MEMORY.md", "index");
        temp.Write("projects/p/memory/note.md", "note");
        temp.Write("projects/p/memory/sub/deep.md", "deep");
        temp.Write("projects/p/memory/.hidden/x.md", "hidden");
        temp.Write("projects/p/memory/.hidden.md", "hidden");
        temp.Write("projects/p/memory/data.json", "{}");
        temp.Write("projects/q/memory/MEMORY.md", "other project");
        temp.Write("projects/p/transcript.jsonl", "{}");
        temp.Write("projects/p/notes.md", "not memory");
        temp.Write("projects/p/MEMORY.md", "not in memory");
        temp.Write("projects/p/sessions/x.md", "not memory");
        temp.Write("projects/p/subagents/memory/x.md", "not memory");
        temp.Write("projects/README.md", "not memory");
        temp.Write("projects/.hidden/memory/a.md", "hidden project");
        temp.Write("rules/projects/x.md", "an unrelated nested directory");

        ConfigIndex index = Build(temp);

        Keys(index).ShouldBe(
        [
            "projects/p/memory/MEMORY.md",
            "projects/p/memory/note.md",
            "projects/p/memory/sub/deep.md",
            "projects/q/memory/MEMORY.md",
            "rules/projects/x.md",
        ]);
        index.Files["projects/p/memory/MEMORY.md"].Layer.ShouldBe(Layer.MemoryIndex);
        index.Files["projects/p/memory/sub/deep.md"].Layer.ShouldBe(Layer.Memory);
    }

    [Fact]
    public void Build_Files_AreSortedByPathAndKeyedWithForwardSlashes()
    {
        using var temp = new TempDirectory();
        temp.Write("rules/z.md", "x");
        temp.Write("b.md", "x");
        temp.Write("rules-x.md", "x");
        temp.Write("a.md", "x");
        temp.Write("rules/sub/deep/m.md", "x");
        temp.Write("Z.md", "x");

        Keys(Build(temp)).ShouldBe(["Z.md", "a.md", "b.md", "rules-x.md", "rules/sub/deep/m.md", "rules/z.md"]);
    }

    [Fact]
    public void Build_UnchangedTree_ProducesAnEquivalentIndex()
    {
        using var temp = new TempDirectory();
        temp.Write("CLAUDE.md", "[a](rules/a.md)");
        temp.Write("rules/a.md", "x");

        IndexDiff.Compute(Build(temp), Build(temp)).IsEmpty.ShouldBeTrue();
    }

    // ---- File contents ----------------------------------------------------------------------------------------

    [Fact]
    public void Build_Utf8ByteOrderMark_IsStrippedBeforeParsing()
    {
        using var temp = new TempDirectory();
        byte[] text = System.Text.Encoding.UTF8.GetBytes("---\nname: bom\n---\nbody");
        temp.WriteBytes("rules/bom.md", [0xEF, 0xBB, 0xBF, .. text]);

        ConfigFile file = Build(temp).Files["rules/bom.md"];

        file.Content.ShouldBe("---\nname: bom\n---\nbody");
        FrontmatterValues.GetString(file.Frontmatter, "name").ShouldBe("bom");
        file.FrontmatterError.ShouldBeNull();
    }

    [Fact]
    public void Build_InvalidUtf8_IsReplacedInsteadOfFailing()
    {
        using var temp = new TempDirectory();
        temp.WriteBytes("a.md", [0x61, 0xFF, 0x62]);

        Build(temp).Files["a.md"].Content.ShouldBe("a�b");
    }

    [Fact]
    public void Build_FileLargerThanTheLimit_IsSkippedAndLogged()
    {
        using var temp = new TempDirectory();
        temp.WriteBytes("rules/big.md", new byte[IndexBuilder.MaxFileBytes + 1]);
        temp.WriteBytes("rules/limit.md", new byte[IndexBuilder.MaxFileBytes]);
        temp.Write("rules/small.md", "x");
        var logger = new CapturingLogger<IndexBuilder>();

        ConfigIndex index = Build(temp, logger);

        IndexBuilder.MaxFileBytes.ShouldBe(2 * 1024 * 1024);
        Keys(index).ShouldBe(["rules/limit.md", "rules/small.md"]);
        logger.Entries.ShouldContain(entry => entry.Level == LogLevel.Warning && entry.Message.Contains("rules/big.md", StringComparison.Ordinal));
    }

    [Fact]
    public void Build_FileProperties_AreFilledIn()
    {
        using var temp = new TempDirectory();
        temp.Write("rules/a.md", "x");
        const string content = "---\nname: deploy\ndescription: Ships it\ndisable-model-invocation: false\n---\n# Deploy\nSee [[other]] and `rules/a.md`.\n";
        temp.Write("skills/deploy/SKILL.md", content);

        ConfigFile file = Build(temp).Files["skills/deploy/SKILL.md"];

        file.Path.ShouldBe("skills/deploy/SKILL.md");
        file.Name.ShouldBe("SKILL.md");
        file.Layer.ShouldBe(Layer.Skill);
        file.LoadMode.ShouldBe(LoadMode.DescriptionEverySession);
        file.Tokens.ShouldBe(new TokenCounts(
            Total: TokenEstimator.Estimate(content),
            EverySession: TokenEstimator.Estimate("deploy: Ships it"),
            ProjectSession: 0));
        FrontmatterValues.GetString(file.Frontmatter, "name").ShouldBe("deploy");
        file.FrontmatterError.ShouldBeNull();
        file.FrontmatterErrorLine.ShouldBeNull();
        file.Content.ShouldBe(content);
        file.Body.ShouldBe("# Deploy\nSee [[other]] and `rules/a.md`.\n");
        file.BodyStartLine.ShouldBe(6);
        Describe(file).ShouldBe(["WikiLink|other|7|Broken|", "RelativePath|rules/a.md|7|Resolved|rules/a.md"]);
        file.IsOrphan.ShouldBeFalse();
    }

    [Fact]
    public void Build_MalformedFrontmatter_ReportsTheErrorAndClassifiesFromTheFallbackKeys()
    {
        using var temp = new TempDirectory();
        temp.Write("rules/x.md", "---\npaths: src/**\ndescription: a: b\n---\nbody");

        ConfigFile file = Build(temp).Files["rules/x.md"];

        file.FrontmatterError.ShouldNotBeNull().ShouldStartWith("Line 3");
        file.FrontmatterErrorLine.ShouldBe(3);
        file.Layer.ShouldBe(Layer.PathRule);
        file.LoadMode.ShouldBe(LoadMode.Conditional);
        file.Body.ShouldBe("body");
    }

    [Fact]
    public void Build_MalformedFrontmatterWithoutAPosition_HasTheErrorButNoErrorLine()
    {
        using var temp = new TempDirectory();
        temp.Write("rules/list.md", "---\n- a\n- b\n---\nbody");

        ConfigFile file = Build(temp).Files["rules/list.md"];

        file.FrontmatterError.ShouldBe("Frontmatter is not a mapping");
        file.FrontmatterErrorLine.ShouldBeNull();
    }

    [Fact]
    public void Build_MalformedFrontmatterInAFileWithByteOrderMarkAndCrLf_NamesTheLineOfTheContent()
    {
        using var temp = new TempDirectory();
        byte[] text = System.Text.Encoding.UTF8.GetBytes("---\r\nname: win\r\ndescription: Does X: then Y\r\n---\r\nbody\r\n");
        temp.WriteBytes("rules/win.md", [0xEF, 0xBB, 0xBF, .. text]);

        ConfigFile file = Build(temp).Files["rules/win.md"];

        // The line is counted in the content (the byte order mark is not part of it) and is the line that holds the text.
        int line = file.FrontmatterErrorLine.ShouldNotBeNull();
        line.ShouldBe(3);
        file.Content.Split('\n')[line - 1].TrimEnd('\r').ShouldBe("description: Does X: then Y");
        file.FrontmatterError.ShouldNotBeNull().ShouldStartWith("Line 3, column ");
        FrontmatterValues.GetString(file.Frontmatter, "name").ShouldBe("win");
    }

    // ---- settings.json / output style -------------------------------------------------------------------------

    [Fact]
    public void Build_OutputStyleFromSettings_SelectsTheMatchingStyleOnly()
    {
        using var temp = new TempDirectory();
        temp.Write("settings.json", "{ \"permissions\": {}, \"outputStyle\": \"Explanatory\" }");
        temp.Write("output-styles/explanatory.md", "style one");
        temp.Write("output-styles/custom.md", "---\nname: EXPLANATORY\n---\nstyle two");
        temp.Write("output-styles/other.md", "style three");

        ConfigIndex index = Build(temp);

        index.OutputStyle.ShouldBe("Explanatory");
        index.Files["output-styles/explanatory.md"].LoadMode.ShouldBe(LoadMode.EverySession);
        index.Files["output-styles/custom.md"].LoadMode.ShouldBe(LoadMode.EverySession);
        index.Files["output-styles/other.md"].LoadMode.ShouldBe(LoadMode.Inactive);
        index.Files["output-styles/explanatory.md"].Tokens.EverySession.ShouldBe(TokenEstimator.Estimate("style one"));
        index.Files.ContainsKey("settings.json").ShouldBeFalse();
    }

    [Theory]
    [InlineData("{ not json")]
    [InlineData("[]")]
    [InlineData("\"text\"")]
    [InlineData("{}")]
    [InlineData("{\"outputStyle\": 5}")]
    [InlineData("{\"outputStyle\": null}")]
    [InlineData("{\"outputStyle\": \"\"}")]
    [InlineData("")]
    public void Build_UnusableSettings_IgnoreTheOutputStyle(string settings)
    {
        using var temp = new TempDirectory();
        temp.Write("settings.json", settings);
        temp.Write("output-styles/explanatory.md", "style");

        ConfigIndex index = Build(temp);

        index.OutputStyle.ShouldBeNull();
        index.Files["output-styles/explanatory.md"].LoadMode.ShouldBe(LoadMode.Inactive);
    }

    [Fact]
    public void Build_MalformedSettings_LogsAWarning()
    {
        using var temp = new TempDirectory();
        temp.Write("settings.json", "{ not json");
        var logger = new CapturingLogger<IndexBuilder>();

        Build(temp, logger);

        logger.Entries.ShouldContain(entry => entry.Level == LogLevel.Warning && entry.Message.Contains("settings.json", StringComparison.Ordinal));
    }

    [Fact]
    public void Build_SettingsThatIsADirectory_IsIgnored()
    {
        using var temp = new TempDirectory();
        temp.CreateDirectory("settings.json");

        Build(temp).OutputStyle.ShouldBeNull();
    }

    // ---- Links, backlinks, orphans ----------------------------------------------------------------------------

    [Fact]
    public void Build_ResolvedLinks_BecomeBacklinksOrderedBySourceThenLine()
    {
        using var temp = new TempDirectory();
        temp.Write(
            "CLAUDE.md",
            "See [rule](rules/a.md) and [[rules/b]] and `rules/c.md` and ~/.claude/rules/d.md.\nSelf: [me](CLAUDE.md). Broken: [x](nope.md).\n");
        temp.Write("rules/a.md", "# A\n[[b]]\n");
        temp.Write("rules/b.md", "x");
        temp.Write("rules/c.md", "x");
        temp.Write("rules/d.md", "x");

        ConfigIndex index = Build(temp);

        Backlinks(index, "rules/a.md").ShouldBe(["CLAUDE.md|MarkdownLink|1"]);
        Backlinks(index, "rules/b.md").ShouldBe(["CLAUDE.md|WikiLink|1", "rules/a.md|WikiLink|2"]);
        Backlinks(index, "rules/c.md").ShouldBe(["CLAUDE.md|RelativePath|1"]);
        Backlinks(index, "rules/d.md").ShouldBe(["CLAUDE.md|ClaudePath|1"]);
        index.Backlinks.ContainsKey("CLAUDE.md").ShouldBeFalse();
        index.Backlinks.Keys.Order(StringComparer.Ordinal).ShouldBe(["rules/a.md", "rules/b.md", "rules/c.md", "rules/d.md"]);
        Describe(index.Files["CLAUDE.md"]).ShouldContain("MarkdownLink|nope.md|2|Broken|nope.md");
        Describe(index.Files["CLAUDE.md"]).ShouldContain("MarkdownLink|CLAUDE.md|2|Resolved|CLAUDE.md");

        static string[] Backlinks(ConfigIndex index, string path) =>
            [.. index.Backlinks[path].Select(backlink => $"{backlink.Source}|{backlink.Kind}|{backlink.Line}")];
    }

    [Fact]
    public void Build_OrphanCandidates_AreFlaggedUnlessSomeOtherFileLinksToThem()
    {
        using var temp = new TempDirectory();
        temp.Write("CLAUDE.md", "[tools](TOOLS.md) [t](shared/t.md)");
        temp.Write("TOOLS.md", "linked reference");
        temp.Write("README.md", "unlinked reference");
        temp.Write("shared/s.md", "unlinked shared");
        temp.Write("shared/t.md", "linked shared");
        temp.Write("hooks/h.md", "unlinked other");
        temp.Write("rules/r.md", "unlinked rule: not an orphan candidate");
        temp.Write("agents/ag.md", "unlinked agent: not an orphan candidate");
        temp.Write("skills/a/SKILL.md", "[ref](ref2.md)");
        temp.Write("skills/a/ref.md", "unlinked skill resource");
        temp.Write("skills/a/ref2.md", "linked skill resource");
        temp.Write("projects/p/memory/MEMORY.md", "- [linked](linked.md)\n- [[named]]\n");
        temp.Write("projects/p/memory/linked.md", "x");
        temp.Write("projects/p/memory/n1.md", "---\nname: named\n---\nx");
        temp.Write("projects/p/memory/lonely.md", "x");
        temp.Write("projects/p/memory/self.md", "[me](self.md) [[self]]");

        ConfigIndex index = Build(temp);

        string[] orphans = [.. index.Files.Values.Where(file => file.IsOrphan).Select(file => file.Path)];
        orphans.ShouldBe(["README.md", "hooks/h.md", "projects/p/memory/lonely.md", "projects/p/memory/self.md", "shared/s.md", "skills/a/ref.md"]);
    }

    [Fact]
    public void Build_MemoryWikiLinks_ResolveByFrontmatterNameAndUnresolvedOnesArePending()
    {
        using var temp = new TempDirectory();
        temp.Write("projects/p/memory/MEMORY.md", "[[user_role]] [[missing]] [[dup]] [[plain-file]]\n");
        temp.Write("projects/p/memory/role-file.md", "---\nname: user_role\n---\nx");
        temp.Write("projects/p/memory/dup1.md", "---\nname: dup\n---\nx");
        temp.Write("projects/p/memory/dup2.md", "---\nname: dup\n---\nx");
        temp.Write("projects/p/memory/plain-file.md", "no frontmatter");
        temp.Write("projects/p/memory/unnamed.md", "---\nname: [a, b]\n---\nx");

        ConfigIndex index = Build(temp);

        Describe(index.Files["projects/p/memory/MEMORY.md"]).ShouldBe(
        [
            "WikiLink|user_role|1|Resolved|projects/p/memory/role-file.md",
            "WikiLink|missing|1|Pending|",
            "WikiLink|dup|1|Resolved|projects/p/memory/dup1.md",
            "WikiLink|plain-file|1|Resolved|projects/p/memory/plain-file.md",
        ]);
        index.Backlinks["projects/p/memory/role-file.md"].Single().Source.ShouldBe("projects/p/memory/MEMORY.md");
    }

    [Fact]
    public void Build_RelativePathLinks_ConsiderSkippedAndHiddenTopLevelDirectories()
    {
        using var temp = new TempDirectory();
        temp.Write("CLAUDE.md", "`plugins/keep.md` `hooks/run.sh` `nothing/here.md` `skills/zzz.md` `.git/HEAD.txt` `.git/missing.txt`\n");
        temp.Write("plugins/keep.md", "skipped directory, never indexed");
        temp.Write("hooks/run.sh", "echo");
        temp.Write("skills/other.md", "x");
        temp.Write(".git/HEAD.txt", "ref");

        ConfigFile file = Build(temp).Files["CLAUDE.md"];

        Describe(file).ShouldBe(
        [
            "RelativePath|plugins/keep.md|1|NonMarkdown|plugins/keep.md",
            "RelativePath|hooks/run.sh|1|NonMarkdown|hooks/run.sh",
            "RelativePath|skills/zzz.md|1|Broken|skills/zzz.md",
            "RelativePath|.git/HEAD.txt|1|NonMarkdown|.git/HEAD.txt",
            "RelativePath|.git/missing.txt|1|Broken|.git/missing.txt",
        ]);
    }

    [Fact]
    public void Build_CodeSamplePathsWithoutAnExtension_AreNotLinks()
    {
        using var temp = new TempDirectory();
        temp.Write("CLAUDE.md", "Build into `bin/obj`, keep `skills/deploy` and `hooks/run` around, or `hooks/` whole.\n");
        temp.Write("skills/deploy/SKILL.md", "x");
        temp.Write("hooks/run", "echo");

        ConfigFile file = Build(temp).Files["CLAUDE.md"];

        Describe(file).ShouldBe(["RelativePath|hooks/|1|NonMarkdown|hooks"]);
    }

    [Fact]
    public void Build_ClaudePathWithAShellVariable_LinksTheDirectoryBeforeTheVariable()
    {
        using var temp = new TempDirectory();
        temp.Write("CLAUDE.md", "for s in ...; do cat ~/.claude/skills/$s/SKILL.md; done\n");
        temp.Write("skills/deploy/SKILL.md", "x");

        ConfigFile file = Build(temp).Files["CLAUDE.md"];

        Describe(file).ShouldBe(["ClaudePath|~/.claude/skills/|1|NonMarkdown|skills"]);
        file.Links.ShouldNotContain(link => link.Status == LinkStatus.Broken);
    }

    [Fact]
    public void Build_LinksEscapingTheRoot_AreExternalAndResolveNothing()
    {
        using var temp = new TempDirectory();
        string outside = Path.Combine(temp.Path, "outside");
        Directory.CreateDirectory(outside);
        File.WriteAllText(Path.Combine(outside, "secret.md"), "secret");
        string root = temp.CreateDirectory("root");
        temp.Write("root/CLAUDE.md", "[a](../outside/secret.md) [b](%2e%2e/outside/secret.md) [[../outside/secret]] `../outside/secret.md`");

        ConfigIndex index = CreateBuilder().Build(root);

        Describe(index.Files["CLAUDE.md"]).ShouldBe(
        [
            "MarkdownLink|../outside/secret.md|1|External|",
            "MarkdownLink|%2e%2e/outside/secret.md|1|External|",
            "WikiLink|../outside/secret|1|External|",
        ]);
        index.Backlinks.ShouldBeEmpty();
    }

    [Fact]
    public void PathExists_RelativePathInsideTheRoot_ProbesFilesAndDirectories()
    {
        using var temp = new TempDirectory();
        temp.Write("rules/a.md", "x");
        temp.Write("hooks/run.sh", "echo");

        IndexBuilder.PathExists(temp.Path, "rules/a.md").ShouldBeTrue();
        IndexBuilder.PathExists(temp.Path, "hooks").ShouldBeTrue();
        IndexBuilder.PathExists(temp.Path, "hooks/run.sh").ShouldBeTrue();
        IndexBuilder.PathExists(temp.Path, "hooks/missing.sh").ShouldBeFalse();
    }

    [Fact]
    public void PathExists_PathThroughAFolderThatIsALinkIntoTheNetwork_DoesNotExistAsFarAsTheProbeCanSay()
    {
        using var temp = new TempDirectory();
        temp.Write("root/here/x.md", "x");
        temp.Write("net/x.md", "x");
        TestLinks.ToFolder(temp.Resolve("root/evil"), temp.Resolve("net"));
        TestLinks.ToFolder(temp.Resolve("root/ok"), temp.Resolve("root/here"));
        string root = temp.Resolve("root");

        // The text of a note is enough to make such a path, so that it is not looked at: the file is there, behind the link, and the probe does not go to see.
        using (FakeNetwork.In(temp.Resolve("net")))
        {
            IndexBuilder.PathExists(root, "evil/x.md").ShouldBeFalse();
            IndexBuilder.PathExists(root, "evil").ShouldBeFalse();
            IndexBuilder.PathExists(root, "ok/x.md").ShouldBeTrue();
        }

        IndexBuilder.PathExists(root, "evil/x.md").ShouldBeTrue();
    }

    [Fact]
    public void PathExists_RootedValue_NeverEscapesTheRoot()
    {
        using var temp = new TempDirectory();
        string root = temp.CreateDirectory("root");
        string outside = temp.Write("outside.md", "x");

        IndexBuilder.PathExists(root, outside).ShouldBeFalse();
        IndexBuilder.PathExists(root, outside.Replace('\\', '/')).ShouldBeFalse();
    }

    // ---- A body that makes a link pattern run out of time ------------------------------------------------------

    [Fact]
    public void Build_BodyThatMakesALinkPatternRunOutOfTime_IsIndexedWithoutLinksAndTheRestIsKept()
    {
        using var temp = new TempDirectory();
        temp.Write("rules/hostile.md", PathologicalMarkdown.UnclosedBacktickRuns(1_000_000, "a"));
        temp.Write("rules/a.md", "See [b](b.md).");
        temp.Write("rules/b.md", "# B");
        var logger = new CapturingLogger<IndexBuilder>();
        var builder = new IndexBuilder(logger, StringComparison.Ordinal)
        {
            LinkPatterns = LinkExtractor.PatternSet.WithTimeout(TimeSpan.FromMilliseconds(100)),
        };

        ConfigIndex index = builder.Build(temp.Path);

        Keys(index).ShouldBe(["rules/a.md", "rules/b.md", "rules/hostile.md"]);
        index.Files["rules/hostile.md"].Links.ShouldBeEmpty();
        Describe(index.Files["rules/a.md"]).ShouldBe(["MarkdownLink|b.md|1|Resolved|rules/b.md"]);
        index.Backlinks["rules/b.md"].Select(backlink => backlink.Source).ShouldBe(["rules/a.md"]);
    }

    [Fact]
    public void Build_BodyThatMakesALinkPatternRunOutOfTime_LogsOneWarningNamingTheFile()
    {
        using var temp = new TempDirectory();
        temp.Write("rules/hostile.md", PathologicalMarkdown.UnclosedBacktickRuns(1_000_000, "a"));
        temp.Write("rules/fine.md", "See [x](x.md).");
        var logger = new CapturingLogger<IndexBuilder>();
        var builder = new IndexBuilder(logger, StringComparison.Ordinal)
        {
            LinkPatterns = LinkExtractor.PatternSet.WithTimeout(TimeSpan.FromMilliseconds(100)),
        };

        builder.Build(temp.Path);

        string warning = logger.Entries.Where(entry => entry.Level >= LogLevel.Warning).Select(entry => entry.Message).ShouldHaveSingleItem();
        warning.ShouldContain("rules/hostile.md");
        warning.ShouldContain("not extracted");
    }

    [Fact]
    public void Build_BodiesThatFinishInTime_LogNoWarning()
    {
        using var temp = new TempDirectory();
        temp.Write("a.md", "See [b](b.md) and `rules/c.md` and [[d]].");
        temp.Write("b.md", "x");
        var logger = new CapturingLogger<IndexBuilder>();

        Build(temp, logger);

        logger.Entries.ShouldNotContain(entry => entry.Level >= LogLevel.Warning);
    }

    // ---- Case-insensitive platforms ----------------------------------------------------------------------------

    [Fact]
    public void Build_WindowsPolicy_ComparesNamesIgnoringCaseAndKeepsTheSpellingOfTheFile()
    {
        using var temp = new TempDirectory();
        temp.Write("CLAUDE.md", "[a](rules/a.md) `RULES/A.MD`");
        temp.Write("Rules/A.md", "x");
        temp.Write("Plugins/skipped.md", "skipped");
        temp.Write("Projects/p/Memory/MEMORY.md", "index");

        ConfigIndex index = CreateBuilder(comparison: StringComparison.OrdinalIgnoreCase).Build(temp.Path);

        Keys(index).ShouldBe(["CLAUDE.md", "Projects/p/Memory/MEMORY.md", "Rules/A.md"]);
        index.Files["rules/a.md"].Layer.ShouldBe(Layer.Rule);
        index.Files["projects/p/memory/memory.md"].Layer.ShouldBe(Layer.MemoryIndex);
        Describe(index.Files["CLAUDE.md"]).ShouldBe(
        [
            "MarkdownLink|rules/a.md|1|Resolved|Rules/A.md",
            "RelativePath|RULES/A.MD|1|Resolved|Rules/A.md",
        ]);
        index.Backlinks["RULES/A.MD"].Count.ShouldBe(2);
    }

    // ---- Logging ----------------------------------------------------------------------------------------------

    [Fact]
    public void Build_CompletedBuild_LogsFileCountAndDuration()
    {
        using var temp = new TempDirectory();
        temp.Write("a.md", "x");
        temp.Write("b.md", "x");
        var logger = new CapturingLogger<IndexBuilder>();

        Build(temp, logger);

        logger.Entries.ShouldContain(entry =>
            entry.Level == LogLevel.Information
            && entry.Message.Contains("Indexed 2 files", StringComparison.Ordinal)
            && entry.Message.Contains(" ms", StringComparison.Ordinal));
    }
}
