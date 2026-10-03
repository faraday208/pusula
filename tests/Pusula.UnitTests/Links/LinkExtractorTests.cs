using System.Diagnostics;
using System.Reflection;
using System.Text.RegularExpressions;
using Pusula.Links;
using Pusula.UnitTests.Support;
using Shouldly;
using Xunit;

namespace Pusula.UnitTests.Links;

public sealed class LinkExtractorTests
{
    private static string[] Extract(string body, int bodyStartLine = 1) =>
        [.. LinkExtractor.Extract(body, bodyStartLine).Select(link => $"{link.Kind}|{link.Raw}|{link.Line}")];

    // ---- Markdown links -------------------------------------------------------------------------------------

    [Theory]
    [InlineData("See [the rule](rules/a.md) now.", "rules/a.md")]
    [InlineData("[a](b.md \"Title\")", "b.md")]
    [InlineData("[a](b.md 'Title')", "b.md")]
    [InlineData("[a](<my file.md>)", "my file.md")]
    [InlineData("[a](<my file.md> \"T\")", "my file.md")]
    [InlineData("[a [b] c](x.md)", "x.md")]
    [InlineData("[a](my%20note.md#Some%20Heading)", "my%20note.md#Some%20Heading")]
    [InlineData("[a](../shared/x.md)", "../shared/x.md")]
    [InlineData("[a](file:///home/u/.claude/rules/a.md)", "file:///home/u/.claude/rules/a.md")]
    [InlineData("[a](FILE:///x.md)", "FILE:///x.md")]
    [InlineData("[![badge](b.png)](target.md)", "target.md")]
    [InlineData("[a](b.md )", "b.md")]
    public void Extract_MarkdownLink_ReturnsDestination(string body, string raw) =>
        Extract(body).ShouldBe([$"MarkdownLink|{raw}|1"]);

    [Theory]
    [InlineData("[a](#section)")]
    [InlineData("[a](<#section>)")]
    [InlineData("[a](https://example.com/x.md)")]
    [InlineData("[a](http://example.com)")]
    [InlineData("[a](mailto:me@example.com)")]
    [InlineData("[a](ftp://example.com/x)")]
    [InlineData("[a](C:/Users/x.md)")]
    [InlineData("[a]()")]
    [InlineData("[a](<>)")]
    [InlineData("[a](< >)")]
    [InlineData("![alt](image.png)")]
    [InlineData("[a] (b.md)")]
    [InlineData("[a]( b.md)")]
    [InlineData("[a](b.md")]
    [InlineData("[unterminated(b.md)")]
    public void Extract_NonFileMarkdownSyntax_ReturnsNothing(string body) =>
        Extract(body).ShouldBeEmpty();

    // ---- Wiki links -----------------------------------------------------------------------------------------

    [Theory]
    [InlineData("[[note]]", "note")]
    [InlineData("[[note#Heading|alias]]", "note#Heading|alias")]
    [InlineData("[[folder/note]]", "folder/note")]
    [InlineData("[[ spaced ]]", " spaced ")]
    [InlineData("text [[#Heading]] text", "#Heading")]
    public void Extract_WikiLink_ReturnsInnerText(string body, string raw) =>
        Extract(body).ShouldBe([$"WikiLink|{raw}|1"]);

    [Theory]
    [InlineData("[[unclosed")]
    [InlineData("[[]]")]
    [InlineData("[[multi\nline]]")]
    [InlineData("![[unclosed")]
    [InlineData("![[]]")]
    public void Extract_MalformedWikiLinksAndEmbeds_ReturnNothing(string body) =>
        Extract(body).ShouldBeEmpty();

    // ---- Embeds -----------------------------------------------------------------------------------------------

    [Theory]
    [InlineData("![[img.png]]", "img.png")]
    [InlineData("![[note#Heading|200]]", "note#Heading|200")]
    [InlineData("text ![[a b]] text", "a b")]
    [InlineData("![[#Heading]]", "#Heading")]
    public void Extract_Embed_ReturnsInnerText(string body, string raw) =>
        Extract(body).ShouldBe([$"Embed|{raw}|1"]);

    [Fact]
    public void Extract_EmbedAndWikiLinkOnOneLine_ReportsEachOnceInOrder() =>
        Extract("[[a]] and ![[b]] and [[c]]").ShouldBe(["WikiLink|a|1", "Embed|b|1", "WikiLink|c|1"]);

    [Theory]
    [InlineData("```\n![[w]]\n```")]
    [InlineData("`![[w]]`")]
    public void Extract_EmbedInsideCode_IsIgnored(string body) =>
        Extract(body).ShouldBeEmpty();

    [Fact]
    public void Extract_ClaudePathInsideEmbed_IsReportedOnlyAsTheEmbed() =>
        Extract("![[~/.claude/rules/a.md]]").ShouldBe(["Embed|~/.claude/rules/a.md|1"]);

    // ---- Code and links are blanked for the tags --------------------------------------------------------------

    [Fact]
    public void MaskCodeAndLinks_PlainText_IsKept() =>
        LinkExtractor.MaskCodeAndLinks("plain #tag text").ShouldBe("plain #tag text");

    // The placeholder is not white space: a tag right after a blanked span is not preceded by a space.
    [Fact]
    public void MaskCodeAndLinks_InlineCode_IsBlankedWithAPlaceholderThatIsNotWhiteSpace() =>
        LinkExtractor.MaskCodeAndLinks("a `#code`#tag b").ShouldBe("a " + new string('\u0001', 7) + "#tag b");

    [Fact]
    public void MaskCodeAndLinks_MarkdownLinksWikiLinksAndEmbeds_AreBlanked()
    {
        LinkExtractor.MaskCodeAndLinks("see [x #y](z.md)").ShouldBe("see " + new string('\u0001', 12));
        LinkExtractor.MaskCodeAndLinks("[[a #b]] ![[c]]").ShouldBe(new string('\u0001', 8) + " " + new string('\u0001', 6));
    }

    [Fact]
    public void MaskCodeAndLinks_FencedCode_IsBlankedAndLineBreaksStay() =>
        LinkExtractor.MaskCodeAndLinks("a\n```\n#x\n```\n#y").ShouldBe("a\n   \n  \n   \n#y");

    [Fact]
    public void Extract_WikiLinkContainingACodeSpan_KeepsTheTextAsWritten() =>
        Extract("[[a `b` c]]").ShouldBe(["WikiLink|a `b` c|1"]);

    [Fact]
    public void Extract_DestinationContainingACodeSpan_KeepsTheTextAsWritten() =>
        Extract("[x](<a `b` c.md>)").ShouldBe(["MarkdownLink|a `b` c.md|1"]);

    [Fact]
    public void Extract_SeveralWikiLinksOnOneLine_ReturnsEachInOrder() =>
        Extract("[[a]] and [[b]] and [[c|d]]").ShouldBe(["WikiLink|a|1", "WikiLink|b|1", "WikiLink|c|d|1"]);

    // ---- Code is ignored for links ----------------------------------------------------------------------------

    [Theory]
    [InlineData("```\n[a](x.md)\n[[w]]\n~/.claude/rules/a.md\n```")]
    [InlineData("~~~\n[a](x.md)\n~~~")]
    [InlineData("```\n[a](x.md)\n")]
    [InlineData("`[a](x.md)`")]
    [InlineData("`[[w]]`")]
    [InlineData("``[a](x.md)``")]
    public void Extract_LinksInsideCode_AreIgnored(string body) =>
        Extract(body).ShouldBeEmpty();

    [Fact]
    public void Extract_UnclosedFence_MasksToTheEndOfTheText() =>
        Extract("[a](before.md)\n```\n[b](inside.md)\n\n[c](also-inside.md)").ShouldBe(["MarkdownLink|before.md|1"]);

    [Fact]
    public void Extract_LinkAfterClosedFence_IsReported() =>
        Extract("```js\n[a](x.md)\n```\n[b](y.md)").ShouldBe(["MarkdownLink|y.md|4"]);

    [Fact]
    public void Extract_FenceClosedByALongerRun_EndsThere() =>
        Extract("```\n[a](x.md)\n`````\n[b](y.md)").ShouldBe(["MarkdownLink|y.md|4"]);

    [Fact]
    public void Extract_ShorterRunDoesNotCloseTheFence() =>
        Extract("````\n```\n[a](x.md)\n````\n[b](y.md)").ShouldBe(["MarkdownLink|y.md|5"]);

    [Fact]
    public void Extract_TildeFenceIsNotClosedByBackticks() =>
        Extract("~~~\n```\n[a](x.md)\n~~~\n[b](y.md)").ShouldBe(["MarkdownLink|y.md|5"]);

    [Fact]
    public void Extract_ClosingFenceFollowedByWhitespace_Closes() =>
        Extract("```\nx\n```   \n[b](y.md)").ShouldBe(["MarkdownLink|y.md|4"]);

    [Fact]
    public void Extract_ClosingFenceFollowedByText_DoesNotClose() =>
        Extract("```\nx\n``` text\n[a](x.md)\n```\n[b](y.md)").ShouldBe(["MarkdownLink|y.md|6"]);

    [Fact]
    public void Extract_ClosingFenceIndentedByFourSpaces_DoesNotClose() =>
        Extract("```\n    ```\n[a](x.md)").ShouldBeEmpty();

    [Fact]
    public void Extract_FenceIndentedByThreeSpaces_IsAFence() =>
        Extract("   ```\n[a](x.md)\n   ```\n[b](y.md)").ShouldBe(["MarkdownLink|y.md|4"]);

    [Fact]
    public void Extract_FenceIndentedByFourSpaces_IsNotAFence() =>
        Extract("    ```\n[a](x.md)").ShouldBe(["MarkdownLink|x.md|2"]);

    [Theory]
    [InlineData("``\n[a](x.md)")]
    [InlineData("`~`\n[a](x.md)")]
    [InlineData("~~\n[a](x.md)")]
    public void Extract_RunsShorterThanThree_AreNotFences(string body) =>
        Extract(body).ShouldBe(["MarkdownLink|x.md|2"]);

    [Fact]
    public void Extract_FenceWithCrLfLineEndings_ClosesOnTheRightLine() =>
        Extract("```\r\n[a](x.md)\r\n```\r\n[b](y.md)\r\n").ShouldBe(["MarkdownLink|y.md|4"]);

    [Fact]
    public void MaskFences_FencedLines_AreReplacedBySpacesKeepingLineBreaks() =>
        LinkExtractor.MaskFences("a\n```\nb c\n```\nd").ShouldBe("a\n   \n   \n   \nd");

    [Fact]
    public void MaskFences_CrLf_KeepsCarriageReturns() =>
        LinkExtractor.MaskFences("```\r\nx\r\n```\r\ny").ShouldBe("   \r\n \r\n   \r\ny");

    [Fact]
    public void MaskFences_TextWithoutFences_ReturnsTheSameInstance()
    {
        const string text = "just `code` and [a](b.md)\n";

        LinkExtractor.MaskFences(text).ShouldBeSameAs(text);
    }

    // ---- Claude paths -----------------------------------------------------------------------------------------

    [Theory]
    [InlineData("See ~/.claude/rules/a.md for details")]
    [InlineData("(~/.claude/rules/a.md)")]
    [InlineData("~/.claude/rules/a.md.")]
    [InlineData("~/.claude/rules/a.md:")]
    [InlineData("~/.claude/rules/a.md,")]
    [InlineData("~/.claude/rules/a.md;")]
    [InlineData("'~/.claude/rules/a.md'")]
    [InlineData("\"~/.claude/rules/a.md\"")]
    [InlineData("`~/.claude/rules/a.md`")]
    [InlineData("<~/.claude/rules/a.md>")]
    [InlineData("**~/.claude/rules/a.md**")]
    [InlineData("a | ~/.claude/rules/a.md | b")]
    [InlineData("- ~/.claude/rules/a.md\n")]
    public void Extract_ClaudePath_EndsBeforeDelimitersAndTrailingPunctuation(string body) =>
        Extract(body).ShouldBe(["ClaudePath|~/.claude/rules/a.md|1"]);

    [Theory]
    [InlineData("~/.claude/rules/a.md:12", "~/.claude/rules/a.md:12")]
    [InlineData("~/.claude/shared/dotnet-rules.v2.md", "~/.claude/shared/dotnet-rules.v2.md")]
    [InlineData("~/.claude/.credentials.json", "~/.claude/.credentials.json")]
    [InlineData("~/.claude/rules/*.md", "~/.claude/rules/")]
    [InlineData("~/.claude/skills/*/SKILL.md", "~/.claude/skills/")]
    [InlineData("~/.claude/rules/<name>.md", "~/.claude/rules/")]
    [InlineData("~/.claude/rules/", "~/.claude/rules/")]
    [InlineData("~/.claude/a", "~/.claude/a")]
    [InlineData("~/.claude/skills/$s/SKILL.md", "~/.claude/skills/")]
    [InlineData("~/.claude/skills/${name}/SKILL.md", "~/.claude/skills/")]
    [InlineData("~/.claude/rules/$NAME.md", "~/.claude/rules/")]
    [InlineData("~/.claude/a$b", "~/.claude/a")]
    [InlineData("~/.claude/skills/deploy/$1.md", "~/.claude/skills/deploy/")]
    public void Extract_ClaudePathEdgeCases_FollowThePattern(string body, string raw) =>
        Extract(body).ShouldBe([$"ClaudePath|{raw}|1"]);

    [Theory]
    [InlineData("~/.claude")]
    [InlineData("~/.claude/")]
    [InlineData("~/.claude/<name>.md")]
    [InlineData("~/.claude/{a,b}")]
    [InlineData("~/.claude/*")]
    [InlineData("~/.claude/$HOME/x")]
    [InlineData("~/.claude/${x}")]
    [InlineData("~/.claude/$")]
    [InlineData("~/.claud/rules/a.md")]
    [InlineData("/home/u/.claude/rules/a.md")]
    public void Extract_NotAClaudePath_ReturnsNothing(string body) =>
        Extract(body).ShouldBeEmpty();

    [Fact]
    public void Extract_TwoClaudePathsOnOneLine_ReturnsBoth() =>
        Extract("~/.claude/a.md and ~/.claude/b.md").ShouldBe(["ClaudePath|~/.claude/a.md|1", "ClaudePath|~/.claude/b.md|1"]);

    [Fact]
    public void Extract_ClaudePathInsideMarkdownLink_IsReportedOnlyAsTheLink() =>
        Extract("[r](~/.claude/rules/a.md)").ShouldBe(["MarkdownLink|~/.claude/rules/a.md|1"]);

    [Fact]
    public void Extract_ClaudePathInsideWikiLink_IsReportedOnlyAsTheLink() =>
        Extract("[[~/.claude/rules/a.md]]").ShouldBe(["WikiLink|~/.claude/rules/a.md|1"]);

    [Fact]
    public void Extract_ClaudePathInsideExternalLink_IsNotReported() =>
        Extract("[d](https://example.com/~/.claude/rules/a.md)").ShouldBeEmpty();

    [Fact]
    public void Extract_ClaudePathInsideAnchorLink_IsNotReported() =>
        Extract("[d](#~/.claude/rules/a.md)").ShouldBeEmpty();

    [Fact]
    public void Extract_ClaudePathNextToALink_IsStillReported() =>
        Extract("[a](x.md) then ~/.claude/rules/a.md").ShouldBe(["MarkdownLink|x.md|1", "ClaudePath|~/.claude/rules/a.md|1"]);

    // ---- Relative paths in inline code ------------------------------------------------------------------------

    [Theory]
    [InlineData("`rules/a.md`", "rules/a.md")]
    [InlineData("`./rules/a.md`", "./rules/a.md")]
    [InlineData("`skills/`", "skills/")]
    [InlineData("` rules/a.md `", "rules/a.md")]
    [InlineData("``rules/a.md``", "rules/a.md")]
    [InlineData("`a/b/c.d/e-f_g`", "a/b/c.d/e-f_g")]
    [InlineData("`kurallar/şablon.md`", "kurallar/şablon.md")]
    [InlineData("`git-hooks/prepare-commit-msg`", "git-hooks/prepare-commit-msg")]
    public void Extract_RelativePathInCode_ReturnsTheCodeContent(string body, string raw) =>
        Extract(body).ShouldBe([$"RelativePath|{raw}|1"]);

    [Theory]
    [InlineData("`a.md`")]
    [InlineData("`../x/y.md`")]
    [InlineData("`..x/y.md`")]
    [InlineData("`/abs/x.md`")]
    [InlineData("`~/x/y.md`")]
    [InlineData("`a/b c`")]
    [InlineData("`a/b:c`")]
    [InlineData("`src/**/*.cs`")]
    [InlineData("`a//b`")]
    [InlineData("`npm run build`")]
    [InlineData("`rules/a.md --flag`")]
    [InlineData("rules/a.md outside code")]
    [InlineData("`` ``")]
    public void Extract_CodeThatIsNotARelativePath_ReturnsNothing(string body) =>
        Extract(body).ShouldBeEmpty();

    [Fact]
    public void Extract_ClaudePathInCode_IsNotARelativePath() =>
        Extract("`~/.claude/rules/a.md`").ShouldBe(["ClaudePath|~/.claude/rules/a.md|1"]);

    [Fact]
    public void Extract_RelativePathInsideFence_IsIgnored() =>
        Extract("```\n`rules/a.md`\n```").ShouldBeEmpty();

    [Fact]
    public void Extract_CodeSpanInsideLinkText_ReportsBothTheLinkAndThePath() =>
        Extract("[`rules/a.md`](rules/a.md)").ShouldBe(["MarkdownLink|rules/a.md|1", "RelativePath|rules/a.md|1"]);

    [Fact]
    public void Extract_CodeSpanAcrossLines_IsNotASpan() =>
        Extract("`rules/a.md\nrules/b.md`").ShouldBeEmpty();

    // ---- Order, lines, misc -----------------------------------------------------------------------------------

    [Fact]
    public void Extract_MixedKinds_AreReturnedInDocumentOrder() =>
        Extract("`rules/a.md` [[w]] [m](m.md) ~/.claude/p.md").ShouldBe(
        [
            "RelativePath|rules/a.md|1",
            "WikiLink|w|1",
            "MarkdownLink|m.md|1",
            "ClaudePath|~/.claude/p.md|1",
        ]);

    [Fact]
    public void Extract_WikiLinkInsideMarkdownLinkText_ReportsBoth() =>
        Extract("[[a]](b.md)").ShouldBe(["MarkdownLink|b.md|1", "WikiLink|a|1"]);

    [Fact]
    public void Extract_LinesAreOffsetByTheBodyStartLine() =>
        Extract("first\n[a](x.md)\n\n`rules/b.md`\n~/.claude/c.md", bodyStartLine: 10).ShouldBe(
        [
            "MarkdownLink|x.md|11",
            "RelativePath|rules/b.md|13",
            "ClaudePath|~/.claude/c.md|14",
        ]);

    [Fact]
    public void Extract_LinkOnTheFirstLine_ReportsTheBodyStartLine() =>
        Extract("[a](x.md)", bodyStartLine: 6).ShouldBe(["MarkdownLink|x.md|6"]);

    [Fact]
    public void Extract_CrLfBody_CountsLinesByLineFeed() =>
        Extract("a\r\nb\r\n[[w]]\r\n", bodyStartLine: 3).ShouldBe(["WikiLink|w|5"]);

    [Fact]
    public void Extract_LinesAfterAFence_AreCountedThroughTheFence() =>
        Extract("```\nx\ny\n```\n[a](x.md)").ShouldBe(["MarkdownLink|x.md|5"]);

    [Theory]
    [InlineData("")]
    [InlineData("plain text without anything")]
    [InlineData("\n\n\n")]
    public void Extract_BodyWithoutReferences_ReturnsNothing(string body) =>
        Extract(body).ShouldBeEmpty();

    // ---- Patterns that run out of time ------------------------------------------------------------------------

    private static string PathologicalLine(string shape, int length) => shape switch
    {
        "unclosed backtick runs of every length" => PathologicalMarkdown.UnclosedBacktickRuns(length, "a"),
        "unclosed backtick runs and opening brackets" => PathologicalMarkdown.UnclosedBacktickRuns(length, "["),
        "backtick and bracket pairs" => PathologicalMarkdown.Repeat("`[", length),
        "link openers" => PathologicalMarkdown.Repeat("[a](", length),
        "wiki link openers" => PathologicalMarkdown.Repeat("[[", length),
        "claude path prefixes" => PathologicalMarkdown.Repeat("~/.claude/", length),
        "claude path with a long tail of dots" => "~/.claude/" + new string('.', length),
        "link destination followed by spaces" => "[a](x" + new string(' ', length),
        "unterminated angle destination" => "[a](<" + new string('a', length),
        _ => throw new ArgumentOutOfRangeException(nameof(shape), shape, "Unknown shape."),
    };

    // One line of 50,000 characters, shaped to make a pattern work as hard as it can. However much that costs, the
    // extraction has to end quickly and quietly: the match timeout cuts it short, nothing is thrown at the caller.
    [Theory]
    [InlineData("unclosed backtick runs of every length")]
    [InlineData("unclosed backtick runs and opening brackets")]
    [InlineData("backtick and bracket pairs")]
    [InlineData("link openers")]
    [InlineData("wiki link openers")]
    [InlineData("claude path prefixes")]
    [InlineData("claude path with a long tail of dots")]
    [InlineData("link destination followed by spaces")]
    [InlineData("unterminated angle destination")]
    public void TryExtract_PathologicalSingleLine_FinishesWithinTwoSecondsWithoutThrowing(string shape)
    {
        string body = PathologicalLine(shape, length: 50_000);
        var stopwatch = Stopwatch.StartNew();

        LinkExtractor.TryExtract(body, 1, out _);

        stopwatch.Elapsed.ShouldBeLessThan(TimeSpan.FromSeconds(2), shape);
    }

    // A file close to the size limit (2 MiB) with the worst shape. Without the timeout the inline code pattern needs
    // about half a minute for it (27 s measured on .NET 10); with it, the production timeout ends the match after a
    // second, so this test takes about that long.
    [Fact]
    public void TryExtract_PathologicalLineNearTheFileSizeLimit_EndsWithinSecondsWithoutThrowing()
    {
        string body = PathologicalMarkdown.UnclosedBacktickRuns(1_500_000, "a");
        var stopwatch = Stopwatch.StartNew();

        LinkExtractor.TryExtract(body, 1, out _);

        stopwatch.Elapsed.ShouldBeLessThan(TimeSpan.FromSeconds(10));
    }

    [Fact]
    public void Extract_BodyThatMakesAPatternRunOutOfTime_ThrowsRegexMatchTimeoutException()
    {
        LinkExtractor.PatternSet patterns = LinkExtractor.PatternSet.WithTimeout(TimeSpan.FromMilliseconds(50));
        string body = PathologicalMarkdown.UnclosedBacktickRuns(1_000_000, "a");

        RegexMatchTimeoutException exception = Should.Throw<RegexMatchTimeoutException>(() => LinkExtractor.Extract(body, 1, patterns));

        exception.MatchTimeout.ShouldBe(TimeSpan.FromMilliseconds(50));
    }

    // Nothing is extracted from such a body, not even what comes before the line that is too much for the patterns.
    [Fact]
    public void TryExtract_BodyThatMakesAPatternRunOutOfTime_ReturnsFalseAndNoLinks()
    {
        LinkExtractor.PatternSet patterns = LinkExtractor.PatternSet.WithTimeout(TimeSpan.FromMilliseconds(50));
        string body = "[a](before.md)\n" + PathologicalMarkdown.UnclosedBacktickRuns(1_000_000, "a");

        bool completed = LinkExtractor.TryExtract(body, 1, out IReadOnlyList<RawLink> links, patterns);

        completed.ShouldBeFalse();
        links.ShouldBeEmpty();
    }

    [Fact]
    public void TryExtract_BodyThatFinishesInTime_ReturnsTrueAndTheLinks()
    {
        bool completed = LinkExtractor.TryExtract("[a](x.md) and `rules/y.md`", 1, out IReadOnlyList<RawLink> links);

        completed.ShouldBeTrue();
        links.Select(link => $"{link.Kind}|{link.Raw}|{link.Line}").ShouldBe(["MarkdownLink|x.md|1", "RelativePath|rules/y.md|1"]);
    }

    [Fact]
    public void TryExtract_EmptyBody_ReturnsTrueAndNoLinks()
    {
        LinkExtractor.TryExtract(string.Empty, 1, out IReadOnlyList<RawLink> links).ShouldBeTrue();

        links.ShouldBeEmpty();
    }

    [Fact]
    public void Extract_PatternsFromWithTimeout_FindTheSameLinksAsTheDefaultPatterns()
    {
        const string body = "[a](x.md) [[w#h|t]] ~/.claude/rules/p.md `rules/y.md` [e](https://example.com) ![i](i.png)\n```\n[f](f.md)\n```\n";
        LinkExtractor.PatternSet patterns = LinkExtractor.PatternSet.WithTimeout(TimeSpan.FromSeconds(30));

        LinkExtractor.Extract(body, 1, patterns).ShouldBe(LinkExtractor.Extract(body, 1));
    }

    // The source-generated expressions are the private static methods that return a Regex. Finding them by reflection
    // means that an expression that is added later without a timeout fails here.
    [Fact]
    public void Extractor_EveryRegularExpression_HasTheMatchTimeout()
    {
        MethodInfo[] expressions =
        [
            .. typeof(LinkExtractor)
                .GetMethods(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly)
                .Where(method => method.ReturnType == typeof(Regex)),
        ];

        expressions.ShouldNotBeEmpty();
        foreach (MethodInfo expression in expressions)
        {
            ((Regex)expression.Invoke(null, null)!).MatchTimeout.ShouldBe(TimeSpan.FromMilliseconds(LinkExtractor.MatchTimeoutMilliseconds), expression.Name);
        }
    }

    [Fact]
    public void PatternSet_Default_UsesTheMatchTimeoutForEveryExpression()
    {
        foreach (Regex regex in AllOf(LinkExtractor.PatternSet.Default))
        {
            regex.MatchTimeout.ShouldBe(TimeSpan.FromMilliseconds(LinkExtractor.MatchTimeoutMilliseconds), regex.ToString());
        }
    }

    [Fact]
    public void PatternSet_WithTimeout_UsesTheGivenTimeoutForEveryExpression()
    {
        TimeSpan timeout = TimeSpan.FromMilliseconds(123);

        foreach (Regex regex in AllOf(LinkExtractor.PatternSet.WithTimeout(timeout)))
        {
            regex.MatchTimeout.ShouldBe(timeout, regex.ToString());
        }
    }

    private static Regex[] AllOf(LinkExtractor.PatternSet set) =>
        [set.InlineCode, set.MarkdownLink, set.WikiLink, set.ClaudePath, set.RelativePath, set.Scheme, set.Embed];
}
