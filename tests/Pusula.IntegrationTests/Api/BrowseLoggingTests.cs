using System.Net;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Logging;
using Pusula.IntegrationTests.Support;
using Shouldly;
using Xunit;

namespace Pusula.IntegrationTests.Api;

// What the search for folders says about itself through the logging of the whole application: the Debug lines are for the category of the folder
// finder, and are switched on by the setting that the command line gives (--Logging:LogLevel:Pusula.Browse=Debug).
public sealed class BrowseLoggingTests
{
    private const string FinderCategory = "Pusula.Browse.FolderFinder";

    private static async Task<IReadOnlyList<(string Category, LogLevel Level, string Message)>> SearchAsync(params (string Key, string Value)[] settings)
    {
        var provider = new CapturingLoggerProvider();
        await using var app = new EditingApp
        {
            ConfigureHost = builder =>
            {
                foreach ((string key, string value) in settings)
                {
                    builder.UseSetting(key, value);
                }

                builder.ConfigureLogging(logging => logging.AddProvider(provider));
            },
        };
        app.WriteFile("home/Ideas/Home.md", "# Home\n[[n02]]\n");
        for (int i = 2; i <= 12; i++)
        {
            app.WriteFile($"home/Ideas/n{i:D2}.md", "See [[Home]].\n");
        }

        app.WriteFile("home/Vault/.obsidian/app.json", "{}");
        await app.StartAsync();

        using HttpResponseMessage response = await app.GetFoundAsync();
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        return provider.Entries;
    }

    [Theory]
    [InlineData("Logging:LogLevel:Pusula.Browse")]
    [InlineData("Logging:LogLevel:Pusula.Browse.FolderFinder")]
    public async Task GetFound_DebugSwitchedOnForTheFolderFinder_LogsWhereTheTimeOfTheSearchWent(string key)
    {
        IReadOnlyList<(string Category, LogLevel Level, string Message)> entries = await SearchAsync((key, "Debug"));

        string[] debug = [.. entries.Where(entry => entry.Category == FinderCategory && entry.Level == LogLevel.Debug).Select(entry => entry.Message)];
        debug.ShouldContain(line => line.StartsWith("Search walk: ", StringComparison.Ordinal));
        debug.ShouldContain(line => line.StartsWith("Search candidates: 1 looked into", StringComparison.Ordinal));
        debug.ShouldContain(line => line.StartsWith("Search describe: 3 folders in ", StringComparison.Ordinal));
        debug.ShouldContain(line => line.StartsWith("Search time: ", StringComparison.Ordinal));
        entries.Count(entry => entry.Category == FinderCategory && entry.Level == LogLevel.Information).ShouldBe(1);
    }

    [Fact]
    public async Task GetFound_NothingSwitchedOn_LogsTheSummaryAndNoDetail()
    {
        IReadOnlyList<(string Category, LogLevel Level, string Message)> entries = await SearchAsync();

        entries.Where(entry => entry.Category == FinderCategory).Select(entry => entry.Level).ShouldBe([LogLevel.Information]);
    }

    [Fact]
    public async Task GetFound_DebugSwitchedOnForAnotherCategory_LogsNoDetailOfTheSearch()
    {
        IReadOnlyList<(string Category, LogLevel Level, string Message)> entries = await SearchAsync(("Logging:LogLevel:Pusula.Sources", "Debug"));

        entries.Where(entry => entry.Category == FinderCategory).Select(entry => entry.Level).ShouldBe([LogLevel.Information]);
    }
}
