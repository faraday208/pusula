using Microsoft.Extensions.Logging.Abstractions;
using Pusula.Indexing;
using Pusula.UnitTests.Support;
using Shouldly;
using Xunit;

namespace Pusula.UnitTests.Indexing;

// The limits are small here: a folder of 20,000 files is not made to see that the 20,001st is one too many.
public sealed class ScanLimitsTests
{
    private const long Mebibyte = 1024 * 1024;

    private static ScanLimits Files(int max) => new(max, MaxDirectories: 1_000, MaxBytes: 100 * Mebibyte);

    private static ScanLimits Directories(int max) => new(MaxFiles: 1_000, max, MaxBytes: 100 * Mebibyte);

    private static ScanLimits Bytes(long max) => new(MaxFiles: 1_000, MaxDirectories: 1_000, max);

    private static ConfigIndex Build(TempDirectory folder, ScanLimits limits, SourceProfile profile) =>
        new IndexBuilder(NullLogger<IndexBuilder>.Instance) { Limits = limits }.Build(folder.Path, profile);

    private static FolderTooLargeException BuildTooLarge(TempDirectory folder, ScanLimits limits, SourceProfile profile) =>
        Should.Throw<FolderTooLargeException>(() => Build(folder, limits, profile));

    // ---- What the limits are ------------------------------------------------------------------------------------

    [Fact]
    public void Default_Limits_AreTwentyThousandFilesFiftyThousandFoldersAnd256MebibytesOfMarkdown()
    {
        ScanLimits.Default.MaxFiles.ShouldBe(20_000);
        ScanLimits.Default.MaxDirectories.ShouldBe(50_000);
        ScanLimits.Default.MaxBytes.ShouldBe(256 * Mebibyte);
        new IndexBuilder(NullLogger<IndexBuilder>.Instance).Limits.ShouldBe(ScanLimits.Default);
    }

    [Fact]
    public void Message_OfEachLimit_IsASentenceThatSaysWhichOneWasBeyond()
    {
        FolderTooLargeException.ForFiles("/f", ScanLimits.Default.MaxFiles).Message
            .ShouldBe("The folder is too large to show: more than 20,000 Markdown files.");
        FolderTooLargeException.ForDirectories("/f", ScanLimits.Default.MaxDirectories).Message
            .ShouldBe("The folder is too large to show: more than 50,000 folders.");
        FolderTooLargeException.ForBytes("/f", ScanLimits.Default.MaxBytes).Message
            .ShouldBe("The folder is too large to show: more than 256 MiB of Markdown.");
        FolderTooLargeException.ForBytes("/f", 1_500).Message
            .ShouldBe("The folder is too large to show: more than 1,500 bytes of Markdown.");
        FolderTooLargeException.ForBytes("/f", 3 * Mebibyte).Message
            .ShouldBe("The folder is too large to show: more than 3 MiB of Markdown.");
        FolderTooLargeException.ForFiles("/the/folder", 5).Folder.ShouldBe("/the/folder");
    }

    // ---- Markdown files -----------------------------------------------------------------------------------------

    [Theory]
    [InlineData(SourceProfile.Claude)]
    [InlineData(SourceProfile.Vault)]
    [InlineData(SourceProfile.Markdown)]
    public void Build_MoreMarkdownFilesThanTheLimit_StopsWithTheFolderAndTheLimitInTheMessage(SourceProfile profile)
    {
        using var folder = new TempDirectory();
        for (int i = 1; i <= 4; i++)
        {
            folder.Write($"note{i}.md", $"# {i}\n");
        }

        FolderTooLargeException exception = BuildTooLarge(folder, Files(3), profile);

        exception.Message.ShouldBe("The folder is too large to show: more than 3 Markdown files.");
        exception.Folder.ShouldBe(folder.Path);
    }

    [Theory]
    [InlineData(SourceProfile.Claude)]
    [InlineData(SourceProfile.Vault)]
    [InlineData(SourceProfile.Markdown)]
    public void Build_ExactlyAsManyMarkdownFilesAsTheLimit_IsFine(SourceProfile profile)
    {
        using var folder = new TempDirectory();
        for (int i = 1; i <= 3; i++)
        {
            folder.Write($"note{i}.md", $"# {i}\n");
        }

        Build(folder, Files(3), profile).Files.Count.ShouldBe(3);
    }

    [Fact]
    public void Build_MarkdownFilesInFolders_AreCountedAcrossTheWholeTree()
    {
        using var folder = new TempDirectory();
        folder.Write("a.md", "# a\n");
        folder.Write("b.md", "# b\n");
        folder.Write("sub/deep/c.md", "# c\n");
        folder.Write("sub/deep/d.md", "# d\n");

        BuildTooLarge(folder, Files(3), SourceProfile.Markdown).Message.ShouldContain("more than 3 Markdown files");
        Build(folder, Files(4), SourceProfile.Markdown).Files.Count.ShouldBe(4);
    }

    [Fact]
    public void Build_WhatIsNotMarkdownOrIsNotScanned_DoesNotCountAsAFile()
    {
        using var folder = new TempDirectory();
        folder.Write("a.md", "# a\n");
        folder.Write("b.md", "# b\n");
        folder.Write("c.md", "# c\n");
        for (int i = 0; i < 20; i++)
        {
            folder.Write($"other{i}.txt", "x");
            folder.Write($"image{i}.png", "x");
        }

        folder.Write(".hidden.md", "# hidden\n");
        folder.Write(".git/hook.md", "# hidden folder\n");
        folder.Write("node_modules/package/readme.md", "# dependency\n");

        Build(folder, Files(3), SourceProfile.Markdown).Files.Count.ShouldBe(3);
    }

    [Fact]
    public void Build_ClaudeFolder_DoesNotCountTheRuntimeFoldersItNeverEnters()
    {
        using var folder = new TempDirectory();
        folder.Write("CLAUDE.md", "# c\n");
        folder.Write("rules/a.md", "# a\n");
        for (int i = 0; i < 20; i++)
        {
            folder.Write($"plugins/cache/p{i}.md", "# plugin\n");
            folder.Write($"sessions/s{i}.md", "# session\n");
        }

        Build(folder, Files(2), SourceProfile.Claude).Files.Count.ShouldBe(2);
    }

    [Fact]
    public void Build_MarkdownFileTooLargeToRead_CountsAsAFileButNotAsBytes()
    {
        using var folder = new TempDirectory();
        folder.Write("small1.md", new string('a', 100));
        folder.Write("small2.md", new string('b', 100));
        folder.Write("huge.md", new string('c', (int)IndexBuilder.MaxFileBytes + 1));

        // Three files come across, so that is too many for a limit of two; the huge one is not read, so it adds no bytes.
        BuildTooLarge(folder, Files(2), SourceProfile.Markdown).Message.ShouldContain("more than 2 Markdown files");
        Build(folder, new ScanLimits(MaxFiles: 3, MaxDirectories: 10, MaxBytes: 250), SourceProfile.Markdown).Files.Count.ShouldBe(2);
    }

    // ---- Folders ------------------------------------------------------------------------------------------------

    [Theory]
    [InlineData(SourceProfile.Claude)]
    [InlineData(SourceProfile.Vault)]
    [InlineData(SourceProfile.Markdown)]
    public void Build_MoreFoldersThanTheLimit_StopsWithTheLimitInTheMessage(SourceProfile profile)
    {
        using var folder = new TempDirectory();
        for (int i = 1; i <= 4; i++)
        {
            folder.CreateDirectory($"folder{i}");
        }

        FolderTooLargeException exception = BuildTooLarge(folder, Directories(3), profile);

        exception.Message.ShouldBe("The folder is too large to show: more than 3 folders.");
        exception.Folder.ShouldBe(folder.Path);
        Build(folder, Directories(4), profile).Files.ShouldBeEmpty();
    }

    [Fact]
    public void Build_FoldersInsideFolders_AreAllCountedButTheRootIsNot()
    {
        using var folder = new TempDirectory();
        folder.CreateDirectory("a/b/c/d");

        BuildTooLarge(folder, Directories(3), SourceProfile.Markdown).Message.ShouldContain("more than 3 folders");
        Build(folder, Directories(4), SourceProfile.Markdown).Files.ShouldBeEmpty();

        using var flat = new TempDirectory();
        flat.Write("a.md", "# a\n");
        Build(flat, Directories(0), SourceProfile.Markdown).Files.Count.ShouldBe(1);
    }

    [Fact]
    public void Build_FoldersTheScanDoesNotEnter_DoNotCount()
    {
        using var folder = new TempDirectory();
        folder.CreateDirectory("one");
        folder.CreateDirectory(".git/objects/aa");
        folder.CreateDirectory(".obsidian/plugins/x");
        folder.CreateDirectory("node_modules/a/b/c");

        Build(folder, Directories(1), SourceProfile.Markdown).Files.ShouldBeEmpty();

        using var claude = new TempDirectory();
        claude.CreateDirectory("rules");
        claude.CreateDirectory("plugins/a/b/c");
        claude.CreateDirectory("cache/x/y");
        claude.CreateDirectory(".hidden/z");

        Build(claude, Directories(1), SourceProfile.Claude).Files.ShouldBeEmpty();
    }

    // ---- Bytes --------------------------------------------------------------------------------------------------

    [Theory]
    [InlineData(SourceProfile.Claude)]
    [InlineData(SourceProfile.Vault)]
    [InlineData(SourceProfile.Markdown)]
    public void Build_MoreMarkdownThanTheLimit_StopsWithTheLimitInTheMessage(SourceProfile profile)
    {
        using var folder = new TempDirectory();
        for (int i = 1; i <= 3; i++)
        {
            folder.Write($"note{i}.md", new string('x', 100));
        }

        FolderTooLargeException exception = BuildTooLarge(folder, Bytes(250), profile);

        exception.Message.ShouldBe("The folder is too large to show: more than 250 bytes of Markdown.");
        exception.Folder.ShouldBe(folder.Path);
        Build(folder, Bytes(300), profile).Files.Count.ShouldBe(3);
    }

    [Fact]
    public void Build_MoreMarkdownThanTheLimitInMebibytes_SaysSoInMebibytes()
    {
        using var folder = new TempDirectory();
        for (int i = 1; i <= 3; i++)
        {
            folder.Write($"big{i}.md", new string('x', (int)Mebibyte));
        }

        BuildTooLarge(folder, Bytes(2 * Mebibyte), SourceProfile.Markdown).Message
            .ShouldBe("The folder is too large to show: more than 2 MiB of Markdown.");
        Build(folder, Bytes(3 * Mebibyte), SourceProfile.Markdown).Files.Count.ShouldBe(3);
    }

    [Fact]
    public void Build_FolderThatWasTooLarge_LeavesNothingBehindForTheNextBuild()
    {
        using var folder = new TempDirectory();
        for (int i = 1; i <= 4; i++)
        {
            folder.Write($"note{i}.md", "# n\n");
        }

        var builder = new IndexBuilder(NullLogger<IndexBuilder>.Instance) { Limits = Files(3) };
        Should.Throw<FolderTooLargeException>(() => builder.Build(folder.Path, SourceProfile.Markdown));
        Should.Throw<FolderTooLargeException>(() => builder.Build(folder.Path, SourceProfile.Markdown));

        File.Delete(Path.Join(folder.Path, "note4.md"));
        builder.Build(folder.Path, SourceProfile.Markdown).Files.Count.ShouldBe(3);
    }
}
