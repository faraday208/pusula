using Pusula.Indexing;
using Pusula.Sources;
using Pusula.UnitTests.Support;
using Shouldly;
using Xunit;

namespace Pusula.UnitTests.Sources;

public sealed class SourceProfilesTests
{
    [Fact]
    public void Detect_FolderWithAnObsidianDirectory_IsAVault()
    {
        using var temp = new TempDirectory();
        temp.CreateDirectory(".obsidian");
        temp.Write("Note.md", "x");

        SourceProfiles.Detect(temp.Path).ShouldBe(SourceProfile.Vault);
    }

    [Fact]
    public void Detect_ObsidianDirectoryWinsOverTheSignsOfAClaudeFolder()
    {
        using var temp = new TempDirectory();
        temp.CreateDirectory(".obsidian");
        temp.Write("CLAUDE.md", "x");
        temp.CreateDirectory("rules");

        SourceProfiles.Detect(temp.Path).ShouldBe(SourceProfile.Vault);
    }

    [Fact]
    public void Detect_FolderCalledDotClaude_IsAClaudeFolderEvenWhenItIsEmpty()
    {
        using var temp = new TempDirectory();
        string folder = temp.CreateDirectory(".claude");

        SourceProfiles.Detect(folder).ShouldBe(SourceProfile.Claude);
    }

    [Theory]
    [InlineData("rules")]
    [InlineData("skills")]
    [InlineData("projects")]
    [InlineData("agents")]
    [InlineData("commands")]
    public void Detect_ClaudeMdNextToAConfigurationDirectory_IsAClaudeFolder(string directory)
    {
        using var temp = new TempDirectory();
        temp.Write("CLAUDE.md", "x");
        temp.CreateDirectory(directory);

        SourceProfiles.Detect(temp.Path).ShouldBe(SourceProfile.Claude);
    }

    [Fact]
    public void Detect_ClaudeMdAloneOrADirectoryAloneOrAPlainFolder_IsAMarkdownFolder()
    {
        using var onlyFile = new TempDirectory();
        onlyFile.Write("CLAUDE.md", "x");
        using var onlyDirectory = new TempDirectory();
        onlyDirectory.CreateDirectory("rules");
        using var plain = new TempDirectory();
        plain.Write("Note.md", "x");

        SourceProfiles.Detect(onlyFile.Path).ShouldBe(SourceProfile.Markdown);
        SourceProfiles.Detect(onlyDirectory.Path).ShouldBe(SourceProfile.Markdown);
        SourceProfiles.Detect(plain.Path).ShouldBe(SourceProfile.Markdown);
    }

    [Fact]
    public void Detect_FolderThatDoesNotExist_IsJudgedByItsNameAlone()
    {
        using var temp = new TempDirectory();

        SourceProfiles.Detect(temp.Resolve("home/.claude")).ShouldBe(SourceProfile.Claude);
        SourceProfiles.Detect(temp.Resolve("home/notes")).ShouldBe(SourceProfile.Markdown);
    }

    [Theory]
    [InlineData("auto", null, true)]
    [InlineData("AUTO", null, true)]
    [InlineData("claude", SourceProfile.Claude, true)]
    [InlineData("Vault", SourceProfile.Vault, true)]
    [InlineData("MARKDOWN", SourceProfile.Markdown, true)]
    [InlineData("", null, false)]
    [InlineData("obsidian", null, false)]
    [InlineData("1", null, false)]
    [InlineData("Claude, Vault", null, false)]
    public void TryParse_Text_IsOneOfTheFourProfilesInAnyCase(string text, SourceProfile? expected, bool valid)
    {
        SourceProfiles.TryParse(text, out SourceProfile? profile).ShouldBe(valid);

        profile.ShouldBe(expected);
    }

    [Theory]
    [InlineData(SourceProfile.Claude, "claude")]
    [InlineData(SourceProfile.Vault, "vault")]
    [InlineData(SourceProfile.Markdown, "markdown")]
    public void Name_Profile_IsHowASourcesFileSpellsItAndTheReaderTakesItBack(SourceProfile profile, string expected)
    {
        SourceProfiles.Name(profile).ShouldBe(expected);

        SourceProfiles.TryParse(SourceProfiles.Name(profile), out SourceProfile? parsed).ShouldBeTrue();
        parsed.ShouldBe(profile);
    }

    [Fact]
    public void Name_ValueThatIsNoProfile_Throws() =>
        Should.Throw<ArgumentOutOfRangeException>(() => SourceProfiles.Name((SourceProfile)99));
}
