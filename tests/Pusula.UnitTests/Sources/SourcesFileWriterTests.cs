using System.Text;
using Pusula.Sources;
using Pusula.UnitTests.Support;
using Shouldly;
using Xunit;

namespace Pusula.UnitTests.Sources;

public sealed class SourcesFileWriterTests
{
    [Fact]
    public void Write_FolderThatDoesNotExist_CreatesItAndTheFile()
    {
        using var temp = new TempDirectory();
        string file = temp.Resolve("config/pusula/sources.json");

        SourcesFileWriter.Write(file, "{ \"sources\": [] }\n");

        File.ReadAllText(file).ShouldBe("{ \"sources\": [] }\n");
    }

    [Fact]
    public void Write_ExistingFile_ReplacesItWholeAndLeavesNothingElseInTheFolder()
    {
        using var temp = new TempDirectory();
        string file = temp.Write("sources.json", new string('x', 10_000));

        SourcesFileWriter.Write(file, "short");

        File.ReadAllText(file).ShouldBe("short");
        Directory.GetFileSystemEntries(temp.Path).ShouldBe([file]);
    }

    [Fact]
    public void Write_Text_IsUtf8WithoutAByteOrderMark()
    {
        using var temp = new TempDirectory();
        string file = temp.Resolve("sources.json");

        SourcesFileWriter.Write(file, "Notlarım");

        File.ReadAllBytes(file).ShouldBe(new UTF8Encoding(encoderShouldEmitUTF8Identifier: false).GetBytes("Notlarım"));
    }

    [Fact]
    public void Write_FileThatCannotBeReplaced_ThrowsAndLeavesNothingBehind()
    {
        using var temp = new TempDirectory();

        // A folder where the file should be: the new file cannot take its place.
        string file = temp.CreateDirectory("sources.json");
        temp.Write("sources.json/keep.txt", "x");

        Should.Throw<IOException>(() => SourcesFileWriter.Write(file, "text"));

        Directory.GetFileSystemEntries(temp.Path).ShouldBe([file]);
        File.ReadAllText(Path.Join(file, "keep.txt")).ShouldBe("x");
    }

    [Fact]
    public void Write_FileThatIsALink_WritesThroughItAndTheLinkStaysALink()
    {
        Assert.SkipWhen(OperatingSystem.IsWindows(), "Creating a symbolic link needs a privilege on Windows.");
        using var temp = new TempDirectory();
        string target = temp.Write("dotfiles/pusula/sources.json", "old");
        string link = temp.Resolve("config/sources.json");
        Directory.CreateDirectory(Path.GetDirectoryName(link)!);
        File.CreateSymbolicLink(link, target);

        SourcesFileWriter.Write(link, "new");

        new FileInfo(link).LinkTarget.ShouldBe(target);
        File.ReadAllText(target).ShouldBe("new");
        File.ReadAllText(link).ShouldBe("new");
        Directory.GetFileSystemEntries(temp.Resolve("dotfiles/pusula")).ShouldBe([target]);
        Directory.GetFileSystemEntries(temp.Resolve("config")).ShouldBe([link]);
    }
}
