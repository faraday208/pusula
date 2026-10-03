using System.Text;
using System.Text.Json;
using Pusula.IntegrationTests.Support;
using Shouldly;
using Xunit;

namespace Pusula.IntegrationTests.Api;

/// <summary>
/// Where the error of a malformed frontmatter is: <c>frontmatterErrorLine</c> and <c>frontmatterErrorText</c> of
/// <c>/api/sources/{source}/file</c>, and <c>line</c> of the <c>frontmatterErrors</c> of <c>/api/sources/{source}/overview</c>. The files are made up
/// here, written just as each test wants them, and served by a server of that test's own.
/// </summary>
public sealed class FrontmatterErrorLocationTests
{
    // The colon in the value of line 3 is what YAML cannot read; the body starts on line 6.
    private const string ColonInValue = "---\nname: demo\ndescription: Does X: then Y\npaths: src/**\n---\n# Body\n";

    private const string ColonLine = "description: Does X: then Y";

    private static readonly UTF8Encoding Utf8WithoutByteOrderMark = new(encoderShouldEmitUTF8Identifier: false);

    // The file "rule.md" with exactly this content, as the file endpoint returns it.
    private static async Task<JsonDocument> GetFileAsync(string content, Encoding? encoding = null)
    {
        using var sandbox = new TempDirectory();
        File.WriteAllText(sandbox.Resolve("rule.md"), content, encoding ?? Utf8WithoutByteOrderMark);
        await using var factory = new PusulaFactory(sandbox.Path);
        using HttpClient client = factory.CreateClient();

        return await client.GetJsonAsync(factory.Api("file?path=rule.md"));
    }

    private static bool HasErrorLocation(JsonElement file) =>
        file.TryGetProperty("frontmatterErrorLine", out _) || file.TryGetProperty("frontmatterErrorText", out _);

    // ---- file endpoint: an error with a line ----------------------------------------------------------------------

    [Fact]
    public async Task GetFile_ColonInAValue_NamesTheLineOfTheFileAndGivesTheTextOfThatLine()
    {
        using JsonDocument json = await GetFileAsync(ColonInValue);
        JsonElement file = json.RootElement;

        file.GetProperty("frontmatterError").GetString().ShouldNotBeNull().ShouldStartWith("Line 3, column ");
        file.GetProperty("frontmatterErrorLine").GetInt32().ShouldBe(3);
        file.GetProperty("frontmatterErrorText").GetString().ShouldBe(ColonLine);

        // The line is counted from the top of the file: it is in the frontmatter, above the body.
        file.GetProperty("bodyStartLine").GetInt32().ShouldBe(6);
        file.GetProperty("body").GetString().ShouldBe("# Body\n");
    }

    [Fact]
    public async Task GetFile_ColonInAValue_StillReturnsTheKeysTheFallbackReaderCouldTake()
    {
        using JsonDocument json = await GetFileAsync(ColonInValue);
        JsonElement frontmatter = json.RootElement.GetProperty("frontmatter");

        frontmatter.GetProperty("name").GetString().ShouldBe("demo");
        frontmatter.GetProperty("description").GetString().ShouldBe("Does X: then Y");
        frontmatter.GetProperty("paths").GetString().ShouldBe("src/**");
        json.RootElement.GetProperty("frontmatterErrorLine").GetInt32().ShouldBe(3);
    }

    [Fact]
    public async Task GetFile_ErrorLine_IsTheLineOfTheFileWhateverComesBeforeIt()
    {
        using JsonDocument json = await GetFileAsync("---\nname: demo\ntype: note\n\n# a comment\nbad: x: y  \nlast: z\n---\nbody\n");

        json.RootElement.GetProperty("frontmatterErrorLine").GetInt32().ShouldBe(6);
        json.RootElement.GetProperty("frontmatterErrorText").GetString().ShouldBe("bad: x: y");
        json.RootElement.GetProperty("frontmatterError").GetString().ShouldNotBeNull().ShouldStartWith("Line 6, column ");
    }

    [Fact]
    public async Task GetFile_WindowsLineEndingsAndByteOrderMark_NameTheSameLineAndLeaveNoCarriageReturnInTheText()
    {
        string windows = ColonInValue.Replace("\n", "\r\n", StringComparison.Ordinal);

        using JsonDocument json = await GetFileAsync(windows, new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));

        json.RootElement.GetProperty("frontmatterErrorLine").GetInt32().ShouldBe(3);
        json.RootElement.GetProperty("frontmatterErrorText").GetString().ShouldBe(ColonLine);
    }

    [Fact]
    public async Task GetFile_ErrorLineWithNonAsciiLetters_IsGivenAsWritten()
    {
        const string line = "açıklama: Şablon: dene — “tırnak” ve **kalın**";

        using JsonDocument json = await GetFileAsync($"---\nname: demo\n{line}\n---\nbody\n");

        json.RootElement.GetProperty("frontmatterErrorLine").GetInt32().ShouldBe(3);
        json.RootElement.GetProperty("frontmatterErrorText").GetString().ShouldBe(line);
    }

    [Fact]
    public async Task GetFile_LongErrorLine_IsCutToTwoHundredCharactersEndingWithAnEllipsis()
    {
        string line = "description: Does X: " + new string('y', 300);

        using JsonDocument json = await GetFileAsync($"---\nname: demo\n{line}\n---\nbody\n");

        json.RootElement.GetProperty("frontmatterErrorLine").GetInt32().ShouldBe(3);
        string text = json.RootElement.GetProperty("frontmatterErrorText").GetString().ShouldNotBeNull();
        text.Length.ShouldBe(200);
        text.ShouldBe(line[..199] + "…");
    }

    [Fact]
    public async Task GetFile_ErrorLineOfExactlyTwoHundredCharacters_IsNotCut()
    {
        string line = "description: Does X: " + new string('y', 200 - "description: Does X: ".Length);

        using JsonDocument json = await GetFileAsync($"---\nname: demo\n{line}\n---\nbody\n");

        json.RootElement.GetProperty("frontmatterErrorText").GetString().ShouldBe(line);
    }

    // ---- file endpoint: an error without a line, or no error ------------------------------------------------------

    [Theory]
    [InlineData("---\n- a\n- b\n---\nbody\n", "Frontmatter is not a mapping")]
    [InlineData("---\nname: demo\nlist: [1, 2\nother: 3\n---\nbody\n", "Invalid YAML: ")]
    public async Task GetFile_ErrorWithoutAPosition_KeepsTheMessageAndLeavesTheLineAndTheTextOut(string content, string messageStart)
    {
        using JsonDocument json = await GetFileAsync(content);

        json.RootElement.GetProperty("frontmatterError").GetString().ShouldNotBeNull().ShouldStartWith(messageStart);
        HasErrorLocation(json.RootElement).ShouldBeFalse();
    }

    [Fact]
    public async Task GetFile_InvalidYamlWithoutAPosition_StillReturnsTheKeysTheFallbackReaderCouldTake()
    {
        using JsonDocument json = await GetFileAsync("---\nname: demo\nlist: [1, 2\nother: 3\n---\nbody\n");

        json.RootElement.GetProperty("frontmatter").GetProperty("name").GetString().ShouldBe("demo");
        json.RootElement.GetProperty("frontmatter").GetProperty("other").GetString().ShouldBe("3");
        HasErrorLocation(json.RootElement).ShouldBeFalse();
    }

    [Fact]
    public async Task GetFile_TooDeeplyNestedFrontmatter_HasNoLineButKeepsTheTopLevelKeys()
    {
        using JsonDocument json = await GetFileAsync("---\nname: demo\ndeep: " + new string('[', 100) + new string(']', 100) + "\n---\nbody\n");

        json.RootElement.GetProperty("frontmatterError").GetString().ShouldBe("Frontmatter is nested too deeply.");
        json.RootElement.GetProperty("frontmatter").GetProperty("name").GetString().ShouldBe("demo");
        HasErrorLocation(json.RootElement).ShouldBeFalse();
    }

    [Fact]
    public async Task GetFile_OversizedFrontmatter_HasNoLineButKeepsTheTopLevelKeys()
    {
        using JsonDocument json = await GetFileAsync("---\nname: demo\nblob: " + new string('a', 70_000) + "\n---\nbody\n");

        json.RootElement.GetProperty("frontmatterError").GetString().ShouldNotBeNull().ShouldStartWith("Frontmatter is longer than ");
        json.RootElement.GetProperty("frontmatter").GetProperty("name").GetString().ShouldBe("demo");
        HasErrorLocation(json.RootElement).ShouldBeFalse();
    }

    [Theory]
    [InlineData("---\nname: demo\ndescription: fine\n---\nbody\n")]
    [InlineData("# No frontmatter at all\n")]
    [InlineData("---\n---\nbody\n")]
    public async Task GetFile_FrontmatterThatIsFine_HasNoErrorProperties(string content)
    {
        using JsonDocument json = await GetFileAsync(content);

        json.RootElement.PropertyNames().ShouldNotContain(name => name.StartsWith("frontmatterError", StringComparison.Ordinal));
    }

    // ---- overview endpoint ----------------------------------------------------------------------------------------

    [Fact]
    public async Task GetOverview_FrontmatterErrors_CarryTheLineWhenItIsKnownAndKeepTheMessageOfTheFileEndpoint()
    {
        using var sandbox = new TempDirectory();
        sandbox.Write("a-colon.md", ColonInValue);
        sandbox.Write("b-list.md", "---\n- a\n- b\n---\nbody\n");
        sandbox.Write("c-fine.md", "---\nname: demo\n---\nbody\n");
        await using var factory = new PusulaFactory(sandbox.Path);
        using HttpClient client = factory.CreateClient();

        using JsonDocument overview = await client.GetJsonAsync(factory.Api("overview"));
        using JsonDocument colon = await client.GetJsonAsync(factory.Api("file?path=a-colon.md"));
        using JsonDocument list = await client.GetJsonAsync(factory.Api("file?path=b-list.md"));

        JsonElement[] errors = [.. overview.RootElement.GetProperty("frontmatterErrors").EnumerateArray()];
        errors.Select(error => error.GetProperty("path").GetString()).ShouldBe(["a-colon.md", "b-list.md"]);

        errors[0].PropertyNames().ShouldBe(["path", "error", "line"], ignoreOrder: true);
        errors[0].GetProperty("line").GetInt32().ShouldBe(3);
        errors[0].GetProperty("line").GetInt32().ShouldBe(colon.RootElement.GetProperty("frontmatterErrorLine").GetInt32());
        errors[0].GetProperty("error").GetString().ShouldStartWith("Line 3, column ");
        errors[0].GetProperty("error").GetString().ShouldBe(colon.RootElement.GetProperty("frontmatterError").GetString());

        errors[1].PropertyNames().ShouldBe(["path", "error"], ignoreOrder: true);
        errors[1].GetProperty("error").GetString().ShouldBe("Frontmatter is not a mapping");
        errors[1].GetProperty("error").GetString().ShouldBe(list.RootElement.GetProperty("frontmatterError").GetString());
    }
}
