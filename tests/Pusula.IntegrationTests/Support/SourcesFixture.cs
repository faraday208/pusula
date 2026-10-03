using Xunit;

namespace Pusula.IntegrationTests.Support;

/// <summary>
/// Several sources at once: a Claude-like folder, a vault and a folder that does not exist, listed in a sources file.
/// Everything is made up here; the sources file of the user is never read.
/// </summary>
public sealed class SourcesFixture : IAsyncLifetime
{
    public const string ClaudeSecret = "CLAUDE-SECRET-5d2e77";

    private readonly TempDirectory _sandbox = new();
    private PusulaFactory? _factory;

    public HttpClient Client { get; private set; } = null!;

    public string SourcesFile { get; private set; } = string.Empty;

    public string ClaudeFolder => _sandbox.Resolve("claude-like");

    public string VaultFolder => _sandbox.Resolve("vault");

    public string MissingFolder => _sandbox.Resolve("missing-folder");

    public ValueTask InitializeAsync()
    {
        _sandbox.Write("claude-like/CLAUDE.md", "Read [the rule](rules/style.md).\n");
        _sandbox.Write("claude-like/rules/style.md", "---\ndescription: Style\n---\n# Style\n");
        _sandbox.Write("claude-like/settings.json", $$"""{"apiKey":"{{ClaudeSecret}}"}""");

        Directory.CreateDirectory(_sandbox.Resolve("vault/.obsidian"));
        _sandbox.Write(
            "vault/Home.md",
            "---\ntags: [moc]\n---\n# Home\nSee [[Alpha]] and ![[pic.png]] and [[Unwritten]] and [beta](folder/Beta.md).\n#todo\n");
        _sandbox.Write("vault/Alpha.md", "# Alpha\nBack to [[Home]]. #todo #Done\n");
        _sandbox.Write("vault/folder/Beta.md", "# Beta\n");
        Directory.CreateDirectory(_sandbox.Resolve("vault/_attachments"));
        File.WriteAllBytes(_sandbox.Resolve("vault/_attachments/pic.png"), [1, 2, 3]);

        SourcesFile = _sandbox.Write(
            "sources.json",
            $$"""
            {
              // made-up sources
              "sources": [
                { "id": "claude", "name": "Claude Config", "path": "{{ClaudeFolder}}", "profile": "auto" },
                { "name": "Not Defteri", "path": "{{VaultFolder}}" },
                { "path": "{{MissingFolder}}", "name": "Kayıp" },
              ]
            }
            """);

        _factory = PusulaFactory.FromSourcesFile(SourcesFile);
        Client = _factory.CreateClient();
        return ValueTask.CompletedTask;
    }

    public async ValueTask DisposeAsync()
    {
        Client?.Dispose();
        if (_factory is not null)
        {
            await _factory.DisposeAsync();
        }

        _sandbox.Dispose();
    }
}
