using System.Collections.Immutable;
using System.Runtime.Versioning;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Pusula.Indexing;
using Pusula.UnitTests.Support;
using Shouldly;
using Xunit;

namespace Pusula.UnitTests.Indexing;

/// <summary>Symbolic links and permissions: behavior that depends on the file system.</summary>
public sealed class IndexBuilderFileSystemTests
{
    private static ConfigIndex Build(string root, ILogger<IndexBuilder>? logger = null) =>
        new IndexBuilder(logger ?? NullLogger<IndexBuilder>.Instance).Build(root);

    private static string[] Keys(ConfigIndex index) => [.. index.Files.Keys];

    private static void CreateDirectoryLink(string linkPath, string target)
    {
        try
        {
            Directory.CreateSymbolicLink(linkPath, target);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or PlatformNotSupportedException)
        {
            Assert.Skip($"Symbolic links are not available here: {exception.Message}");
        }
    }

    private static void CreateFileLink(string linkPath, string target)
    {
        try
        {
            File.CreateSymbolicLink(linkPath, target);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or PlatformNotSupportedException)
        {
            Assert.Skip($"Symbolic links are not available here: {exception.Message}");
        }
    }

    [Fact]
    public void Build_SymbolicLinkLoops_TerminateAndEachFileIsIndexedOncePerPath()
    {
        using var temp = new TempDirectory();
        temp.Write("a.md", "x");
        temp.Write("rules/r.md", "x");
        temp.Write("skills/s/SKILL.md", "x");
        CreateDirectoryLink(temp.Resolve("rules/loop"), temp.Path);
        CreateDirectoryLink(temp.Resolve("skills/back"), temp.Resolve("rules"));
        CreateDirectoryLink(temp.Resolve("self"), temp.Resolve("self"));
        var logger = new CapturingLogger<IndexBuilder>();

        ConfigIndex index = Build(temp.Path, logger);

        Keys(index).ShouldBe(["a.md", "rules/r.md", "skills/back/r.md", "skills/s/SKILL.md"]);

        // A loop is a normal setup, not a problem: it is noted at debug level only.
        logger.Entries.ShouldContain(entry => entry.Level == LogLevel.Debug && entry.Message.Contains("rules/loop", StringComparison.Ordinal));
        logger.Entries.ShouldNotContain(entry => entry.Level >= LogLevel.Warning);
    }

    [Fact]
    public void Build_LinkBackToAnAncestorThroughARelativeTarget_IsNotFollowed()
    {
        using var temp = new TempDirectory();
        temp.Write("rules/deep/r.md", "x");
        CreateDirectoryLink(temp.Resolve("rules/deep/up"), "../..");

        Keys(Build(temp.Path)).ShouldBe(["rules/deep/r.md"]);
    }

    [Fact]
    public void Build_DirectoryLinkToAFolderOutsideTheRoot_IsFollowedUnderTheLinkName()
    {
        using var temp = new TempDirectory();
        string root = temp.CreateDirectory("root");
        temp.Write("outside/skill/SKILL.md", "---\nname: linked\n---\nbody");
        temp.Write("outside/skill/ref.md", "reference");
        CreateDirectoryLink(Path.Combine(root, "skills-link"), temp.Resolve("outside/skill"));
        Directory.CreateDirectory(Path.Combine(root, "skills"));
        CreateDirectoryLink(Path.Combine(root, "skills", "one"), temp.Resolve("outside/skill"));
        CreateDirectoryLink(Path.Combine(root, "skills", "two"), temp.Resolve("outside/skill"));

        ConfigIndex index = Build(root);

        Keys(index).ShouldBe(
        [
            "skills-link/SKILL.md",
            "skills-link/ref.md",
            "skills/one/SKILL.md",
            "skills/one/ref.md",
            "skills/two/SKILL.md",
            "skills/two/ref.md",
        ]);
        index.Files["skills/one/SKILL.md"].Layer.ShouldBe(Layer.Skill);
        index.Files["skills/two/ref.md"].Layer.ShouldBe(Layer.SkillResource);
        index.Files["skills-link/SKILL.md"].Layer.ShouldBe(Layer.Other);
    }

    [Fact]
    public void Build_RootThatIsASymbolicLink_IsFollowedAndLoopsBackToItAreSkipped()
    {
        using var temp = new TempDirectory();
        temp.Write("real/CLAUDE.md", "x");
        temp.Write("real/rules/r.md", "x");
        string link = temp.Resolve("link");
        CreateDirectoryLink(link, temp.Resolve("real"));
        CreateDirectoryLink(temp.Resolve("real/rules/loop"), link);

        ConfigIndex index = Build(link);

        Keys(index).ShouldBe(["CLAUDE.md", "rules/r.md"]);
        index.Root.ShouldBe(link);
    }

    [Fact]
    public void Build_FileSymbolicLink_IsReadThroughTheLink()
    {
        using var temp = new TempDirectory();
        temp.Write("shared/real.md", "the content");
        CreateFileLink(temp.Resolve("rules.md"), temp.Resolve("shared/real.md"));

        ConfigIndex index = Build(temp.Path);

        index.Files["rules.md"].Content.ShouldBe("the content");
        index.Files["shared/real.md"].Content.ShouldBe("the content");
    }

    [Fact]
    public void Build_FileSymbolicLinkToALargeFile_IsSizedByItsTargetNotTheLink()
    {
        using var temp = new TempDirectory();
        temp.WriteBytes("shared/big.md", new byte[IndexBuilder.MaxFileBytes + 1]);
        CreateFileLink(temp.Resolve("alias.md"), temp.Resolve("shared/big.md"));
        var logger = new CapturingLogger<IndexBuilder>();

        ConfigIndex index = Build(temp.Path, logger);

        index.Files.ShouldBeEmpty();
        logger.Entries.Count(entry => entry.Level == LogLevel.Warning).ShouldBe(2);
    }

    [Fact]
    public void Build_DanglingAndLoopingFileLinks_AreSkippedWithAWarning()
    {
        using var temp = new TempDirectory();
        temp.Write("keep.md", "x");
        CreateFileLink(temp.Resolve("dangling.md"), temp.Resolve("missing.md"));
        CreateFileLink(temp.Resolve("a.md"), temp.Resolve("b.md"));
        CreateFileLink(temp.Resolve("b.md"), temp.Resolve("a.md"));
        CreateDirectoryLink(temp.Resolve("dangling-dir"), temp.Resolve("missing-dir"));
        var logger = new CapturingLogger<IndexBuilder>();

        ConfigIndex index = Build(temp.Path, logger);

        Keys(index).ShouldBe(["keep.md"]);
        logger.Entries.ShouldContain(entry => entry.Level == LogLevel.Warning && entry.Message.Contains("dangling.md", StringComparison.Ordinal));
        logger.Entries.ShouldContain(entry => entry.Level == LogLevel.Warning && entry.Message.Contains("a.md", StringComparison.Ordinal));
    }

    [Fact]
    [UnsupportedOSPlatform("windows")]
    public void Build_UnreadableFile_IsSkippedWithAWarning()
    {
        Assert.SkipWhen(OperatingSystem.IsWindows() || Environment.IsPrivilegedProcess, "Needs POSIX permissions and an unprivileged user.");
        using var temp = new TempDirectory();
        temp.Write("keep.md", "x");
        string secret = temp.Write("rules/secret.md", "hidden");
        File.SetUnixFileMode(secret, UnixFileMode.None);
        var logger = new CapturingLogger<IndexBuilder>();

        ConfigIndex index = Build(temp.Path, logger);

        Keys(index).ShouldBe(["keep.md"]);
        logger.Entries.ShouldContain(entry => entry.Level == LogLevel.Warning && entry.Message.Contains("rules/secret.md", StringComparison.Ordinal));
    }

    [Fact]
    [UnsupportedOSPlatform("windows")]
    public void Build_UnreadableDirectory_IsSkipped()
    {
        Assert.SkipWhen(OperatingSystem.IsWindows() || Environment.IsPrivilegedProcess, "Needs POSIX permissions and an unprivileged user.");
        using var temp = new TempDirectory();
        temp.Write("keep.md", "x");
        temp.Write("locked/a.md", "hidden");
        string locked = temp.Resolve("locked");
        File.SetUnixFileMode(locked, UnixFileMode.None);

        try
        {
            Keys(Build(temp.Path)).ShouldBe(["keep.md"]);
        }
        finally
        {
            File.SetUnixFileMode(locked, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }
    }

    [Fact]
    public void Build_NeverWritesToTheRoot()
    {
        using var temp = new TempDirectory();
        temp.Write("CLAUDE.md", "[a](rules/a.md)");
        temp.Write("rules/a.md", "x");
        temp.Write("settings.json", "{\"outputStyle\":\"x\"}");
        string[] before = Snapshot(temp.Path);

        _ = Build(temp.Path);

        Snapshot(temp.Path).ShouldBe(before);

        static string[] Snapshot(string root) =>
        [
            .. Directory.EnumerateFileSystemEntries(root, "*", SearchOption.AllDirectories)
                .Order(StringComparer.Ordinal)
                .Select(path => $"{Path.GetRelativePath(root, path)}|{(File.Exists(path) ? File.GetLastWriteTimeUtc(path).Ticks + File.ReadAllText(path) : "dir")}"),
        ];
    }

    [Fact]
    public void Build_ReturnedCollections_AreImmutableSnapshots()
    {
        using var temp = new TempDirectory();
        temp.Write("a.md", "[[b]]");
        temp.Write("b.md", "x");

        ConfigIndex index = Build(temp.Path);

        index.Files.ShouldBeAssignableTo<ImmutableSortedDictionary<string, ConfigFile>>();
        index.Backlinks.ShouldBeAssignableTo<ImmutableSortedDictionary<string, IReadOnlyList<Backlink>>>();
    }
}
