using System.Runtime.Versioning;
using Microsoft.Extensions.Logging;
using Pusula.Startup;
using Pusula.UnitTests.Support;
using Shouldly;
using Xunit;

namespace Pusula.UnitTests.Startup;

public sealed class InstanceAddressTests
{
    private const string Address = "http://127.0.0.1:41234/";
    private const UnixFileMode Private = UnixFileMode.UserRead | UnixFileMode.UserWrite;
    private const UnixFileMode PrivateFolder = UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute;

    private static string FolderOf(TempDirectory state) => Path.Join(state.Path, "pusula");

    private static string FileOf(TempDirectory state) => Path.Join(FolderOf(state), InstanceAddress.FileName);

    private static void NeedsUnix() => Assert.SkipWhen(OperatingSystem.IsWindows(), "Needs the file modes of Unix.");

    private static bool Write(TempDirectory state, string address = Address, string[]? shown = null) =>
        InstanceAddress.Write(state.Path, address, shown ?? [], new CapturingLogger<object>());

    private static string Place(TempDirectory state, string content, UnixFileMode fileMode = Private, UnixFileMode folderMode = PrivateFolder)
    {
        string file = state.Write($"pusula/{InstanceAddress.FileName}", content);
        if (!OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(file, fileMode);
            File.SetUnixFileMode(FolderOf(state), folderMode);
        }

        return file;
    }

    // ---- Writing -----------------------------------------------------------------------------------------------------

    [Fact]
    public void Write_NothingThereYet_MakesOneLineAndReadGivesItBack()
    {
        using var state = new TempDirectory();

        Write(state).ShouldBeTrue();

        File.ReadAllText(FileOf(state)).ShouldBe(Address + "\n");
        InstanceAddress.Read(state.Path).ShouldBe(Address);
        Directory.GetFiles(FolderOf(state)).Select(Path.GetFileName).ShouldBe([InstanceAddress.FileName]);
    }

    [Fact]
    [UnsupportedOSPlatform("windows")]
    public void Write_MakesTheFolderAndTheFileForTheUserAlone()
    {
        NeedsUnix();
        using var state = new TempDirectory();

        Write(state).ShouldBeTrue();

        File.GetUnixFileMode(FolderOf(state)).ShouldBe(PrivateFolder);
        File.GetUnixFileMode(FileOf(state)).ShouldBe(Private);
    }

    [Fact]
    public void Write_AnAddressThereAlready_IsReplacedByTheNewest()
    {
        using var state = new TempDirectory();
        Place(state, "http://127.0.0.1:1111/\n");

        Write(state).ShouldBeTrue();

        InstanceAddress.Read(state.Path).ShouldBe(Address);
    }

    [Theory]
    [InlineData("")]
    [InlineData("http://127.0.0.1:41234")]
    [InlineData("ftp://127.0.0.1/")]
    [InlineData("127.0.0.1:41234/")]
    [InlineData("http://127.0.0.1:41234/x/")]
    [InlineData("http://127.0.0.1:41234/?a=b/")]
    [InlineData("http://user@127.0.0.1:41234/")]
    [InlineData("http://127.0.0.1:41234/ \n/")]
    [InlineData("http://192.168.1.20:5190/")]
    [InlineData("http://192.0.2.7:5190/")]
    [InlineData("http://198.51.100.9:5190/")]
    [InlineData("http://my-laptop:5190/")]
    [InlineData("http://my-laptop.tail1234.ts.net:5190/")]
    [InlineData("http://0.0.0.0:5190/")]
    public void Write_ThatIsNotAnAddressOfThePage_WritesNothing(string address)
    {
        using var state = new TempDirectory();

        Write(state, address).ShouldBeFalse();

        Directory.Exists(FolderOf(state)).ShouldBeFalse();
    }

    [Fact]
    public void Write_FolderOfAShownSource_WritesNothingAndSaysSo()
    {
        using var state = new TempDirectory();
        var logger = new CapturingLogger<object>();

        InstanceAddress.Write(state.Path, Address, [state.Path], logger).ShouldBeFalse();

        Directory.Exists(FolderOf(state)).ShouldBeFalse();
        logger.Entries.ShouldContain(entry => entry.Message.Contains("shown", StringComparison.Ordinal));
    }

    [Fact]
    [UnsupportedOSPlatform("windows")]
    public void Write_FolderThatOthersCanWriteTo_WritesNothing()
    {
        NeedsUnix();
        using var state = new TempDirectory();
        Directory.CreateDirectory(FolderOf(state));
        File.SetUnixFileMode(FolderOf(state), PrivateFolder | UnixFileMode.OtherWrite);

        Write(state).ShouldBeFalse();

        Directory.GetFileSystemEntries(FolderOf(state)).ShouldBeEmpty();
    }

    [Fact]
    [UnsupportedOSPlatform("windows")]
    public void Write_FolderThatIsALink_WritesNothingThroughIt()
    {
        NeedsUnix();
        using var state = new TempDirectory();
        using var elsewhere = new TempDirectory();
        Directory.CreateSymbolicLink(FolderOf(state), elsewhere.Path);

        Write(state).ShouldBeFalse();

        Directory.GetFileSystemEntries(elsewhere.Path).ShouldBeEmpty();
    }

    // ---- Reading -----------------------------------------------------------------------------------------------------

    [Fact]
    public void Read_NoFile_IsNull()
    {
        using var state = new TempDirectory();

        InstanceAddress.Read(state.Path).ShouldBeNull();
        InstanceAddress.Read(string.Empty).ShouldBeNull();
        Directory.GetFileSystemEntries(state.Path).ShouldBeEmpty();
    }

    [Theory]
    [InlineData("http://127.0.0.1:41234/", "http://127.0.0.1:41234/")]
    [InlineData("http://127.0.0.1:41234/\n", "http://127.0.0.1:41234/")]
    [InlineData("https://localhost:5191/\r\n", "https://localhost:5191/")]
    [InlineData("http://[::1]:5190/\n", "http://[::1]:5190/")]
    [InlineData("http://192.168.1.20:5190/\n", null)]
    [InlineData("http://192.0.2.7:5190/\n", null)]
    [InlineData("http://198.51.100.9:5190/\n", null)]
    [InlineData("http://my-laptop:5190/\n", null)]
    [InlineData("http://[2001:db8::1]:5190/\n", null)]
    [InlineData("http://127.0.0.1:41234/\n\n", null)]
    [InlineData("http://127.0.0.1:41234/\nhttp://127.0.0.1:1/\n", null)]
    [InlineData(" http://127.0.0.1:41234/\n", null)]
    [InlineData("http://127.0.0.1:41234\n", null)]
    [InlineData("javascript:alert(1)//", null)]
    [InlineData("file:///etc/passwd/", null)]
    [InlineData("\n", null)]
    [InlineData("not an address\n", null)]
    public void Read_ByContent(string content, string? expected)
    {
        using var state = new TempDirectory();
        Place(state, content);

        InstanceAddress.Read(state.Path).ShouldBe(expected);
    }

    // A valid address, so that only the size decides: the file is 24 bytes ("\n" included).
    [Theory]
    [InlineData(24, true)]
    [InlineData(23, false)]
    public void Read_ValidAddressAtTheSizeCap_IsReadOnlyWithinIt(int maxBytes, bool read)
    {
        using var state = new TempDirectory();
        Place(state, Address + "\n");
        new FileInfo(FileOf(state)).Length.ShouldBe(24);

        InstanceAddress.Read(state.Path, maxBytes).ShouldBe(read ? Address : null);
    }

    [Fact]
    public void Read_CapInTheApplication_IsFiveHundredAndTwelveBytes() => InstanceAddress.MaxFileBytes.ShouldBe(512);

    [Fact]
    public void Read_EmptyFile_IsNull()
    {
        using var state = new TempDirectory();
        Place(state, string.Empty);

        InstanceAddress.Read(state.Path).ShouldBeNull();
    }

    [Fact]
    [UnsupportedOSPlatform("windows")]
    public void Read_FileThatOthersCanRead_IsNull()
    {
        NeedsUnix();
        using var state = new TempDirectory();
        Place(state, Address + "\n", fileMode: Private | UnixFileMode.OtherRead);

        InstanceAddress.Read(state.Path).ShouldBeNull();
    }

    [Fact]
    [UnsupportedOSPlatform("windows")]
    public void Read_FolderThatOthersCanWriteTo_IsNull()
    {
        NeedsUnix();
        using var state = new TempDirectory();
        Place(state, Address + "\n", folderMode: PrivateFolder | UnixFileMode.GroupWrite);

        InstanceAddress.Read(state.Path).ShouldBeNull();
    }

    [Fact]
    [UnsupportedOSPlatform("windows")]
    public void Read_FileThatIsALink_IsNull()
    {
        NeedsUnix();
        using var state = new TempDirectory();
        using var elsewhere = new TempDirectory();
        string real = elsewhere.Write("real", Address + "\n");
        File.SetUnixFileMode(real, Private);
        Directory.CreateDirectory(FolderOf(state));
        File.SetUnixFileMode(FolderOf(state), PrivateFolder);
        File.CreateSymbolicLink(FileOf(state), real);

        InstanceAddress.Read(state.Path).ShouldBeNull();
    }

    [Fact]
    [UnsupportedOSPlatform("windows")]
    public void Read_FolderThatIsALink_IsNull()
    {
        NeedsUnix();
        using var state = new TempDirectory();
        using var elsewhere = new TempDirectory();
        string file = elsewhere.Write(InstanceAddress.FileName, Address + "\n");
        File.SetUnixFileMode(file, Private);
        File.SetUnixFileMode(elsewhere.Path, PrivateFolder);
        Directory.CreateSymbolicLink(FolderOf(state), elsewhere.Path);

        InstanceAddress.Read(state.Path).ShouldBeNull();
    }

    // ---- Stopping ----------------------------------------------------------------------------------------------------

    [Fact]
    public void DeleteIfOurs_ContentIsOurAddress_DeletesTheFile()
    {
        using var state = new TempDirectory();
        Write(state).ShouldBeTrue();

        InstanceAddress.DeleteIfOurs(state.Path, Address);

        File.Exists(FileOf(state)).ShouldBeFalse();
    }

    [Fact]
    public void DeleteIfOurs_ANewerPusulaWroteItsOwn_LeavesTheFile()
    {
        using var state = new TempDirectory();
        Write(state, "http://127.0.0.1:2222/").ShouldBeTrue();

        InstanceAddress.DeleteIfOurs(state.Path, Address);

        InstanceAddress.Read(state.Path).ShouldBe("http://127.0.0.1:2222/");
    }

    [Fact]
    public void DeleteIfOurs_NoFile_DoesNothing()
    {
        using var state = new TempDirectory();

        Should.NotThrow(() => InstanceAddress.DeleteIfOurs(state.Path, Address));
        InstanceAddress.DeleteIfOurs(string.Empty, Address);
    }

    [Theory]
    [InlineData("http://127.0.0.1:5190/")]
    [InlineData("https://127.0.0.1:5191/")]
    [InlineData("http://localhost:5190/")]
    [InlineData("http://[::1]:5190/")]
    public void Write_LoopbackAddress_IsRecordedAndRead(string address)
    {
        using var state = new TempDirectory();

        Write(state, address).ShouldBeTrue();

        InstanceAddress.Read(state.Path).ShouldBe(address);
    }

    [Fact]
    public void Write_LanAddress_WritesNothing()
    {
        using var state = new TempDirectory();

        Write(state, "http://192.168.1.20:5190/").ShouldBeFalse();

        Directory.Exists(FolderOf(state)).ShouldBeFalse();
    }

    // browser opens, urls default, folders on the command line -> record / look up
    [Theory]
    [InlineData(true, false, true)]
    [InlineData(true, true, false)]
    [InlineData(false, false, false)]
    [InlineData(false, true, false)]
    public void ShouldRecord_NeedsTheBrowserAndNoFolders(bool openBrowser, bool folders, bool expected) =>
        InstanceAddress.ShouldRecord(openBrowser, folders).ShouldBe(expected);

    [Theory]
    [InlineData(true, true, false, true)]
    [InlineData(true, false, false, false)]
    [InlineData(true, true, true, false)]
    [InlineData(false, true, false, false)]
    public void ShouldLookUp_NeedsTheBrowserTheDefaultAddressAndNoFolders(bool openBrowser, bool urlsAreDefault, bool folders, bool expected) =>
        InstanceAddress.ShouldLookUp(openBrowser, urlsAreDefault, folders).ShouldBe(expected);

    [Fact]
    public void Parse_IsTheOneRuleOfReadAndWrite() => InstanceAddress.Parse(Address).ShouldBe(Address);
}
