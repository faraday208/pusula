using Pusula.Sources;
using Shouldly;
using Xunit;

namespace Pusula.UnitTests.Sources;

public sealed class SourceIdsTests
{
    [Theory]
    [InlineData(".claude", "claude")]
    [InlineData("Not Defteri", "not-defteri")]
    [InlineData("Çalışma Şablonları", "calisma-sablonlari")]
    [InlineData("ĞÜŞİÖÇ ğüşıöç", "gusioc-gusioc")]
    [InlineData("  my--vault__v2!!  ", "my-vault-v2")]
    [InlineData("a.b.c", "a-b-c")]
    [InlineData("Project 2026", "project-2026")]
    [InlineData("Café", "caf")]
    [InlineData("---", "source")]
    [InlineData("", "source")]
    [InlineData("日本語", "source")]
    public void Derive_Name_GivesLowercaseAsciiWordsJoinedBySingleHyphens(string name, string expected) =>
        SourceIds.Derive(name).ShouldBe(expected);

    [Fact]
    public void Derive_LongName_IsCutAtFortyCharactersWithoutATrailingHyphen()
    {
        string id = SourceIds.Derive(new string('a', 39) + " tail");

        id.ShouldBe(new string('a', 39));
        SourceIds.Derive(new string('b', 100)).Length.ShouldBe(40);
    }

    [Fact]
    public void MakeUnique_TakenId_GetsTheNextFreeNumber()
    {
        var taken = new HashSet<string> { "notes", "notes-2" };

        SourceIds.MakeUnique("notes", taken).ShouldBe("notes-3");
        SourceIds.MakeUnique("fresh", taken).ShouldBe("fresh");
        taken.ShouldBe(["notes", "notes-2", "notes-3", "fresh"], ignoreOrder: true);
    }

    [Fact]
    public void MakeUnique_IdOfFullLength_StaysWithinTheLimitWithItsSuffix()
    {
        string id = new('a', SourceIds.MaxLength);
        var taken = new HashSet<string> { id };

        string second = SourceIds.MakeUnique(id, taken);

        second.Length.ShouldBe(SourceIds.MaxLength);
        second.ShouldEndWith("-2");
    }

    [Theory]
    [InlineData("claude", true)]
    [InlineData("a-b-1", true)]
    [InlineData("2026", true)]
    [InlineData("", false)]
    [InlineData("-a", false)]
    [InlineData("a-", false)]
    [InlineData("a--b", false)]
    [InlineData("Claude", false)]
    [InlineData("a_b", false)]
    [InlineData("a b", false)]
    [InlineData("ı", false)]
    public void IsValid_Id_AllowsLowercaseLettersAndDigitsJoinedBySingleHyphens(string id, bool expected) =>
        SourceIds.IsValid(id).ShouldBe(expected);

    [Theory]
    [InlineData("/home/u/.claude", ".claude")]
    [InlineData("/home/u/Documents/My Vault/", "My Vault")]
    [InlineData("/", "/")]
    public void NameOf_Folder_IsItsLastSegmentOrTheWholePathOfARootDirectory(string path, string expected) =>
        SourceIds.NameOf(path).ShouldBe(expected);
}
