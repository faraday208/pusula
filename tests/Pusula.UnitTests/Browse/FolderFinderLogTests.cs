using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Pusula.Browse;
using Pusula.Startup;
using Pusula.UnitTests.Support;
using Shouldly;
using Xunit;

namespace Pusula.UnitTests.Browse;

// What a search says about itself at the Debug level, for finding out where the time of a search went.
public sealed class FolderFinderLogTests
{
    private const string Linked = "# Note\nSee [[n02]] for more.\n";

    private static FolderFinder Finder(string home, ILogger<FolderFinder> logger, BrowseLimits? limits = null) =>
        new(new UserDirectories(home, string.Empty), DriveFolders.None, limits ?? BrowseLimits.Default, TimeProvider.System, logger);

    private static void MakeNotes(TempDirectory temp, string folder, int notes)
    {
        for (int i = 1; i <= notes; i++)
        {
            temp.Write($"{folder}/{(i == 1 ? "Home" : $"n{i:D2}")}.md", Linked);
        }
    }

    // A home with a folder of notes at the first level, one at the last level (the fourth) and an empty folder beside it, and a vault.
    private static string MakeHome(TempDirectory temp)
    {
        MakeNotes(temp, "home/Notes", 12);
        MakeNotes(temp, "home/a/b/c/Deep", 12);
        temp.CreateDirectory("home/a/b/c/Plain");
        temp.CreateDirectory("home/Vault/.obsidian");
        return temp.Resolve("home");
    }

    private static string[] Debug(CapturingLogger<FolderFinder> logger) =>
        [.. logger.Entries.Where(entry => entry.Level == LogLevel.Debug).Select(entry => entry.Message)];

    [Fact]
    public void Find_DebugLoggerThatIsOn_GetsALineForEachStageOfTheSearchWithTheNumbersOfTheTree()
    {
        using var temp = new TempDirectory();
        var logger = new CapturingLogger<FolderFinder>();

        Finder(MakeHome(temp), logger).Find(TestContext.Current.CancellationToken);

        string[] lines = Debug(logger);
        // The walk reads home, Notes, a, b and c (the vault is not entered, nor are the folders of the fourth level): 3 + 12 + 1 + 1 + 2 entries.
        lines.ShouldContain(line => line.StartsWith("Search walk: 5 folders read, 19 entries in ", StringComparison.Ordinal));
        lines.ShouldContain("Search walk found: 1 folders with an entry note, 2 folders at the last level, 1 vaults");
        // Notes, and Deep once it was asked and has an entry note: twelve entries each, twelve notes opened each.
        lines.ShouldContain(line => line.StartsWith("Search candidates: 2 looked into (0 cut at the limit of entries for one folder), 24 entries in ", StringComparison.Ordinal)
            && line.EndsWith("; 2 are folders of notes, 0 were not reached", StringComparison.Ordinal));
        lines.ShouldContain(line => line.StartsWith("Search sampling: 24 notes opened in ", StringComparison.Ordinal));
        lines.ShouldContain(line => line.StartsWith("Search last level: 2 folders asked for an entry note, 1 have one, ", StringComparison.Ordinal));
        lines.ShouldContain(line => line.StartsWith("Search budget: not used up (", StringComparison.Ordinal) && line.EndsWith(" entries taken)", StringComparison.Ordinal));
        lines.ShouldContain(line => line.StartsWith("Search describe: 3 folders in ", StringComparison.Ordinal) && line.Contains("used up by nothing", StringComparison.Ordinal));
        lines.ShouldContain(line => line.StartsWith("Search time: ", StringComparison.Ordinal));
        lines.Length.ShouldBe(8);
    }

    [Fact]
    public void Find_TheSummaryLine_IsStillThereAtTheInformationLevelAndComesAfterTheDetails()
    {
        using var temp = new TempDirectory();
        var logger = new CapturingLogger<FolderFinder>();

        Finder(MakeHome(temp), logger).Find(TestContext.Current.CancellationToken);

        logger.Entries[^1].Level.ShouldBe(LogLevel.Information);
        logger.Entries[^1].Message.ShouldStartWith("Searched for vaults: 3 found, complete True, in ");
        logger.Entries.Count(entry => entry.Level == LogLevel.Information).ShouldBe(1);
    }

    [Fact]
    public void Find_BudgetThatRunsOutInTheWalk_SaysSoAndSaysWhatItRanOutOf()
    {
        using var temp = new TempDirectory();
        var logger = new CapturingLogger<FolderFinder>();

        Finder(MakeHome(temp), logger, BrowseLimits.Default with { SearchEntries = 3 }).Find(TestContext.Current.CancellationToken);

        Debug(logger).ShouldContain(line => line.StartsWith("Search budget: used up by entries during the walk, after 3 entries and ", StringComparison.Ordinal));
    }

    [Fact]
    public void Find_BudgetThatRunsOutWhileTheCandidatesAreLookedInto_SaysSo()
    {
        using var temp = new TempDirectory();
        var logger = new CapturingLogger<FolderFinder>();

        // The walk reads 19 entries; the first candidate takes 24 more.
        Finder(MakeHome(temp), logger, BrowseLimits.Default with { SearchEntries = 19 + 5 }).Find(TestContext.Current.CancellationToken);

        Debug(logger).ShouldContain(line => line.StartsWith("Search budget: used up by entries during the looking into the candidates, after 24 entries and ", StringComparison.Ordinal));
    }

    [Fact]
    public void Find_BudgetThatRunsOutOfTime_SaysTimeAndNotEntries()
    {
        using var temp = new TempDirectory();
        var logger = new CapturingLogger<FolderFinder>();

        Finder(MakeHome(temp), logger, BrowseLimits.Default with { SearchTime = TimeSpan.Zero }).Find(TestContext.Current.CancellationToken);

        Debug(logger).ShouldContain(line => line.StartsWith("Search budget: used up by time during the walk, after 0 entries and ", StringComparison.Ordinal));
    }

    [Fact]
    public void Find_CandidateThatHasMoreEntriesThanTheLimit_IsCountedAsCut()
    {
        using var temp = new TempDirectory();
        var logger = new CapturingLogger<FolderFinder>();
        MakeNotes(temp, "home/Notes", 12);

        Finder(temp.Resolve("home"), logger, BrowseLimits.Default with { CheckEntriesPerFolder = 5 }).Find(TestContext.Current.CancellationToken);

        Debug(logger).ShouldContain(line => line.StartsWith("Search candidates: 1 looked into (1 cut at the limit of entries for one folder), 5 entries in ", StringComparison.Ordinal));
    }

    [Fact]
    public void Find_DetailsAreOnlyForTheLogThatAsksForDebug_AndTheSearchDoesTheSameWithoutThem()
    {
        using var temp = new TempDirectory();
        string home = MakeHome(temp);

        FoundFolders quiet = Finder(home, Microsoft.Extensions.Logging.Abstractions.NullLogger<FolderFinder>.Instance).Find(TestContext.Current.CancellationToken);
        FoundFolders loud = Finder(home, new CapturingLogger<FolderFinder>()).Find(TestContext.Current.CancellationToken);

        quiet.Folders.Select(folder => (folder.Path, folder.Kind, folder.MarkdownCount)).ShouldBe([.. loud.Folders.Select(folder => (folder.Path, folder.Kind, folder.MarkdownCount))]);
        quiet.Complete.ShouldBe(loud.Complete);
    }

    // ---- Switching it on from the command line -------------------------------------------------------------------

    // What the host does with its settings: the logging section of the configuration (which the command line is part of) decides which messages of
    // which category each provider gets.
    private static (FolderFinder Finder, CapturingLoggerProvider Provider, ILoggerFactory Factory) FinderWithTheHostsLogging(string home, params string[] commandLine)
    {
        IConfiguration configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Logging:LogLevel:Default"] = "Information", ["Logging:LogLevel:Microsoft.AspNetCore"] = "Warning" })
            .AddCommandLine(commandLine)
            .Build();
        var provider = new CapturingLoggerProvider();
        ILoggerFactory factory = LoggerFactory.Create(logging => logging.AddConfiguration(configuration.GetSection("Logging")).AddProvider(provider));
        return (Finder(home, factory.CreateLogger<FolderFinder>()), provider, factory);
    }

    [Theory]
    [InlineData("--Logging:LogLevel:Pusula.Browse=Debug")]
    [InlineData("--Logging:LogLevel:Pusula.Browse.FolderFinder=Debug")]
    public void Find_DebugSwitchedOnByTheCommandLine_LogsTheDetailsOfTheSearch(string setting)
    {
        using var temp = new TempDirectory();
        (FolderFinder finder, CapturingLoggerProvider provider, ILoggerFactory factory) = FinderWithTheHostsLogging(MakeHome(temp), setting);
        using ILoggerFactory _ = factory;

        finder.Find(TestContext.Current.CancellationToken);

        provider.Entries.ShouldAllBe(entry => entry.Category == "Pusula.Browse.FolderFinder");
        provider.Entries.Count(entry => entry.Level == LogLevel.Debug).ShouldBe(8);
        provider.Entries.Count(entry => entry.Level == LogLevel.Information).ShouldBe(1);
    }

    [Fact]
    public void Find_DebugSwitchedOnByTheCommandLineWithTheValueAfterTheOption_LogsTheDetailsToo()
    {
        using var temp = new TempDirectory();
        (FolderFinder finder, CapturingLoggerProvider provider, ILoggerFactory factory) = FinderWithTheHostsLogging(MakeHome(temp), "--Logging:LogLevel:Pusula.Browse", "Debug");
        using ILoggerFactory _ = factory;

        finder.Find(TestContext.Current.CancellationToken);

        provider.Entries.Count(entry => entry.Level == LogLevel.Debug).ShouldBe(8);
    }

    [Fact]
    public void Find_NothingSwitchedOn_LogsTheSummaryOnly()
    {
        using var temp = new TempDirectory();
        (FolderFinder finder, CapturingLoggerProvider provider, ILoggerFactory factory) = FinderWithTheHostsLogging(MakeHome(temp));
        using ILoggerFactory _ = factory;

        finder.Find(TestContext.Current.CancellationToken);

        provider.Entries.Select(entry => entry.Level).ShouldBe([LogLevel.Information]);
    }

    [Fact]
    public void Find_DebugSwitchedOnForAnotherCategory_DoesNotLogTheDetails()
    {
        using var temp = new TempDirectory();
        (FolderFinder finder, CapturingLoggerProvider provider, ILoggerFactory factory) = FinderWithTheHostsLogging(MakeHome(temp), "--Logging:LogLevel:Pusula.Sources=Debug");
        using ILoggerFactory _ = factory;

        finder.Find(TestContext.Current.CancellationToken);

        provider.Entries.Select(entry => entry.Level).ShouldBe([LogLevel.Information]);
    }
}
