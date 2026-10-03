using System.Text.Json.Nodes;
using Pusula.Indexing;
using Pusula.Links;
using Pusula.UnitTests.Support;
using Shouldly;
using Xunit;

namespace Pusula.UnitTests.Indexing;

public sealed class NoteTagsTests
{
    private static string[] Tags(string body, JsonObject? frontmatter = null)
    {
        NoteTags.TryExtract(frontmatter, body, out IReadOnlyList<string> tags).ShouldBeTrue();
        return [.. tags];
    }

    private static JsonObject Frontmatter(string key, JsonNode value) => new() { [key] = value };

    // ---- Frontmatter ------------------------------------------------------------------------------------------

    [Fact]
    public void TryExtract_FrontmatterList_GivesEachEntry() =>
        Tags(string.Empty, Frontmatter("tags", new JsonArray("moc", "#index", " spaced "))).ShouldBe(["moc", "index", "spaced"]);

    [Theory]
    [InlineData("moc, index")]
    [InlineData("moc index")]
    [InlineData("#moc,#index")]
    [InlineData("  moc ,\n index  ")]
    public void TryExtract_FrontmatterText_IsSplitAtCommasAndWhiteSpace(string text) =>
        Tags(string.Empty, Frontmatter("tags", text)).ShouldBe(["moc", "index"]);

    [Fact]
    public void TryExtract_FrontmatterKeyTag_IsReadToo() =>
        Tags(string.Empty, Frontmatter("tag", "single")).ShouldBe(["single"]);

    [Fact]
    public void TryExtract_BothKeys_GiveTagsFirstThenTag()
    {
        var frontmatter = new JsonObject { ["tag"] = "b", ["tags"] = new JsonArray("a") };

        Tags(string.Empty, frontmatter).ShouldBe(["a", "b"]);
    }

    [Fact]
    public void TryExtract_EmptyOrOddFrontmatterValues_GiveNothing()
    {
        Tags(string.Empty, Frontmatter("tags", string.Empty)).ShouldBeEmpty();
        Tags(string.Empty, Frontmatter("tags", new JsonArray())).ShouldBeEmpty();
        Tags(string.Empty, Frontmatter("tags", new JsonArray(new JsonArray("nested"), new JsonObject()))).ShouldBeEmpty();
        Tags(string.Empty, Frontmatter("tags", new JsonObject())).ShouldBeEmpty();
        Tags(string.Empty, new JsonObject { ["title"] = "no tags here" }).ShouldBeEmpty();
        Tags(string.Empty).ShouldBeEmpty();
    }

    // ---- Tags in the text -------------------------------------------------------------------------------------

    [Theory]
    [InlineData("#todo", "todo")]
    [InlineData("text #todo more", "todo")]
    [InlineData("line one\n#second-line", "second-line")]
    [InlineData("#project/sub_tag-1", "project/sub_tag-1")]
    [InlineData("#çalışma ve #Şablon", "çalışma")]
    [InlineData("#y2024", "y2024")]
    [InlineData("#2024a", "2024a")]
    [InlineData("done: #tag, next", "tag")]
    [InlineData("(see #tag.)", "tag")]
    public void TryExtract_TagInTheText_IsFoundWithoutTheHash(string body, string first) =>
        Tags(body).First().ShouldBe(first);

    [Theory]
    [InlineData("# Heading")]
    [InlineData("## Heading")]
    [InlineData("##no")]
    [InlineData("a#b")]
    [InlineData("https://example.com/page#anchor")]
    [InlineData("#2024")]
    [InlineData("#1")]
    [InlineData("#")]
    [InlineData("price is 5 # not a tag")]
    public void TryExtract_ThingsThatAreNoTags_AreLeftAlone(string body) =>
        Tags(body).ShouldBeEmpty();

    [Theory]
    [InlineData("`#code`")]
    [InlineData("a ``#double`` b")]
    [InlineData("```\n#fenced\n```")]
    [InlineData("~~~\n#tilde fenced\n~~~")]
    [InlineData("[link #text](Note.md)")]
    [InlineData("[[Note#Heading]]")]
    [InlineData("[[Note #tag-like]]")]
    [InlineData("![[Note #tag-like]]")]
    [InlineData("`code`#glued")]
    public void TryExtract_TagsInsideCodeAndLinks_DoNotCount(string body) =>
        Tags(body).ShouldBeEmpty();

    [Fact]
    public void TryExtract_TagsAroundCode_StillCount() =>
        Tags("#before `#in code` #after\n```\n#fenced\n```\n#last").ShouldBe(["before", "after", "last"]);

    // ---- Together ---------------------------------------------------------------------------------------------

    [Fact]
    public void TryExtract_FrontmatterThenBody_EachTagOnceIgnoringCaseWithItsFirstSpelling()
    {
        JsonObject frontmatter = Frontmatter("tags", new JsonArray("Todo", "moc"));

        Tags("#TODO #Later #later\n#MOC #other", frontmatter).ShouldBe(["Todo", "moc", "Later", "other"]);
    }

    [Fact]
    public void TryExtract_BodyThatMakesAPatternRunOutOfTime_ReturnsFalseAndKeepsTheFrontmatterTags()
    {
        LinkExtractor.PatternSet patterns = LinkExtractor.PatternSet.WithTimeout(TimeSpan.FromMilliseconds(50));
        string body = "#before\n" + PathologicalMarkdown.UnclosedBacktickRuns(1_000_000, "a");

        bool completed = NoteTags.TryExtract(Frontmatter("tags", new JsonArray("kept")), body, out IReadOnlyList<string> tags, patterns);

        completed.ShouldBeFalse();
        tags.ShouldBe(["kept"]);
    }
}
