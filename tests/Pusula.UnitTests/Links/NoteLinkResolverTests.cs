using Pusula.Links;
using Shouldly;
using Xunit;

namespace Pusula.UnitTests.Links;

// A made-up vault, in memory: nothing here touches the disk, because the resolver never does.
public sealed class NoteLinkResolverTests
{
    private const string Root = "/vaults/demo";

    private static readonly string[] Notes =
    [
        "Home.md",
        "Alpha.md",
        "Beta.md",
        "folder/Beta.md",
        "folder/Gamma.md",
        "folder/Sibling.md",
        "a/Dup.md",
        "a/Linker.md",
        "b/Dup.md",
        "b/Linker.md",
        "x/Same.md",
        "y/z/Same.md",
        "deep/er/Name.md",
        "Ünite/Şablon.md",
        "My Note.md",
    ];

    private static readonly string[] OtherFiles =
    [
        "_attachments/pic.png",
        "_attachments/Doc.PDF",
        "_bases/Kararlar.base",
        "folder/data.csv",
        "big.md",
    ];

    private static readonly NoteLinkResolver Resolver = new(new NoteLinkContext(Root, Notes, [.. Notes, .. OtherFiles]));

    private static string Resolve(LinkKind kind, string raw, string source = "Home.md")
    {
        Link? link = Resolver.Resolve(new RawLink(kind, raw, 7), source);
        link.ShouldNotBeNull();
        link.Kind.ShouldBe(kind);
        link.Raw.ShouldBe(raw);
        link.Line.ShouldBe(7);
        return $"{link.Status}|{link.Target ?? "-"}|{link.Heading ?? "-"}";
    }

    private static string Wiki(string raw, string source = "Home.md") => Resolve(LinkKind.WikiLink, raw, source);

    private static string Md(string raw, string source = "Home.md") => Resolve(LinkKind.MarkdownLink, raw, source);

    // ---- Wiki links: the order of the lookup ------------------------------------------------------------------

    [Theory]
    [InlineData("Alpha", "Resolved|Alpha.md|-")]
    [InlineData("Alpha.md", "Resolved|Alpha.md|-")]
    [InlineData("alpha", "Resolved|Alpha.md|-")]
    [InlineData("folder/Gamma", "Resolved|folder/Gamma.md|-")]
    [InlineData("folder/Gamma.md", "Resolved|folder/Gamma.md|-")]
    [InlineData("Gamma", "Resolved|folder/Gamma.md|-")]
    [InlineData("er/Name", "Resolved|deep/er/Name.md|-")]
    [InlineData("Name", "Resolved|deep/er/Name.md|-")]
    [InlineData("my note", "Resolved|My Note.md|-")]
    [InlineData("ünite/şablon", "Resolved|Ünite/Şablon.md|-")]
    [InlineData("ŞABLON", "Resolved|Ünite/Şablon.md|-")]
    public void Resolve_WikiLink_FindsTheNoteByPathOrByTheEndOfItsPathIgnoringCase(string raw, string expected) =>
        Wiki(raw).ShouldBe(expected);

    [Fact]
    public void Resolve_WikiLink_PathFromTheRootOfTheVaultWinsOverTheEndOfAnotherPath()
    {
        // Beta.md exists at the root and in folder/: a link written from inside folder/ still means the root's.
        Wiki("Beta", source: "folder/Sibling.md").ShouldBe("Resolved|Beta.md|-");
        Wiki("folder/Beta", source: "Home.md").ShouldBe("Resolved|folder/Beta.md|-");
    }

    [Fact]
    public void Resolve_WikiLink_PathFromTheFolderOfTheSourceComesBeforeTheEndOfAnotherPath()
    {
        // "Sibling" is not at the root; from folder/ the path "Sibling" means folder/Sibling.md, and so would the name.
        Wiki("Sibling", source: "folder/Gamma.md").ShouldBe("Resolved|folder/Sibling.md|-");

        // "z/Same" is the end of y/z/Same.md, and from y/ it is also the path y/z/Same.md.
        Wiki("z/Same", source: "y/Home.md").ShouldBe("Resolved|y/z/Same.md|-");
    }

    [Fact]
    public void Resolve_WikiLink_OfSeveralNotesWithTheSameName_PrefersTheFolderOfTheSourceThenTheShortestPath()
    {
        Wiki("Dup", source: "a/Linker.md").ShouldBe("Resolved|a/Dup.md|-");
        Wiki("Dup", source: "b/Linker.md").ShouldBe("Resolved|b/Dup.md|-");

        // Nowhere near: x/Same.md is shorter than y/z/Same.md; of equal length the first in ordinal order.
        Wiki("Same", source: "Home.md").ShouldBe("Resolved|x/Same.md|-");
        Wiki("Dup", source: "Home.md").ShouldBe("Resolved|a/Dup.md|-");
    }

    [Theory]
    [InlineData("./Gamma", "folder/Beta.md", "Resolved|folder/Gamma.md|-")]
    [InlineData("./Gamma.md", "folder/Beta.md", "Resolved|folder/Gamma.md|-")]
    [InlineData("../Home", "folder/Beta.md", "Resolved|Home.md|-")]
    [InlineData("../folder/Gamma", "a/Linker.md", "Resolved|folder/Gamma.md|-")]
    [InlineData("./Alpha", "Home.md", "Resolved|Alpha.md|-")]
    public void Resolve_WikiLinkThatStartsWithADotSegment_IsRelativeToTheFolderOfTheSource(string raw, string source, string expected) =>
        Wiki(raw, source).ShouldBe(expected);

    [Fact]
    public void Resolve_WikiLinkThatStartsWithADotSegment_IsNeverLookedUpAnywhereElse() =>
        Wiki("./Alpha", source: "folder/Beta.md").ShouldBe("Pending|-|-");

    [Theory]
    [InlineData("../../escape", "folder/Beta.md")]
    [InlineData("../escape", "Home.md")]
    [InlineData("folder/../../escape", "Home.md")]
    [InlineData("a\\b", "Home.md")]
    public void Resolve_WikiLinkThatLeavesTheVault_IsExternal(string raw, string source) =>
        Wiki(raw, source).ShouldBe("External|-|-");

    [Theory]
    [InlineData("Not Written Yet")]
    [InlineData("folder/Missing")]
    [InlineData("./nothing")]
    [InlineData("/")]
    [InlineData(".")]
    public void Resolve_WikiLinkToANoteThatDoesNotExist_IsPendingNotBroken(string raw) =>
        Wiki(raw, source: "folder/Beta.md").ShouldBe("Pending|-|-");

    [Theory]
    [InlineData("Gamma#Heading|alias", "Resolved|folder/Gamma.md|Heading")]
    [InlineData("Gamma#Heading", "Resolved|folder/Gamma.md|Heading")]
    [InlineData("Gamma|alias", "Resolved|folder/Gamma.md|-")]
    [InlineData("Gamma#", "Resolved|folder/Gamma.md|-")]
    [InlineData("Gamma #  Spaced heading ", "Resolved|folder/Gamma.md|Spaced heading")]
    [InlineData("Missing#Heading", "Pending|-|Heading")]
    [InlineData("#Local", "Resolved|Home.md|Local")]
    [InlineData("|alias", "Resolved|Home.md|-")]
    public void Resolve_WikiLink_SplitsOffTheHeadingAndTheAlias(string raw, string expected) =>
        Wiki(raw).ShouldBe(expected);

    // ---- Attachments ------------------------------------------------------------------------------------------

    [Theory]
    [InlineData("pic.png", "NonMarkdown|_attachments/pic.png|-")]
    [InlineData("PIC.PNG", "NonMarkdown|_attachments/pic.png|-")]
    [InlineData("_attachments/pic.png", "NonMarkdown|_attachments/pic.png|-")]
    [InlineData("Doc.pdf", "NonMarkdown|_attachments/Doc.PDF|-")]
    [InlineData("Kararlar.base", "NonMarkdown|_bases/Kararlar.base|-")]
    [InlineData("data.csv", "NonMarkdown|folder/data.csv|-")]
    [InlineData("big", "NonMarkdown|big.md|-")]
    public void Resolve_LinkToAFileThatIsNotANote_IsNonMarkdown(string raw, string expected)
    {
        Wiki(raw).ShouldBe(expected);
        Resolve(LinkKind.Embed, raw).ShouldBe(expected);
    }

    // [[pic]] means the note "pic", never the image: only the whole file name finds an attachment.
    [Fact]
    public void Resolve_LinkToAnAttachmentWithoutItsExtension_IsPending() =>
        Wiki("pic").ShouldBe("Pending|-|-");

    [Fact]
    public void Resolve_Embed_IsResolvedLikeAWikiLinkAndKeepsItsKind()
    {
        Resolve(LinkKind.Embed, "Alpha#Section|300").ShouldBe("Resolved|Alpha.md|Section");
        Resolve(LinkKind.Embed, "Unwritten").ShouldBe("Pending|-|-");
        Resolve(LinkKind.Embed, "../../x").ShouldBe("External|-|-");
    }

    // ---- Markdown links ---------------------------------------------------------------------------------------

    [Theory]
    [InlineData("Alpha.md", "Home.md", "Resolved|Alpha.md|-")]
    [InlineData("alpha.md", "Home.md", "Resolved|Alpha.md|-")]
    [InlineData("Alpha", "Home.md", "Resolved|Alpha.md|-")]
    [InlineData("folder/Beta.md#Top", "Home.md", "Resolved|folder/Beta.md|Top")]
    [InlineData("My%20Note.md", "Home.md", "Resolved|My Note.md|-")]
    [InlineData("<My Note.md>", "Home.md", "Resolved|My Note.md|-")]
    [InlineData("Gamma.md", "folder/Beta.md", "Resolved|folder/Gamma.md|-")]
    [InlineData("./Gamma", "folder/Beta.md", "Resolved|folder/Gamma.md|-")]
    [InlineData("Home.md", "folder/Beta.md", "Resolved|Home.md|-")]
    [InlineData("../Home.md", "folder/Beta.md", "Resolved|Home.md|-")]
    [InlineData("Dup.md", "Home.md", "Resolved|a/Dup.md|-")]
    [InlineData("Dup.md", "b/Linker.md", "Resolved|b/Dup.md|-")]
    [InlineData("_attachments/pic.png", "Home.md", "NonMarkdown|_attachments/pic.png|-")]
    [InlineData("pic.png", "folder/Beta.md", "NonMarkdown|_attachments/pic.png|-")]
    [InlineData("#Heading", "Home.md", "Resolved|Home.md|Heading")]
    public void Resolve_MarkdownLink_FindsTheFileFromTheSourceThenFromTheRootThenByName(string raw, string source, string expected) =>
        Md(raw, source).ShouldBe(expected);

    [Theory]
    [InlineData("nope.md", "Home.md", "Broken|nope.md|-")]
    [InlineData("nope.md#Top", "folder/Beta.md", "Broken|folder/nope.md|Top")]
    [InlineData("folder/Missing", "Home.md", "Broken|folder/Missing|-")]
    public void Resolve_MarkdownLinkToAFileThatDoesNotExist_IsBrokenAndPointsWhereItWouldHaveToPoint(string raw, string source, string expected) =>
        Md(raw, source).ShouldBe(expected);

    [Theory]
    [InlineData("../../x.md", "folder/Beta.md")]
    [InlineData("../x.md", "Home.md")]
    [InlineData("~/notes/x.md", "Home.md")]
    [InlineData("/etc/passwd", "Home.md")]
    [InlineData("/vaults/other/Alpha.md", "Home.md")]
    public void Resolve_MarkdownLinkThatLeavesTheVault_IsExternal(string raw, string source) =>
        Md(raw, source).ShouldBe("External|-|-");

    [Theory]
    [InlineData("/vaults/demo/Alpha.md", "Resolved|Alpha.md|-")]
    [InlineData("file:///vaults/demo/folder/Gamma.md", "Resolved|folder/Gamma.md|-")]
    [InlineData("/vaults/demo/missing.md", "Broken|missing.md|-")]
    [InlineData("/vaults/demo", "NonMarkdown|-|-")]
    public void Resolve_MarkdownLinkWithAnAbsolutePath_IsLookedUpWhenItLiesUnderTheVault(string raw, string expected) =>
        Md(raw).ShouldBe(expected);

    [Fact]
    public void Resolve_MarkdownLinkToTheVaultItself_IsNotAFile() =>
        Md(".", source: "Home.md").ShouldBe("NonMarkdown|-|-");

    // ---- Not references of a note -----------------------------------------------------------------------------

    [Theory]
    [InlineData(LinkKind.ClaudePath)]
    [InlineData(LinkKind.RelativePath)]
    public void Resolve_PathReferencesOfAClaudeFolder_AreNotLinksOfANote(LinkKind kind) =>
        Resolver.Resolve(new RawLink(kind, "rules/x.md", 1), "Home.md").ShouldBeNull();
}
