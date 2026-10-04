using System.Globalization;
using Pusula.Browse;
using Pusula.UnitTests.Support;
using Shouldly;
using Xunit;

namespace Pusula.UnitTests.Browse;

public sealed class FolderNamesTests
{
    private static readonly string[] TurkishNames = ["Zeytin", "Çiçek", "Şeker", "ankara", "Ömür", "Bursa", "İzmir", "ığdır", "ağaç", "Ağrı", "Irmak"];

    private static readonly string[] WithADot = ["zeta", "9lives", ".claude", "Alpha"];

    [Theory]
    [InlineData(".git", true)]
    [InlineData(".claude", true)]
    [InlineData(".obsidian", true)]
    [InlineData("..hidden", true)]
    [InlineData(".", true)]
    [InlineData("notes", false)]
    [InlineData("my.notes", false)]
    [InlineData("notes.", false)]
    [InlineData("", false)]
    public void IsHidden_Name_IsHiddenWhenItStartsWithADot(string name, bool expected) =>
        FolderNames.IsHidden(name).ShouldBe(expected);

    [Theory]
    [InlineData(FileAttributes.Hidden, true)]
    [InlineData(FileAttributes.System, true)]
    [InlineData(FileAttributes.Hidden | FileAttributes.System, true)]
    [InlineData(FileAttributes.Directory | FileAttributes.Hidden | FileAttributes.ReparsePoint, true)]
    [InlineData(FileAttributes.Directory | FileAttributes.System | FileAttributes.ReadOnly, true)]
    [InlineData(FileAttributes.Directory, false)]
    [InlineData(FileAttributes.Directory | FileAttributes.ReadOnly | FileAttributes.Archive, false)]
    [InlineData(FileAttributes.Directory | FileAttributes.ReparsePoint, false)]
    [InlineData(FileAttributes.Normal, false)]
    public void HasHiddenAttribute_Attributes_AreHiddenWhenTheyIncludeHiddenOrSystem(FileAttributes attributes, bool expected) =>
        FolderNames.HasHiddenAttribute(attributes).ShouldBe(expected);

    [Fact]
    public void IsHidden_FolderWithADotAtTheStart_IsHiddenOnEverySystem()
    {
        using var temp = new TempDirectory();

        FolderNames.IsHidden(new DirectoryInfo(temp.CreateDirectory(".config"))).ShouldBeTrue();
        FolderNames.IsHidden(new DirectoryInfo(temp.CreateDirectory("config"))).ShouldBeFalse();
    }

    [Theory]
    [InlineData(FileAttributes.Hidden)]
    [InlineData(FileAttributes.System)]
    [InlineData(FileAttributes.Hidden | FileAttributes.System)]
    public void IsHidden_FolderWithTheHiddenOrSystemAttributeOnWindows_IsHidden(FileAttributes attributes)
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "Needs the attributes of Windows.");
        using var temp = new TempDirectory();
        string plain = temp.CreateDirectory("Plain");
        string marked = temp.CreateDirectory("AppData");
        File.SetAttributes(marked, attributes);

        FolderNames.IsHidden(new DirectoryInfo(marked)).ShouldBeTrue();
        FolderNames.IsHidden(new DirectoryInfo(plain)).ShouldBeFalse();
    }

    [Fact]
    public void IsHidden_FolderThatTheSystemMarksHiddenWithoutADotOutsideWindows_IsNotHidden()
    {
        Assert.SkipWhen(OperatingSystem.IsWindows(), "Windows hides a folder by its attributes.");
        using var temp = new TempDirectory();
        string folder = temp.CreateDirectory("Library");
        try
        {
            // macOS has a hidden flag, and it is set here; elsewhere there is nothing to set and the call changes nothing.
            File.SetAttributes(folder, FileAttributes.Hidden);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException or PlatformNotSupportedException)
        {
            Assert.Skip($"The hidden flag cannot be set here: {exception.Message}");
        }

        FolderNames.IsHidden(new DirectoryInfo(folder)).ShouldBeFalse();
    }

    [Theory]
    [InlineData(".git", true)]
    [InlineData(".obsidian", true)]
    [InlineData("node_modules", true)]
    [InlineData("bin", true)]
    [InlineData("obj", true)]
    [InlineData("notes", false)]
    [InlineData("binaries", false)]
    [InlineData("object", false)]
    [InlineData("my-obj", false)]
    [InlineData("node_modules_old", false)]
    public void IsLeftOutOfSearch_Folder_IsLeftOutWhenItIsHiddenOrHoldsWhatIsBuiltOrFetched(string name, bool expected)
    {
        using var temp = new TempDirectory();

        FolderNames.IsLeftOutOfSearch(new DirectoryInfo(temp.CreateDirectory(name))).ShouldBe(expected);
    }

    [Fact]
    public void IsLeftOutOfSearch_NameThatDiffersInCase_IsLeftOutWhereThePlatformComparesPathsIgnoringCase()
    {
        using var temp = new TempDirectory();
        var folder = new DirectoryInfo(temp.CreateDirectory("BIN"));

        // Windows compares paths ignoring case, the others do not: BIN is not bin there.
        FolderNames.IsLeftOutOfSearch(folder).ShouldBe(OperatingSystem.IsWindows());
    }

    [Fact]
    public void IsNodeModules_OnlyThatName()
    {
        FolderNames.IsNodeModules("node_modules").ShouldBeTrue();
        FolderNames.IsNodeModules("node_modules2").ShouldBeFalse();
        FolderNames.IsNodeModules("my_node_modules").ShouldBeFalse();
        FolderNames.IsNodeModules("node-modules").ShouldBeFalse();
        FolderNames.IsNodeModules(".node_modules").ShouldBeFalse();
    }

    [Fact]
    public void IsClaudeFolder_OnlyThatName()
    {
        FolderNames.IsClaudeFolder(".claude").ShouldBeTrue();
        FolderNames.IsClaudeFolder("claude").ShouldBeFalse();
        FolderNames.IsClaudeFolder(".claude.bak").ShouldBeFalse();
    }

    // The Turkish alphabet: a b c ç d e f g ğ h ı i j k l m n o ö p r s ş t u ü v y z. Ordinal order would put every
    // name that starts with a capital letter before one that starts with a small letter, and ç ğ ı ö ş ü after z.
    [Fact]
    public void Compare_Names_FollowTheTurkishAlphabetIgnoringCase()
    {
        string[] sorted = [.. TurkishNames.Order(Comparer<string>.Create(FolderNames.Compare))];

        sorted.ShouldBe(["ağaç", "Ağrı", "ankara", "Bursa", "Çiçek", "ığdır", "Irmak", "İzmir", "Ömür", "Şeker", "Zeytin"]);
    }

    [Fact]
    public void CompareIgnoringCase_Names_FollowTheTurkishAlphabetAndCaseDoesNotMatter()
    {
        string[] sorted = [.. TurkishNames.Order(Comparer<string>.Create(FolderNames.CompareIgnoringCase))];

        sorted.ShouldBe(["ağaç", "Ağrı", "ankara", "Bursa", "Çiçek", "ığdır", "Irmak", "İzmir", "Ömür", "Şeker", "Zeytin"]);
        FolderNames.CompareIgnoringCase("Notes", "notes").ShouldBe(0);
        FolderNames.CompareIgnoringCase("IRMAK", "ırmak").ShouldBe(0);
        FolderNames.CompareIgnoringCase("alpha", "Beta").ShouldBeLessThan(0);
    }

    [Fact]
    public void Compare_NamesThatDifferOnlyInCase_AreNotEqualAndKeepAFixedOrder()
    {
        int forward = FolderNames.Compare("Notes", "notes");
        int backward = FolderNames.Compare("notes", "Notes");

        forward.ShouldNotBe(0);
        Math.Sign(forward).ShouldBe(-Math.Sign(backward));
    }

    [Fact]
    public void FindTurkish_RuntimeWithTheTurkishCulture_IsItsCompareInfo()
    {
        CompareInfo turkish = FolderNames.FindTurkish(CultureInfo.GetCultureInfo);

        turkish.Compare("ı", "i", CompareOptions.IgnoreCase).ShouldBeLessThan(0);
        turkish.ShouldBe(CultureInfo.GetCultureInfo("tr-TR").CompareInfo);
    }

    [Fact]
    public void FindTurkish_RuntimeWithoutTheTurkishCulture_IsTheInvariantCompareInfo()
    {
        CompareInfo fallback = FolderNames.FindTurkish(name => throw new CultureNotFoundException(nameof(name), name, "No such culture."));

        fallback.ShouldBe(CultureInfo.InvariantCulture.CompareInfo);
    }

    [Fact]
    public void Compare_SameName_IsZero() =>
        FolderNames.Compare("Belgeler", "Belgeler").ShouldBe(0);

    [Fact]
    public void Compare_ADotAtTheStart_ComesBeforeLettersAndDigits()
    {
        string[] sorted = [.. WithADot.Order(Comparer<string>.Create(FolderNames.Compare))];

        sorted.ShouldBe([".claude", "9lives", "Alpha", "zeta"]);
    }
}
