using System.Text.Json.Nodes;
using Pusula.Indexing;
using Shouldly;
using Xunit;

namespace Pusula.UnitTests.Indexing;

public sealed class FrontmatterParserTests
{
    [Fact]
    public void Parse_ValidFrontmatter_ReturnsStringValuesAndBody()
    {
        const string text = "---\nname: foo\ndescription: hello world\nenabled: true\ncount: 3\nnothing: null\n---\n# Title\nbody\n";

        FrontmatterResult result = FrontmatterParser.Parse(text);

        result.Error.ShouldBeNull();
        result.ErrorLine.ShouldBeNull();
        result.Frontmatter.ShouldNotBeNull();
        FrontmatterValues.GetString(result.Frontmatter, "name").ShouldBe("foo");
        FrontmatterValues.GetString(result.Frontmatter, "description").ShouldBe("hello world");
        FrontmatterValues.GetString(result.Frontmatter, "enabled").ShouldBe("true");
        FrontmatterValues.GetString(result.Frontmatter, "count").ShouldBe("3");
        FrontmatterValues.GetString(result.Frontmatter, "nothing").ShouldBe("null");
        result.Body.ShouldBe("# Title\nbody\n");
        result.BodyStartLine.ShouldBe(8);
    }

    [Fact]
    public void Parse_NestedMetadata_ReturnsInnerObject()
    {
        const string text = "---\nname: foo\nmetadata:\n  author: me\n  version: \"1.0\"\n---\nbody";

        FrontmatterResult result = FrontmatterParser.Parse(text);

        result.Error.ShouldBeNull();
        JsonObject metadata = result.Frontmatter!["metadata"].ShouldBeOfType<JsonObject>();
        FrontmatterValues.GetString(metadata, "author").ShouldBe("me");
        FrontmatterValues.GetString(metadata, "version").ShouldBe("1.0");
    }

    [Fact]
    public void Parse_FlowAndBlockSequences_ReturnJsonArraysOfStrings()
    {
        const string text = "---\nallowed-tools: [Read, Grep]\npaths:\n  - \"src/**\"\n  - docs/**\n---\n";

        FrontmatterResult result = FrontmatterParser.Parse(text);

        result.Error.ShouldBeNull();
        result.Frontmatter!["allowed-tools"].ShouldBeOfType<JsonArray>().Select(node => node!.GetValue<string>()).ShouldBe(["Read", "Grep"]);
        result.Frontmatter["paths"].ShouldBeOfType<JsonArray>().Select(node => node!.GetValue<string>()).ShouldBe(["src/**", "docs/**"]);
    }

    [Fact]
    public void Parse_TextWithoutFrontmatter_ReturnsWholeTextAsBody()
    {
        const string text = "# Title\n\nsome text\n";

        FrontmatterResult result = FrontmatterParser.Parse(text);

        result.Frontmatter.ShouldBeNull();
        result.Error.ShouldBeNull();
        result.ErrorLine.ShouldBeNull();
        result.Body.ShouldBe(text);
        result.BodyStartLine.ShouldBe(1);
    }

    [Fact]
    public void Parse_EmptyText_ReturnsEmptyBody()
    {
        FrontmatterResult result = FrontmatterParser.Parse(string.Empty);

        result.Frontmatter.ShouldBeNull();
        result.Body.ShouldBeEmpty();
        result.BodyStartLine.ShouldBe(1);
    }

    [Fact]
    public void Parse_CrLfLineEndings_ParsesFrontmatterAndCountsLines()
    {
        const string text = "---\r\nname: a\r\ndescription: b\r\n---\r\nbody\r\nmore\r\n";

        FrontmatterResult result = FrontmatterParser.Parse(text);

        result.Error.ShouldBeNull();
        FrontmatterValues.GetString(result.Frontmatter, "name").ShouldBe("a");
        FrontmatterValues.GetString(result.Frontmatter, "description").ShouldBe("b");
        result.Body.ShouldBe("body\r\nmore\r\n");
        result.BodyStartLine.ShouldBe(5);
    }

    [Fact]
    public void Parse_ThreeDotsClosingLine_EndsFrontmatter()
    {
        FrontmatterResult result = FrontmatterParser.Parse("---\nname: a\n...\nbody");

        FrontmatterValues.GetString(result.Frontmatter, "name").ShouldBe("a");
        result.Body.ShouldBe("body");
        result.BodyStartLine.ShouldBe(4);
    }

    [Fact]
    public void Parse_ClosingLineWithoutTrailingNewline_ReturnsEmptyBody()
    {
        FrontmatterResult result = FrontmatterParser.Parse("---\na: b\n---");

        FrontmatterValues.GetString(result.Frontmatter, "a").ShouldBe("b");
        result.Body.ShouldBeEmpty();
        result.BodyStartLine.ShouldBe(4);
    }

    [Theory]
    [InlineData("---\n---\nbody")]
    [InlineData("---\n# only a comment\n---\nbody")]
    public void Parse_EmptyBlock_ReturnsEmptyMapping(string text)
    {
        FrontmatterResult result = FrontmatterParser.Parse(text);

        result.Error.ShouldBeNull();
        result.ErrorLine.ShouldBeNull();
        result.Frontmatter.ShouldNotBeNull().Count.ShouldBe(0);
        result.Body.ShouldBe("body");
    }

    [Theory]
    [InlineData("---\nname: a\nno closing line\n")]
    [InlineData("---\nname: a\n--- \nbody")]
    [InlineData("----\nname: a\n---\nbody")]
    [InlineData("\n---\nname: a\n---\nbody")]
    [InlineData("---")]
    [InlineData("---\n")]
    public void Parse_NotAFrontmatterBlock_TreatsTextAsBody(string text)
    {
        FrontmatterResult result = FrontmatterParser.Parse(text);

        result.Frontmatter.ShouldBeNull();
        result.Error.ShouldBeNull();
        result.ErrorLine.ShouldBeNull();
        result.Body.ShouldBe(text);
        result.BodyStartLine.ShouldBe(1);
    }

    [Fact]
    public void Parse_UnquotedColonInValue_FallsBackToTopLevelKeysAndReportsLineAndColumn()
    {
        const string text = "---\nname: foo\ndescription: Does X: then Y\nkebab-key: 'quoted'\n  indented: ignored\n---\nbody";

        FrontmatterResult result = FrontmatterParser.Parse(text);

        result.Error.ShouldNotBeNull();
        result.Error.ShouldStartWith("Line 3, column ");
        result.Error.ShouldNotContain("(Line:");
        result.ErrorLine.ShouldBe(3);
        FrontmatterValues.GetString(result.Frontmatter, "name").ShouldBe("foo");
        FrontmatterValues.GetString(result.Frontmatter, "description").ShouldBe("Does X: then Y");
        FrontmatterValues.GetString(result.Frontmatter, "kebab-key").ShouldBe("quoted");
        FrontmatterValues.Has(result.Frontmatter, "indented").ShouldBeFalse();
        result.Body.ShouldBe("body");
        result.BodyStartLine.ShouldBe(7);
    }

    [Fact]
    public void Parse_TabIndentation_ReportsErrorAndKeepsTopLevelKeys()
    {
        FrontmatterResult result = FrontmatterParser.Parse("---\nname: a\nmeta:\n\tkey: 1\n---\n");

        result.Error.ShouldNotBeNull().ShouldStartWith("Line ");
        FrontmatterValues.GetString(result.Frontmatter, "name").ShouldBe("a");
        FrontmatterValues.Has(result.Frontmatter, "meta").ShouldBeTrue();
    }

    [Fact]
    public void Parse_UnclosedFlowSequence_ReportsInvalidYamlInsteadOfThrowing()
    {
        // YamlDotNet throws InvalidOperationException (not YamlException) for this input.
        FrontmatterResult result = FrontmatterParser.Parse("---\nname: a\nlist: [1, 2\nother: 3\n---\nbody");

        result.Error.ShouldNotBeNull().ShouldStartWith("Invalid YAML: ");
        result.ErrorLine.ShouldBeNull();
        FrontmatterValues.GetString(result.Frontmatter, "name").ShouldBe("a");
        FrontmatterValues.GetString(result.Frontmatter, "other").ShouldBe("3");
        result.Body.ShouldBe("body");
    }

    [Fact]
    public void Parse_DuplicateKeys_ReportsErrorAndKeepsTheLastValue()
    {
        FrontmatterResult result = FrontmatterParser.Parse("---\na: 1\na: 2\n---\n");

        result.Error.ShouldNotBeNull().ShouldContain("Duplicate key a");
        result.Error.ShouldStartWith("Line 3, column 1: ");
        result.ErrorLine.ShouldBe(3);
        FrontmatterValues.GetString(result.Frontmatter, "a").ShouldBe("2");
    }

    [Theory]
    [InlineData("---\n- a\n- b\n---\nbody")]
    [InlineData("---\njust text\n---\nbody")]
    public void Parse_FrontmatterThatIsNotAMapping_ReportsError(string text)
    {
        FrontmatterResult result = FrontmatterParser.Parse(text);

        result.Frontmatter.ShouldBeNull();
        result.Error.ShouldBe("Frontmatter is not a mapping");
        result.ErrorLine.ShouldBeNull();
        result.Body.ShouldBe("body");
    }

    [Fact]
    public void Parse_NonScalarKey_ReportsErrorWithPosition()
    {
        FrontmatterResult result = FrontmatterParser.Parse("---\n? [a, b]\n: c\n---\n");

        result.Error.ShouldNotBeNull().ShouldContain("plain scalars");
        result.Error.ShouldStartWith("Line 2, column ");
        result.ErrorLine.ShouldBe(2);
    }

    [Fact]
    public void Parse_RecursiveAlias_ReportsTooComplexInsteadOfOverflowing()
    {
        FrontmatterResult result = FrontmatterParser.Parse("---\na: &x [*x]\nb: c\n---\n");

        result.Error.ShouldNotBeNull().ShouldContain("too complex");
        result.ErrorLine.ShouldBe(2);
        FrontmatterValues.GetString(result.Frontmatter, "b").ShouldBe("c");
    }

    [Fact]
    public void Parse_ExponentialAliasExpansion_ReportsTooComplex()
    {
        string tens(string alias) => "[" + string.Join(", ", Enumerable.Repeat("*" + alias, 10)) + "]";
        string text = "---\n"
            + "a: &a [x, x, x, x, x, x, x, x, x, x]\n"
            + "b: &b " + tens("a") + "\n"
            + "c: &c " + tens("b") + "\n"
            + "d: &d " + tens("c") + "\n"
            + "e: " + tens("d") + "\n"
            + "---\n";

        FrontmatterResult result = FrontmatterParser.Parse(text);

        result.Error.ShouldNotBeNull().ShouldContain("too complex");
    }

    [Fact]
    public void Parse_DeeplyNestedFlowCollections_AreRejectedBeforeYamlParsing()
    {
        string text = "---\nname: a\ndeep: " + new string('[', 100) + new string(']', 100) + "\n---\nbody";

        FrontmatterResult result = FrontmatterParser.Parse(text);

        result.Error.ShouldBe("Frontmatter is nested too deeply.");
        result.ErrorLine.ShouldBeNull();
        FrontmatterValues.GetString(result.Frontmatter, "name").ShouldBe("a");
        result.Body.ShouldBe("body");
    }

    [Fact]
    public void Parse_ModeratelyNestedFlowCollections_AreParsed()
    {
        string text = "---\ndeep: " + new string('[', 10) + new string(']', 10) + "\n---\n";

        FrontmatterResult result = FrontmatterParser.Parse(text);

        result.Error.ShouldBeNull();
        result.Frontmatter!["deep"].ShouldBeOfType<JsonArray>();
    }

    [Fact]
    public void Parse_UnbalancedClosingBrackets_AreNotCountedAsNesting()
    {
        FrontmatterResult result = FrontmatterParser.Parse("---\nname: \"]]]]\"\n---\n");

        result.Error.ShouldBeNull();
        FrontmatterValues.GetString(result.Frontmatter, "name").ShouldBe("]]]]");
    }

    [Fact]
    public void Parse_OversizedFrontmatter_IsRejectedAndFallsBackToTopLevelKeys()
    {
        string text = "---\nname: x\nblob: " + new string('a', 70_000) + "\n---\nbody";

        FrontmatterResult result = FrontmatterParser.Parse(text);

        result.Error.ShouldNotBeNull().ShouldContain("longer than");
        result.ErrorLine.ShouldBeNull();
        FrontmatterValues.GetString(result.Frontmatter, "name").ShouldBe("x");
        result.Body.ShouldBe("body");
    }

    [Fact]
    public void Parse_FrontmatterContainingAnAnchorAndAlias_ExpandsBothCopies()
    {
        FrontmatterResult result = FrontmatterParser.Parse("---\na: &x [1, 2]\nb: *x\n---\n");

        result.Error.ShouldBeNull();
        result.Frontmatter!["a"].ShouldBeOfType<JsonArray>().Count.ShouldBe(2);
        result.Frontmatter["b"].ShouldBeOfType<JsonArray>().Count.ShouldBe(2);
    }

    // ---- The line of the error --------------------------------------------------------------------------------

    [Fact]
    public void Parse_ErrorBelowManyValidLines_ReportsTheLineCountedFromTheTopOfTheFile()
    {
        const string text = "---\nname: a\ndescription: b\ntype: c\n\n# a comment\nbad: x: y\nlast: z\n---\nbody";

        FrontmatterResult result = FrontmatterParser.Parse(text);

        result.ErrorLine.ShouldBe(7);
        result.Error.ShouldNotBeNull().ShouldStartWith("Line 7, column ");
        result.BodyStartLine.ShouldBe(10);
    }

    [Fact]
    public void Parse_ErrorInTheFirstLineOfTheBlock_IsLineTwoBecauseTheOpeningLineIsLineOne()
    {
        FrontmatterResult result = FrontmatterParser.Parse("---\nbad: x: y\n---\n");

        result.ErrorLine.ShouldBe(2);
        result.Error.ShouldNotBeNull().ShouldStartWith("Line 2, column ");
    }

    [Theory]
    [InlineData("---\nname: a\ndescription: Does X: then Y\n---\nbody")]
    [InlineData("---\r\nname: a\r\ndescription: Does X: then Y\r\n---\r\nbody")]
    [InlineData("---\nname: a\ndescription: Does X: then Y\n...\nbody")]
    public void Parse_LineEndingsAndClosingLine_DoNotChangeTheErrorLine(string text)
    {
        FrontmatterResult result = FrontmatterParser.Parse(text);

        result.ErrorLine.ShouldBe(3);
        FrontmatterValues.GetString(result.Frontmatter, "name").ShouldBe("a");
    }

    [Theory]
    [InlineData("---\nname: foo\ndescription: Does X: then Y\n---\nbody")]
    [InlineData("---\nname: a\nmeta:\n\tkey: 1\n---\nbody")]
    [InlineData("---\nkey: [unclosed\n---\nbody")]
    [InlineData("---\na: 1\na: 2\n---\nbody")]
    [InlineData("---\n? [a, b]\n: c\n---\nbody")]
    [InlineData("---\na: &x [*x]\nb: c\n---\nbody")]
    [InlineData("---\nname: \"unclosed\nother: 1\n---\nbody")]
    [InlineData("---\nname: \"a\\qb\"\n---\nbody")]
    [InlineData("---\na: *nowhere\n---\nbody")]
    public void Parse_ErrorWithAPosition_NamesTheSameLineInErrorLineAndInTheMessage(string text)
    {
        FrontmatterResult result = FrontmatterParser.Parse(text);

        int line = result.ErrorLine.ShouldNotBeNull();
        result.Error.ShouldNotBeNull().ShouldStartWith($"Line {line}, column ");

        // The line is one of the frontmatter block: after the opening "---", before the body.
        line.ShouldBeGreaterThanOrEqualTo(2);
        line.ShouldBeLessThan(result.BodyStartLine);
    }
}
