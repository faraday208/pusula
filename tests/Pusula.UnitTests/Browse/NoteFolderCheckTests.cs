using Pusula.Browse;
using Shouldly;
using Xunit;

namespace Pusula.UnitTests.Browse;

// The rules that make a folder a folder of notes, as numbers: the file system is not touched here.
public sealed class NoteFolderCheckTests
{
    [Theory]
    [InlineData("Home.md")]
    [InlineData("index.md")]
    [InlineData("README.md")]
    [InlineData("MOC.md")]
    [InlineData("_index.md")]
    [InlineData("start.md")]
    [InlineData("home.md")]
    [InlineData("HOME.MD")]
    [InlineData("Index.md")]
    [InlineData("readme.md")]
    [InlineData("Readme.MD")]
    [InlineData("moc.md")]
    [InlineData("_INDEX.md")]
    [InlineData("Start.md")]
    public void IsEntryNote_TheSixNamesInAnyCase_AreEntryNotes(string fileName) =>
        NoteFolderCheck.IsEntryNote(fileName).ShouldBeTrue();

    [Theory]
    [InlineData("")]
    [InlineData("README")]
    [InlineData("README.txt")]
    [InlineData("README.md.bak")]
    [InlineData("Home.markdown")]
    [InlineData("homepage.md")]
    [InlineData("index.html")]
    [InlineData("my-index.md")]
    [InlineData("index2.md")]
    [InlineData("notes.md")]
    [InlineData("start-here.md")]
    [InlineData("_home.md")]
    [InlineData(" README.md")]
    public void IsEntryNote_AnyOtherName_IsNot(string fileName) =>
        NoteFolderCheck.IsEntryNote(fileName).ShouldBeFalse();

    // Turkish culture must not decide what is the same name: ordinal, ignoring case. (The dotless i of "ındex" is another letter.)
    [Fact]
    public void IsEntryNote_NameWithTheTurkishDotlessI_IsNotTheSameName() =>
        NoteFolderCheck.IsEntryNote("ındex.md").ShouldBeFalse();

    [Theory]
    [InlineData(9, 9, false)]
    [InlineData(10, 10, true)]
    [InlineData(10, 20, true)]
    [InlineData(10, 21, false)]
    [InlineData(24, 50, false)]
    [InlineData(25, 50, true)]
    [InlineData(1000, 1000, true)]
    [InlineData(0, 0, false)]
    public void HasEnoughNotes_NotesAndFiles_NeedTenNotesThatAreHalfOfTheFiles(int notes, int files, bool expected) =>
        new NoteFolderCheck(notes, files, Sampled: 0, Linked: 0).HasEnoughNotes.ShouldBe(expected);

    [Theory]
    [InlineData(20, 3, false)]
    [InlineData(20, 4, true)]
    [InlineData(10, 1, false)]
    [InlineData(10, 2, true)]
    [InlineData(15, 2, false)]
    [InlineData(15, 3, true)]
    [InlineData(1, 0, false)]
    [InlineData(1, 1, true)]
    [InlineData(20, 0, false)]
    [InlineData(20, 20, true)]
    public void IsFolderOfNotes_SampledAndLinked_NeedAFifthOfTheSampleToHaveAWikilink(int sampled, int linked, bool expected) =>
        new NoteFolderCheck(Notes: 30, Files: 30, sampled, linked).IsFolderOfNotes.ShouldBe(expected);

    [Fact]
    public void IsFolderOfNotes_NothingSampled_IsNotAFolderOfNotes() =>
        new NoteFolderCheck(Notes: 30, Files: 30, Sampled: 0, Linked: 0).IsFolderOfNotes.ShouldBeFalse();

    [Fact]
    public void IsFolderOfNotes_ManyWikilinksButTooFewNotes_IsNotAFolderOfNotes()
    {
        new NoteFolderCheck(Notes: 9, Files: 9, Sampled: 9, Linked: 9).IsFolderOfNotes.ShouldBeFalse();
        new NoteFolderCheck(Notes: 12, Files: 30, Sampled: 12, Linked: 12).IsFolderOfNotes.ShouldBeFalse();
    }

    // The outer folder: 20 notes, all sampled, 10 with a wikilink: its wikilinked notes are estimated at 10.
    [Theory]
    [InlineData(12, 12, 6, true)] // 6 of 10 is exactly 60%
    [InlineData(12, 12, 5, false)]
    [InlineData(12, 12, 12, true)]
    [InlineData(12, 12, 0, false)]
    [InlineData(6, 6, 6, true)]
    [InlineData(5, 5, 5, false)] // 5 of 10
    [InlineData(100, 20, 4, true)] // 4 of 20 sampled is a fifth of 100 notes: 20, and 20 is more than 6
    [InlineData(10, 0, 0, false)] // nothing sampled holds nothing
    public void Holds_InnerFolder_NeedsSixtyPercentOfTheWikilinkedNotesOfTheOuterOne(int notes, int sampled, int linked, bool expected)
    {
        var outer = new NoteFolderCheck(Notes: 20, Files: 20, Sampled: 20, Linked: 10);

        NoteFolderCheck.Holds(new NoteFolderCheck(notes, notes, sampled, linked), outer).ShouldBe(expected);
    }

    [Fact]
    public void Holds_WikilinkedNotesAreTheShareOfTheSampleTimesTheNotes_NotTheSampleItself()
    {
        // A sample of 20 of 1,000 notes with 4 linked is 200 notes; a sample of 20 of 100 notes with 8 linked is 40.
        var outer = new NoteFolderCheck(Notes: 100, Files: 100, Sampled: 20, Linked: 8);
        var small = new NoteFolderCheck(Notes: 12, Files: 12, Sampled: 12, Linked: 12);
        var big = new NoteFolderCheck(Notes: 1000, Files: 1000, Sampled: 20, Linked: 4);

        NoteFolderCheck.Holds(small, outer).ShouldBeFalse(); // 12 of 40
        NoteFolderCheck.Holds(big, outer).ShouldBeTrue(); // 200 of 40
    }

    [Fact]
    public void Holds_OuterFolderWithNothingSampled_IsHeldByNobody() =>
        NoteFolderCheck.Holds(new NoteFolderCheck(12, 12, 12, 12), new NoteFolderCheck(20, 20, 0, 0)).ShouldBeFalse();

    [Fact]
    public void Holds_NumbersThatAreFarBeyondTheLimits_DoNotOverflow()
    {
        var huge = new NoteFolderCheck(int.MaxValue, int.MaxValue, Sampled: 20, Linked: 20);

        NoteFolderCheck.Holds(huge, huge).ShouldBeTrue();
        NoteFolderCheck.Holds(new NoteFolderCheck(1, 1, 1, 1), huge).ShouldBeFalse();
        huge.HasEnoughNotes.ShouldBeTrue();
        huge.IsFolderOfNotes.ShouldBeTrue();
    }

    [Fact]
    public void Rules_TheNumbersOfTheSpecification_AreTheOnesInTheCode()
    {
        NoteFolderCheck.Levels.ShouldBe(3);
        NoteFolderCheck.MinNotes.ShouldBe(10);
        NoteFolderCheck.MinNotesPercent.ShouldBe(50);
        NoteFolderCheck.MinLinkedPercent.ShouldBe(20);
        NoteFolderCheck.SampleSize.ShouldBe(20);
        NoteFolderCheck.SampleBytes.ShouldBe(4096);
        NoteFolderCheck.InnerPercent.ShouldBe(60);
    }
}
