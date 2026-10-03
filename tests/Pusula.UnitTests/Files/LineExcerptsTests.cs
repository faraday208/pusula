using System.Text;
using Pusula.Files;
using Shouldly;
using Xunit;

namespace Pusula.UnitTests.Files;

public sealed class LineExcerptsTests
{
    // Text that can be written as UTF-8, and so as JSON, has no lone surrogate.
    private static readonly UTF8Encoding StrictUtf8 = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

    // White space that is not a plain space, spelled out so that it can be seen in the source.
    private const string NoBreakSpace = "\U000000A0";
    private const string EmSpace = "\U00002003";
    private const string IdeographicSpace = "\U00003000";

    private static string? Of(string text, int line) => new LineExcerpts(text).Of(line);

    // ---- The text of a line -----------------------------------------------------------------------------------

    [Fact]
    public void Of_LineInTheMiddle_IsItsTextWithoutTheWhitespaceAroundIt()
    {
        Of("first\n  second line \t\nthird", 2).ShouldBe("second line");
    }

    [Fact]
    public void Of_FirstAndLastLine_AreFound()
    {
        var lines = new LineExcerpts("first\nsecond\nlast");

        lines.Of(1).ShouldBe("first");
        lines.Of(3).ShouldBe("last");
    }

    [Fact]
    public void Of_WindowsLineEndings_DropTheCarriageReturn()
    {
        var lines = new LineExcerpts("one\r\ntwo\r\nthree\r\n");

        lines.Of(1).ShouldBe("one");
        lines.Of(2).ShouldBe("two");
        lines.Of(3).ShouldBe("three");
    }

    [Fact]
    public void Of_LoneCarriageReturn_IsNotALineBreak()
    {
        // Line numbers in the index count only '\n' (LinkExtractor, FrontmatterParser); an excerpt has to agree with them.
        var lines = new LineExcerpts("a\rb\nc");

        lines.Of(1).ShouldBe("a\rb");
        lines.Of(2).ShouldBe("c");
    }

    [Fact]
    public void Of_UnicodeWhitespaceAroundTheText_IsTrimmed()
    {
        Of($"{NoBreakSpace}{EmSpace} text {IdeographicSpace}\n", 1).ShouldBe("text");
    }

    [Fact]
    public void Of_Markdown_IsLeftAsWritten()
    {
        const string line = "- **bold** [[wiki#head|alias]] `code` [text](a.md \"title\") <b>x</b> | cell |";

        Of($"   {line}   ", 1).ShouldBe(line);
    }

    // ---- No excerpt -------------------------------------------------------------------------------------------

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(4)]
    [InlineData(int.MaxValue)]
    public void Of_LineThatTheTextDoesNotHave_IsNull(int line)
    {
        Of("a\nb\nc", line).ShouldBeNull();
    }

    [Fact]
    public void Of_BlankLine_IsNull()
    {
        var lines = new LineExcerpts($"a\n\n \t {NoBreakSpace}\nb\n");

        lines.Of(2).ShouldBeNull();
        lines.Of(3).ShouldBeNull();
        lines.Of(4).ShouldBe("b");
        lines.Of(5).ShouldBeNull();
        lines.Of(6).ShouldBeNull();
    }

    [Fact]
    public void Of_EmptyText_HasNothingToRead()
    {
        var lines = new LineExcerpts(string.Empty);

        lines.Of(1).ShouldBeNull();
        lines.Of(2).ShouldBeNull();
    }

    // ---- The maximum length -----------------------------------------------------------------------------------

    [Fact]
    public void Of_LineOfExactlyTwoHundredCharacters_IsNotCut()
    {
        string line = new('x', 200);

        Of(line, 1).ShouldBe(line);
    }

    [Fact]
    public void Of_LineOfTwoHundredAndOneCharacters_IsCutToTwoHundredWithTheEllipsisIncluded()
    {
        string line = new('x', 201);

        string excerpt = Of(line, 1).ShouldNotBeNull();

        excerpt.ShouldBe(new string('x', 199) + "…");
        excerpt.Length.ShouldBe(200);
    }

    [Fact]
    public void Of_LongLine_KeepsItsStartAndEndsWithTheEllipsis()
    {
        string line = string.Concat(Enumerable.Range(0, 1000).Select(index => (char)('a' + (index % 26))));

        string excerpt = Of(line, 1).ShouldNotBeNull();

        excerpt.Length.ShouldBe(200);
        excerpt.ShouldBe(line[..199] + "…");
    }

    [Fact]
    public void Of_WhitespaceAroundALineOfTwoHundredCharacters_DoesNotCountTowardsTheLength()
    {
        string line = new('x', 200);

        Of($"    {line}    ", 1).ShouldBe(line);
    }

    [Fact]
    public void Of_CutThatWouldSplitASurrogatePair_StopsBeforeIt()
    {
        // The emoji is a surrogate pair at indexes 198 and 199: keeping 199 characters would leave its high half alone.
        string line = new string('a', 198) + "\U0001F600" + new string('b', 50);

        string excerpt = Of(line, 1).ShouldNotBeNull();

        excerpt.ShouldBe(new string('a', 198) + "…");
        Should.NotThrow(() => StrictUtf8.GetBytes(excerpt));
    }

    [Fact]
    public void Of_CutRightAfterASurrogatePair_KeepsThePair()
    {
        // The emoji sits at indexes 197 and 198: the 199 characters that are kept end with its low half.
        string line = new string('a', 197) + "\U0001F600" + new string('b', 50);

        string excerpt = Of(line, 1).ShouldNotBeNull();

        excerpt.ShouldBe(new string('a', 197) + "\U0001F600" + "…");
        excerpt.Length.ShouldBe(200);
        Should.NotThrow(() => StrictUtf8.GetBytes(excerpt));
    }

    // ---- Many lines -------------------------------------------------------------------------------------------

    [Fact]
    public void Of_ManyLines_EveryLineIsFoundInAnyOrder()
    {
        const int count = 30_000;
        string text = string.Concat(Enumerable.Range(1, count).Select(number => $"  line {number}  \n"));
        var lines = new LineExcerpts(text);

        for (int number = count; number >= 1; number--)
        {
            lines.Of(number).ShouldBe($"line {number}");
        }

        lines.Of(count + 1).ShouldBeNull();
    }
}
