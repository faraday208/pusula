using System.Globalization;
using Pusula.Browse;
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
