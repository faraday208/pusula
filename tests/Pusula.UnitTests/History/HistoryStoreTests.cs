using System.Globalization;
using System.Runtime.Versioning;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Pusula.History;
using Pusula.Indexing;
using Pusula.Links;
using Pusula.Startup;
using Pusula.UnitTests.Support;
using Shouldly;
using Xunit;

namespace Pusula.UnitTests.History;

public sealed class HistoryStoreTests
{
    private static readonly DateOnly Today = new(2026, 10, 12);
    private static readonly DateTimeOffset Morning = new(2026, 10, 12, 9, 14, 3, TimeSpan.Zero);
    private static readonly string Root = Path.GetFullPath("/shown/claude");

    private static ConfigIndex Index(params ConfigFile[] files) => IndexFactory.Index(files, root: Root);

    private static ConfigFile Rule(string path, int total, int everySession = 0, params Link[] links) =>
        IndexFactory.File(path, tokens: new TokenCounts(total, everySession, 0), links: links);

    private static Link Broken(string raw, int line = 1, LinkKind kind = LinkKind.MarkdownLink) =>
        IndexFactory.Link(kind, raw, line, LinkStatus.Broken, raw);

    private static DateOnly Date(string day) => DateOnly.ParseExact(day, "yyyy-MM-dd", CultureInfo.InvariantCulture);

    // The summary of a day, taken at eight in the morning (UTC), of the files given as (path, tokens, every-session tokens).
    private static DailySummary Summary(string day, params (string Path, int Total, int EverySession)[] files) =>
        new(
            Date(day),
            new DateTimeOffset(Date(day).ToDateTime(new TimeOnly(8, 0)), TimeSpan.Zero),
            files.ToDictionary(file => file.Path, file => new FileTokens(file.Total, file.EverySession), DailySummary.Keys),
            []);

    // A store over a scratch directory, with a clock that is in the time zone it is given (UTC by default) and a logger that records.
    private sealed class Harness : IDisposable
    {
        public Harness(DateTimeOffset? now = null, TimeZoneInfo? zone = null, bool withState = true)
        {
            Clock = new ManualTimeProvider(now ?? Morning, zone ?? TimeZoneInfo.Utc);
            Directories = new UserDirectories(Temp.Resolve("home"), Temp.Resolve("data"), withState ? State : string.Empty);
            Store = new HistoryStore(Directories, Clock, Logger);
        }

        public TempDirectory Temp { get; } = new();

        public ManualTimeProvider Clock { get; }

        public CapturingLogger<HistoryStore> Logger { get; } = new();

        public UserDirectories Directories { get; }

        public HistoryStore Store { get; }

        public string State => Temp.Resolve("state");

        public string Folder => Path.Join(State, "pusula", "history");

        public ChangesResponse Ask(ConfigIndex index, params string[] shown) => Store.Changes(index, shown.Length == 0 ? [index.Root] : shown);

        public string FileOf(ConfigIndex index) => Path.Join(Folder, HistoryFile.NameOf(index.Profile, HistoryFile.RootOf(index)));

        // The files in the history folder, by name; none when there is no folder.
        public string[] Files() =>
            Directory.Exists(Folder) ? [.. Directory.GetFileSystemEntries(Folder).Select(path => Path.GetFileName(path)).Order(StringComparer.Ordinal)] : [];

        // What another pusula would do: put a history file of the given days where this one looks.
        public void Prepare(ConfigIndex index, params DailySummary[] days) =>
            HistoryFile.Write(Folder, HistoryFile.NameOf(index.Profile, HistoryFile.RootOf(index)), index.Profile, HistoryFile.RootOf(index), days);

        public JsonDocument Read(ConfigIndex index) => JsonDocument.Parse(File.ReadAllText(FileOf(index)));

        // The days of the history file of a source, in the order they are written.
        public string[] StoredDays(ConfigIndex index)
        {
            using JsonDocument file = Read(index);
            return [.. file.RootElement.GetProperty("days").EnumerateArray().Select(day => day.GetProperty("day").GetString()!)];
        }

        public string[] Messages(LogLevel level) => [.. Logger.Entries.Where(entry => entry.Level == level).Select(entry => entry.Message)];

        public void Dispose() => Temp.Dispose();
    }

    // ---- Where the history is kept -----------------------------------------------------------------------------

    [Fact]
    public void Changes_WithoutAStateDirectory_IsOffAndWritesNothing()
    {
        using var harness = new Harness(withState: false);

        ChangesResponse response = harness.Ask(Index(Rule("a.md", 5)));

        response.ShouldBe(new ChangesResponse(HistoryState.Off));
        Directory.GetFileSystemEntries(harness.Temp.Path, "*", SearchOption.AllDirectories).ShouldBeEmpty();
        harness.Logger.Entries.ShouldBeEmpty();
    }

    [Fact]
    public void Changes_FirstRequestOfTheDay_KeepsTodaysSummaryInOneNewFileAndHasNothingToCompareWith()
    {
        using var harness = new Harness();
        ConfigIndex index = Index(Rule("CLAUDE.md", 3495, 3495, Broken("~/.claude/X.md", kind: LinkKind.ClaudePath)), Rule("rules/a.md", 800));

        ChangesResponse response = harness.Ask(index);

        response.ShouldBe(new ChangesResponse(HistoryState.Kept, Today));
        harness.Files().ShouldBe([HistoryFile.NameOf(SourceProfile.Claude, HistoryFile.RootOf(index))]);
        using JsonDocument file = harness.Read(index);
        JsonElement day = file.RootElement.GetProperty("days").EnumerateArray().Single();
        file.RootElement.GetProperty("version").GetInt32().ShouldBe(1);
        file.RootElement.GetProperty("root").GetString().ShouldBe(HistoryFile.RootOf(index));
        file.RootElement.GetProperty("profile").GetString().ShouldBe("Claude");
        day.GetProperty("day").GetString().ShouldBe("2026-10-12");
        day.GetProperty("takenAt").GetDateTimeOffset().ShouldBe(Morning);
        day.GetProperty("files").GetProperty("CLAUDE.md").EnumerateArray().Select(count => count.GetInt32()).ShouldBe([3495, 3495]);
        day.GetProperty("files").GetProperty("rules/a.md").EnumerateArray().Select(count => count.GetInt32()).ShouldBe([800, 0]);
        day.GetProperty("broken").EnumerateArray().Single().EnumerateArray().Select(part => part.GetString()).ShouldBe(["CLAUDE.md", "ClaudePath", "~/.claude/X.md"]);
    }

    [Fact]
    public void Changes_FirstRequestOfTheDay_SaysSoOnceAndHasNothingToWarnAbout()
    {
        using var harness = new Harness();
        ConfigIndex index = Index(Rule("a.md", 5), Rule("b.md", 6));

        harness.Ask(index);

        harness.Messages(LogLevel.Warning).ShouldBeEmpty();
        string message = harness.Messages(LogLevel.Information).Single();
        message.ShouldBe($"Kept the summary of 2026-10-12 of {HistoryFile.RootOf(index)}: 2 files, in {harness.FileOf(index)}");
    }

    [Fact]
    public void Changes_SecondRequestOfTheSameDay_DoesNotWriteAgainAndDoesNotCompareWithToday()
    {
        using var harness = new Harness();
        harness.Ask(Index(Rule("a.md", 5)));
        string file = harness.FileOf(Index());
        byte[] written = File.ReadAllBytes(file);
        DateTime writtenAt = File.GetLastWriteTimeUtc(file);
        harness.Clock.Advance(TimeSpan.FromHours(3));

        ChangesResponse again = harness.Ask(Index(Rule("a.md", 5), Rule("b.md", 9)));

        again.ShouldBe(new ChangesResponse(HistoryState.Kept, Today));
        File.ReadAllBytes(file).ShouldBe(written);
        File.GetLastWriteTimeUtc(file).ShouldBe(writtenAt);
        harness.Messages(LogLevel.Information).Length.ShouldBe(1);
    }

    [Fact]
    public void Changes_Summaries_AreKeptInMemoryAfterTheFirstRead()
    {
        using var harness = new Harness();
        ConfigIndex index = Index(Rule("a.md", 5));
        harness.Ask(index);
        File.Delete(harness.FileOf(index));

        ChangesResponse again = harness.Ask(index);

        again.ShouldBe(new ChangesResponse(HistoryState.Kept, Today));
        File.Exists(harness.FileOf(index)).ShouldBeFalse();
    }

    [Fact]
    public void Changes_AfterARestartOnTheSameDay_FindsTodaysSummaryInTheFileAndDoesNotWriteItAgain()
    {
        using var harness = new Harness();
        ConfigIndex index = Index(Rule("a.md", 5));
        harness.Ask(index);
        byte[] written = File.ReadAllBytes(harness.FileOf(index));
        var logger = new CapturingLogger<HistoryStore>();
        var restarted = new HistoryStore(harness.Directories, harness.Clock, logger);

        ChangesResponse response = restarted.Changes(Index(Rule("a.md", 5), Rule("b.md", 9)), [Root]);

        response.ShouldBe(new ChangesResponse(HistoryState.Kept, Today));
        File.ReadAllBytes(harness.FileOf(index)).ShouldBe(written);
        logger.Entries.ShouldBeEmpty();
    }

    [Fact]
    public void Changes_Sources_HaveTheirOwnFiles()
    {
        using var harness = new Harness();
        ConfigIndex claude = IndexFactory.Index([Rule("a.md", 5)], root: Path.GetFullPath("/shown/one"));
        ConfigIndex vault = IndexFactory.Index([Rule("v.md", 7)], root: Path.GetFullPath("/shown/two"), profile: SourceProfile.Vault);
        ConfigIndex sameFolderOtherProfile = IndexFactory.Index([Rule("m.md", 9)], root: Path.GetFullPath("/shown/two"), profile: SourceProfile.Markdown);
        harness.Ask(claude);
        harness.Ask(vault);
        harness.Ask(sameFolderOtherProfile);
        harness.Clock.Advance(TimeSpan.FromDays(1));

        ChangesResponse next = harness.Ask(IndexFactory.Index([Rule("v.md", 7), Rule("w.md", 3)], root: Path.GetFullPath("/shown/two"), profile: SourceProfile.Vault));

        harness.Files().Length.ShouldBe(3);
        next.PreviousDay!.Added.Select(file => file.Path).ShouldBe(["w.md"]);
        next.PreviousDay.Removed.ShouldBeEmpty();
    }

    // ---- What it is compared with ---------------------------------------------------------------------------------

    [Fact]
    public void Changes_NextDay_KeepsAnotherSummaryAndComparesWithTheDayBefore()
    {
        using var harness = new Harness();
        harness.Ask(Index(Rule("CLAUDE.md", 100, 100), Rule("old.md", 30)));
        harness.Clock.Advance(TimeSpan.FromDays(1));
        ConfigIndex now = Index(Rule("CLAUDE.md", 120, 120), Rule("new.md", 50, 50, Broken("gone.md", 4)));

        ChangesResponse response = harness.Ask(now);

        response.History.ShouldBe(HistoryState.Kept);
        response.Error.ShouldBeNull();
        response.FirstDay.ShouldBe(Today);
        response.PreviousWeek.ShouldBeNull();
        ChangesSince day = response.PreviousDay!;
        day.Day.ShouldBe(Today);
        day.TakenAt.ShouldBe(Morning);
        day.FilesBefore.ShouldBe(2);
        day.FilesAfter.ShouldBe(2);
        day.EverySessionTokensBefore.ShouldBe(100);
        day.EverySessionTokensAfter.ShouldBe(170);
        day.Added.ShouldBe([new ChangedFile("new.md", 50, 50, LoadMode.EverySession)]);
        day.Removed.ShouldBe([new ChangedFile("old.md", 30, 0)]);
        day.Resized.ShouldBe([new ResizedFile("CLAUDE.md", LoadMode.EverySession, 100, 120, 100, 120)]);
        day.NewlyBroken.ShouldBe([new ChangedLink("new.md", LinkKind.MarkdownLink, "gone.md", 4)]);
        day.NoLongerBroken.ShouldBeEmpty();
        harness.StoredDays(now).ShouldBe(["2026-10-12", "2026-10-13"]);
    }

    [Fact]
    public void Changes_PreviousDay_SkipsTheDaysPusulaWasNotOpened()
    {
        using var harness = new Harness();
        harness.Ask(Index(Rule("a.md", 1)));
        harness.Clock.Advance(TimeSpan.FromDays(3));

        ChangesResponse response = harness.Ask(Index(Rule("a.md", 1), Rule("b.md", 1)));

        response.PreviousDay!.Day.ShouldBe(Today);
        response.PreviousDay.Added.Select(file => file.Path).ShouldBe(["b.md"]);
    }

    [Fact]
    public void Changes_PreviousDay_IsTheNewestSummaryBeforeTodayAndTodaysOwnIsNeverComparedWith()
    {
        using var harness = new Harness();
        ConfigIndex index = Index(Rule("now.md", 1));
        harness.Prepare(index, Summary("2026-10-09", ("d09.md", 1, 0)), Summary("2026-10-11", ("d11.md", 1, 0)), Summary("2026-10-12", ("d12.md", 1, 0)));

        ChangesResponse response = harness.Ask(index);

        response.History.ShouldBe(HistoryState.Kept);
        response.PreviousDay!.Day.ShouldBe(new DateOnly(2026, 10, 11));
        response.PreviousDay.Removed.Select(file => file.Path).ShouldBe(["d11.md"]);
    }

    [Fact]
    public void Changes_PreviousWeek_IsTheNewestSummaryOfSevenDaysAgoOrEarlier()
    {
        using var harness = new Harness();
        ConfigIndex index = Index(Rule("now.md", 1));
        harness.Prepare(
            index,
            Summary("2026-10-02", ("d02.md", 1, 0)),
            Summary("2026-10-04", ("d04.md", 1, 0)),
            Summary("2026-10-05", ("d05.md", 1, 0)),
            Summary("2026-10-06", ("d06.md", 1, 0)),
            Summary("2026-10-10", ("d10.md", 1, 0)));

        ChangesResponse response = harness.Ask(index);

        // Today is the 12th: seven days ago is the 5th. The 6th is not old enough, the 4th and the 2nd are older than the 5th.
        response.PreviousWeek!.Day.ShouldBe(new DateOnly(2026, 10, 5));
        response.PreviousWeek.Removed.Select(file => file.Path).ShouldBe(["d05.md"]);
        response.PreviousDay!.Day.ShouldBe(new DateOnly(2026, 10, 10));

        // The older ones were pruned when today's summary was written: the 5th is the newest of them and the first day now.
        response.FirstDay.ShouldBe(new DateOnly(2026, 10, 5));
    }

    [Fact]
    public void Changes_PreviousWeek_IsTheNewestOlderOneWhenNothingIsExactlySevenDaysOld()
    {
        using var harness = new Harness();
        ConfigIndex index = Index(Rule("now.md", 1));
        harness.Prepare(index, Summary("2026-09-20", ("d20.md", 1, 0)), Summary("2026-10-01", ("d01.md", 1, 0)), Summary("2026-10-08", ("d08.md", 1, 0)));

        ChangesResponse response = harness.Ask(index);

        response.PreviousWeek!.Day.ShouldBe(new DateOnly(2026, 10, 1));
        response.PreviousDay!.Day.ShouldBe(new DateOnly(2026, 10, 8));
    }

    [Fact]
    public void Changes_PreviousWeek_IsAbsentWithLessThanSevenDaysOfHistory()
    {
        using var harness = new Harness();
        ConfigIndex index = Index(Rule("now.md", 1));
        harness.Prepare(index, Summary("2026-10-06", ("d06.md", 1, 0)), Summary("2026-10-11", ("d11.md", 1, 0)));

        ChangesResponse response = harness.Ask(index);

        response.PreviousWeek.ShouldBeNull();
        response.PreviousDay!.Day.ShouldBe(new DateOnly(2026, 10, 11));
        response.FirstDay.ShouldBe(new DateOnly(2026, 10, 6));
    }

    [Fact]
    public void Changes_OnlyAnOldSummary_IsBothThePreviousDayAndThePreviousWeek()
    {
        using var harness = new Harness();
        ConfigIndex index = Index(Rule("now.md", 1));
        harness.Prepare(index, Summary("2026-09-30", ("d30.md", 4, 0)));

        ChangesResponse response = harness.Ask(index);

        response.PreviousDay!.Day.ShouldBe(new DateOnly(2026, 9, 30));
        response.PreviousWeek.ShouldBe(response.PreviousDay);
        response.FirstDay.ShouldBe(new DateOnly(2026, 9, 30));
    }

    [Fact]
    public void Changes_SummaryOfADayThatHasNotCome_IsNeverComparedWith()
    {
        using var harness = new Harness();
        ConfigIndex index = Index(Rule("now.md", 1));
        harness.Prepare(index, Summary("2026-10-11", ("d11.md", 1, 0)), Summary("2026-10-14", ("d14.md", 1, 0)));

        ChangesResponse response = harness.Ask(index);

        response.PreviousDay!.Day.ShouldBe(new DateOnly(2026, 10, 11));
        response.PreviousWeek.ShouldBeNull();
    }

    // ---- What is kept ---------------------------------------------------------------------------------------------

    [Theory]
    [InlineData("2026-10-12", "2026-10-12")]
    [InlineData("2026-10-06,2026-10-07,2026-10-12", "2026-10-06,2026-10-07,2026-10-12")]
    [InlineData("2026-09-22,2026-09-27,2026-10-03,2026-10-04,2026-10-06,2026-10-07,2026-10-09,2026-10-11,2026-10-12", "2026-10-04,2026-10-06,2026-10-07,2026-10-09,2026-10-11,2026-10-12")]
    [InlineData("2026-10-04,2026-10-05,2026-10-06", "2026-10-05,2026-10-06")]
    [InlineData("2026-09-01,2026-10-12", "2026-09-01,2026-10-12")]
    [InlineData("2026-10-12,2026-10-13", "2026-10-12,2026-10-13")]
    public void Prune_Days_KeepsEveryDayAfterTodayMinusSevenAndOfTheOthersOnlyTheNewest(string days, string expected)
    {
        IEnumerable<DailySummary> summaries = days.Split(',').Select(day => Summary(day));

        HistoryStore.Prune(summaries, Today).Select(summary => summary.Day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)).ShouldBe(expected.Split(','));
    }

    [Fact]
    public void Prune_NoDays_KeepsNothing() => HistoryStore.Prune([], Today).ShouldBeEmpty();

    [Fact]
    public void Changes_FirstRequestOfTheDay_PrunesWhatIsOldAndKeepsTheNewestOfIt()
    {
        using var harness = new Harness();
        ConfigIndex index = Index(Rule("now.md", 1));
        harness.Prepare(
            index,
            Summary("2026-09-22", ("d22.md", 1, 0)),
            Summary("2026-09-27", ("d27.md", 1, 0)),
            Summary("2026-10-03", ("d03.md", 1, 0)),
            Summary("2026-10-04", ("d04.md", 1, 0)),
            Summary("2026-10-06", ("d06.md", 1, 0)),
            Summary("2026-10-11", ("d11.md", 1, 0)));

        harness.Ask(index);

        harness.StoredDays(index).ShouldBe(["2026-10-04", "2026-10-06", "2026-10-11", "2026-10-12"]);
        using JsonDocument file = harness.Read(index);
        file.RootElement.GetProperty("days").EnumerateArray().First().GetProperty("files").EnumerateObject().Select(entry => entry.Name).ShouldBe(["d04.md"]);
    }

    [Fact]
    public void Changes_TenDaysInARow_KeepTheLastSevenAndTheNewestOlderOne()
    {
        using var harness = new Harness();
        ConfigIndex index = Index(Rule("a.md", 1));
        for (int day = 0; day < 10; day++)
        {
            harness.Ask(index);
            harness.Clock.Advance(TimeSpan.FromDays(1));
        }

        // The last request was on the 21st: the days after the 14th are kept, and of the older ones the newest, which is the 14th.
        harness.StoredDays(index).ShouldBe(["2026-10-14", "2026-10-15", "2026-10-16", "2026-10-17", "2026-10-18", "2026-10-19", "2026-10-20", "2026-10-21"]);
    }

    [Fact]
    public void Changes_FileThatAnotherPusulaWroteMeanwhile_IsMergedAndNothingIsLost()
    {
        using var harness = new Harness();
        ConfigIndex index = Index(Rule("a.md", 5));
        harness.Ask(index);
        harness.Prepare(index, Summary("2026-10-09", ("d09.md", 1, 0)), Summary("2026-10-12", ("a.md", 5, 0)));
        harness.Clock.Advance(TimeSpan.FromDays(1));

        ChangesResponse response = harness.Ask(index);

        harness.StoredDays(index).ShouldBe(["2026-10-09", "2026-10-12", "2026-10-13"]);
        response.FirstDay.ShouldBe(new DateOnly(2026, 10, 9));
    }

    // What is kept is the file, and not what this pusula remembers of it: whoever replaced it, replaced the history.
    [Fact]
    public void Changes_FileThatAnotherPusulaReplaced_IsWhatTodaysSummaryIsAddedTo()
    {
        using var harness = new Harness();
        ConfigIndex index = Index(Rule("a.md", 5));
        harness.Ask(index);
        harness.Prepare(index, Summary("2026-10-10", ("d10.md", 1, 0)));
        harness.Clock.Advance(TimeSpan.FromDays(1));

        harness.Ask(index);

        harness.StoredDays(index).ShouldBe(["2026-10-10", "2026-10-13"]);
    }

    [Fact]
    public void Changes_FileThatWasDeletedWhilePusulaRuns_StartsTheHistoryOver()
    {
        using var harness = new Harness();
        ConfigIndex index = Index(Rule("a.md", 5));
        harness.Ask(index);
        File.Delete(harness.FileOf(index));
        harness.Clock.Advance(TimeSpan.FromDays(1));

        ChangesResponse response = harness.Ask(index);

        response.ShouldBe(new ChangesResponse(HistoryState.Kept, new DateOnly(2026, 10, 13)));
        harness.StoredDays(index).ShouldBe(["2026-10-13"]);
        harness.Messages(LogLevel.Warning).ShouldBeEmpty();
    }

    [Fact]
    public void Merge_SameDayTwice_KeepsTheOneThatWasTakenEarlier()
    {
        DailySummary early = Summary("2026-10-12", ("early.md", 1, 0)) with { TakenAt = Morning };
        DailySummary late = Summary("2026-10-12", ("late.md", 1, 0)) with { TakenAt = Morning.AddHours(5) };

        HistoryStore.Merge([late, early]).Single().ShouldBeSameAs(early);
        HistoryStore.Merge([early, late]).Single().ShouldBeSameAs(early);
    }

    [Fact]
    public void Merge_SameDayAtTheSameMoment_KeepsTheFirstGiven()
    {
        DailySummary first = Summary("2026-10-12", ("first.md", 1, 0)) with { TakenAt = Morning };
        DailySummary second = Summary("2026-10-12", ("second.md", 1, 0)) with { TakenAt = Morning };

        HistoryStore.Merge([first, second]).Single().ShouldBeSameAs(first);
    }

    [Fact]
    public void Merge_Days_AreInTheOrderOfTheDaysOldestFirst() =>
        HistoryStore.Merge([Summary("2026-10-12"), Summary("2026-10-05"), Summary("2026-10-09"), Summary("2026-10-05")])
            .Select(summary => summary.Day).ShouldBe([new DateOnly(2026, 10, 5), new DateOnly(2026, 10, 9), new DateOnly(2026, 10, 12)]);

    [Theory]
    [InlineData(-5, "early")]
    [InlineData(5, "late")]
    public void Changes_TodayOnDiskFromAnotherPusula_KeepsTheEarlierTakenAt(int hoursFromNow, string expectedKept)
    {
        using var harness = new Harness(Morning.AddDays(-1));
        ConfigIndex index = Index(Rule("a.md", 5));
        harness.Ask(index);
        DailySummary yesterday = Summary("2026-10-11", ("a.md", 5, 0)) with { TakenAt = Morning.AddDays(-1) };
        DailySummary theirs = Summary("2026-10-12", ("theirs.md", 1, 0)) with { TakenAt = Morning.AddHours(hoursFromNow) };
        harness.Prepare(index, yesterday, theirs);
        harness.Clock.Advance(TimeSpan.FromDays(1));

        ChangesResponse response = harness.Ask(index);

        response.History.ShouldBe(HistoryState.Kept);
        using JsonDocument file = harness.Read(index);
        JsonElement kept = file.RootElement.GetProperty("days").EnumerateArray().Single(day => day.GetProperty("day").GetString() == "2026-10-12");
        if (expectedKept == "early")
        {
            // Theirs was taken before this request: it stays, and this request does not replace it.
            kept.GetProperty("takenAt").GetDateTimeOffset().ShouldBe(Morning.AddHours(hoursFromNow));
            kept.GetProperty("files").EnumerateObject().Select(entry => entry.Name).ShouldBe(["theirs.md"]);
        }
        else
        {
            // Theirs says it was taken after this request: the earlier one is this request's.
            kept.GetProperty("takenAt").GetDateTimeOffset().ShouldBe(Morning);
            kept.GetProperty("files").EnumerateObject().Select(entry => entry.Name).ShouldBe(["a.md"]);
        }
    }

    // ---- A file that cannot be used -----------------------------------------------------------------------------

    [Theory]
    [InlineData("not json")]
    [InlineData("")]
    [InlineData("{\"version\":2,\"root\":\"ROOT\",\"profile\":\"Claude\",\"days\":[]}")]
    [InlineData("{\"version\":1,\"root\":\"/another/folder\",\"profile\":\"Claude\",\"days\":[]}")]
    [InlineData("{\"version\":1,\"root\":\"ROOT\",\"profile\":\"Vault\",\"days\":[]}")]
    [InlineData("{\"version\":1,\"root\":\"ROOT\",\"profile\":\"Claude\",\"days\":[{\"day\":\"2026-10-11\"}]}")]
    public void Changes_FileThatCannotBeUsed_IsTreatedAsEmptyAndReplacedByTheNextWrite(string text)
    {
        using var harness = new Harness();
        ConfigIndex index = Index(Rule("a.md", 5));
        harness.Temp.Write("state/pusula/history/" + HistoryFile.NameOf(index.Profile, HistoryFile.RootOf(index)), text.Replace("ROOT", JsonEncodedText.Encode(HistoryFile.RootOf(index)).ToString(), StringComparison.Ordinal));

        ChangesResponse response = harness.Ask(index);

        response.ShouldBe(new ChangesResponse(HistoryState.Kept, Today));
        harness.StoredDays(index).ShouldBe(["2026-10-12"]);
        string warning = harness.Messages(LogLevel.Warning).Single();
        warning.ShouldStartWith($"The history file {harness.FileOf(index)} cannot be used and is treated as empty: ");
        warning.ShouldEndWith("; the next summary replaces it");
    }

    [Fact]
    public void Changes_FileThatCannotBeUsed_IsSaidOnceAndNotAgainOnceItIsReplaced()
    {
        using var harness = new Harness();
        ConfigIndex index = Index(Rule("a.md", 5));
        harness.Temp.Write("state/pusula/history/" + HistoryFile.NameOf(index.Profile, HistoryFile.RootOf(index)), "not json");

        harness.Ask(index);
        harness.Ask(index);
        harness.Clock.Advance(TimeSpan.FromDays(1));
        harness.Ask(index);

        harness.Messages(LogLevel.Warning).Length.ShouldBe(1);
        harness.StoredDays(index).ShouldBe(["2026-10-12", "2026-10-13"]);
    }

    [Fact]
    public void Changes_FileThatBecomesUnusableAgain_IsSaidAgain()
    {
        using var harness = new Harness();
        ConfigIndex index = Index(Rule("a.md", 5));
        harness.Temp.Write("state/pusula/history/" + HistoryFile.NameOf(index.Profile, HistoryFile.RootOf(index)), "not json");
        harness.Ask(index);
        harness.Clock.Advance(TimeSpan.FromDays(1));
        harness.Ask(index);
        File.WriteAllText(harness.FileOf(index), "not json again");
        harness.Clock.Advance(TimeSpan.FromDays(1));

        ChangesResponse response = harness.Ask(index);

        // The file that cannot be used holds nothing: the history starts over with today's summary.
        harness.Messages(LogLevel.Warning).Length.ShouldBe(2);
        response.ShouldBe(new ChangesResponse(HistoryState.Kept, new DateOnly(2026, 10, 14)));
        harness.StoredDays(index).ShouldBe(["2026-10-14"]);
    }

    // ---- Inside a folder that is shown ----------------------------------------------------------------------------

    [Theory]
    [InlineData("state")]
    [InlineData("above the state directory")]
    [InlineData("pusula")]
    [InlineData("history")]
    [InlineData("with a separator at the end")]
    [InlineData("root of the file system")]
    public void Changes_StateFolderInsideAFolderThatIsShown_IsInsideSourceAndWritesNothing(string shown)
    {
        using var harness = new Harness();
        string folder = shown switch
        {
            "state" => harness.State,
            "above the state directory" => harness.Temp.Path,
            "pusula" => Path.Join(harness.State, "pusula"),
            "history" => harness.Folder,
            "with a separator at the end" => harness.State + Path.DirectorySeparatorChar,
            _ => Path.GetPathRoot(harness.State)!,
        };
        ConfigIndex index = Index(Rule("a.md", 5));

        ChangesResponse response = harness.Ask(index, folder);

        response.ShouldBe(new ChangesResponse(HistoryState.InsideSource));
        Directory.Exists(harness.State).ShouldBeFalse();
        harness.Logger.Entries.ShouldBeEmpty();
    }

    [Fact]
    public void Changes_StateFolderInsideOneOfSeveralFoldersThatAreShown_IsInsideSource()
    {
        using var harness = new Harness();

        ChangesResponse response = harness.Ask(Index(Rule("a.md", 5)), harness.Temp.Resolve("elsewhere"), harness.Temp.Resolve("state"), harness.Temp.Resolve("other"));

        response.History.ShouldBe(HistoryState.InsideSource);
    }

    [Theory]
    [InlineData("a folder next to the state directory that starts with the same name")]
    [InlineData("a folder somewhere else")]
    [InlineData("a folder inside the history folder")]
    [InlineData("none")]
    public void Changes_StateFolderThatIsNotInsideAFolderThatIsShown_IsKept(string shown)
    {
        using var harness = new Harness();
        string[] folders = shown switch
        {
            "a folder next to the state directory that starts with the same name" => [harness.State + "-other"],
            "a folder somewhere else" => [harness.Temp.Resolve("elsewhere")],
            "a folder inside the history folder" => [Path.Join(harness.Folder, "sub")],
            _ => [],
        };

        ChangesResponse response = harness.Store.Changes(Index(Rule("a.md", 5)), folders);

        response.ShouldBe(new ChangesResponse(HistoryState.Kept, Today));
    }

    // Where paths ignore case a folder in another case is the same folder.
    [Fact]
    public void Changes_ShownFolderInAnotherCase_IsTheStateFolderOnlyWherePathsIgnoreCase()
    {
        using var harness = new Harness();

        ChangesResponse response = harness.Ask(Index(Rule("a.md", 5)), harness.State.ToUpperInvariant());

        response.History.ShouldBe(OperatingSystem.IsWindows() ? HistoryState.InsideSource : HistoryState.Kept);
    }

    [Fact]
    public void Changes_StateFolderInsideAShownFolder_StillComparesWithTheOlderSummariesAndWritesNothingMore()
    {
        using var harness = new Harness();
        ConfigIndex index = Index(Rule("a.md", 5), Rule("b.md", 6));
        harness.Prepare(index, Summary("2026-10-11", ("a.md", 5, 0)));
        byte[] before = File.ReadAllBytes(harness.FileOf(index));

        ChangesResponse response = harness.Ask(index, harness.State);

        response.History.ShouldBe(HistoryState.InsideSource);
        response.Error.ShouldBeNull();
        response.FirstDay.ShouldBe(new DateOnly(2026, 10, 11));
        response.PreviousDay!.Added.Select(file => file.Path).ShouldBe(["b.md"]);
        File.ReadAllBytes(harness.FileOf(index)).ShouldBe(before);
        harness.Files().Length.ShouldBe(1);
    }

    [Fact]
    public void Changes_StateFolderInsideAShownFolderThatTheSourceBecomesNotShown_IsKeptFromThenOn()
    {
        using var harness = new Harness();
        ConfigIndex index = Index(Rule("a.md", 5));

        harness.Ask(index, harness.State).History.ShouldBe(HistoryState.InsideSource);
        ChangesResponse after = harness.Ask(index);

        after.ShouldBe(new ChangesResponse(HistoryState.Kept, Today));
        harness.Files().Length.ShouldBe(1);
    }

    // ---- A file that cannot be written ----------------------------------------------------------------------------

    [Fact]
    public void Changes_FileInThePlaceOfTheFolder_IsWriteFailedWithTheErrorAndTriedAgainAtTheNextRequest()
    {
        using var harness = new Harness();
        harness.Temp.Write("state/pusula", "a file where the folder should be");
        ConfigIndex index = Index(Rule("a.md", 5));

        ChangesResponse first = harness.Ask(index);
        ChangesResponse second = harness.Ask(index);

        first.History.ShouldBe(HistoryState.WriteFailed);
        first.Error.ShouldNotBeNullOrWhiteSpace();
        first.FirstDay.ShouldBeNull();
        first.PreviousDay.ShouldBeNull();
        first.PreviousWeek.ShouldBeNull();
        second.ShouldBe(first);

        // The obstacle goes: the next request keeps the summary.
        File.Delete(harness.Temp.Resolve("state/pusula"));
        ChangesResponse third = harness.Ask(index);

        third.ShouldBe(new ChangesResponse(HistoryState.Kept, Today));
        harness.Files().ShouldBe([HistoryFile.NameOf(SourceProfile.Claude, HistoryFile.RootOf(index))]);
    }

    [Fact]
    public void Changes_WriteFailed_IsSaidOncePerSourceAndDay()
    {
        using var harness = new Harness();
        harness.Temp.Write("state/pusula", "a file where the folder should be");
        ConfigIndex index = Index(Rule("a.md", 5));

        harness.Ask(index);
        harness.Ask(index);
        harness.Ask(index);

        string[] failed = [.. harness.Messages(LogLevel.Warning).Where(message => message.StartsWith("Today's summary of", StringComparison.Ordinal))];
        failed.Length.ShouldBe(1);
        failed[0].ShouldStartWith($"Today's summary of {HistoryFile.RootOf(index)} could not be written to {harness.Folder}: ");
        failed[0].ShouldEndWith("; the next request tries again");

        // The next day it is said once more, and for another source on the same day too.
        harness.Clock.Advance(TimeSpan.FromDays(1));
        harness.Ask(index);
        harness.Ask(IndexFactory.Index([Rule("v.md", 1)], root: Path.GetFullPath("/shown/two"), profile: SourceProfile.Vault));
        harness.Messages(LogLevel.Warning).Count(message => message.StartsWith("Today's summary of", StringComparison.Ordinal)).ShouldBe(3);
    }

    [Fact]
    public void Changes_WriteFailed_StillComparesWithTheOlderSummariesAndKeepsTodaysWhenItWorksAgain()
    {
        using var harness = new Harness();
        ConfigIndex index = Index(Rule("a.md", 5));
        harness.Ask(index);
        string file = harness.FileOf(index);

        // A folder where the file is: it cannot be read, and the new file cannot take its place.
        File.Delete(file);
        Directory.CreateDirectory(file);
        File.WriteAllText(Path.Join(file, "keep.txt"), "x");
        harness.Clock.Advance(TimeSpan.FromDays(1));
        ConfigIndex now = Index(Rule("a.md", 5), Rule("b.md", 7));

        ChangesResponse failed = harness.Ask(now);

        failed.History.ShouldBe(HistoryState.WriteFailed);
        failed.Error.ShouldNotBeNullOrWhiteSpace();
        failed.FirstDay.ShouldBe(Today);
        failed.PreviousDay!.Day.ShouldBe(Today);
        failed.PreviousDay.Added.Select(added => added.Path).ShouldBe(["b.md"]);
        harness.Messages(LogLevel.Warning).Length.ShouldBe(2);

        Directory.Delete(file, recursive: true);
        ChangesResponse again = harness.Ask(now);

        again.History.ShouldBe(HistoryState.Kept);
        harness.StoredDays(now).ShouldBe(["2026-10-13"]);
        harness.Files().ShouldBe([Path.GetFileName(file)]);
    }

    [Fact]
    [UnsupportedOSPlatform("windows")]
    public void Changes_WriteFailedBecauseTheFolderCannotBeWrittenTo_StillComparesWithTheFileAndKeepsItWhenItWorksAgain()
    {
        Assert.SkipWhen(OperatingSystem.IsWindows() || Environment.IsPrivilegedProcess, "Needs POSIX permissions and an unprivileged user.");
        using var harness = new Harness();
        ConfigIndex index = Index(Rule("a.md", 5));
        harness.Ask(index);
        harness.Clock.Advance(TimeSpan.FromDays(1));
        ConfigIndex now = Index(Rule("a.md", 5), Rule("b.md", 7));
        File.SetUnixFileMode(harness.Folder, UnixFileMode.UserRead | UnixFileMode.UserExecute);
        try
        {
            ChangesResponse failed = harness.Ask(now);

            failed.History.ShouldBe(HistoryState.WriteFailed);
            failed.Error.ShouldNotBeNullOrWhiteSpace();
            failed.FirstDay.ShouldBe(Today);
            failed.PreviousDay!.Added.Select(added => added.Path).ShouldBe(["b.md"]);
        }
        finally
        {
            File.SetUnixFileMode(harness.Folder, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }

        harness.Ask(now).History.ShouldBe(HistoryState.Kept);
        harness.StoredDays(now).ShouldBe(["2026-10-12", "2026-10-13"]);
        harness.Files().Length.ShouldBe(1);
    }

    // ---- The day --------------------------------------------------------------------------------------------------

    [Theory]
    [InlineData(3, "2026-10-12", "2026-10-12T01:30:00+03:00")]
    [InlineData(0, "2026-10-11", "2026-10-11T22:30:00+00:00")]
    [InlineData(-8, "2026-10-11", "2026-10-11T14:30:00-08:00")]
    [InlineData(2, "2026-10-12", "2026-10-12T00:30:00+02:00")]
    public void Changes_LocalDay_IsTheDayInTheTimeZoneOfTheClock(int hours, string day, string takenAt)
    {
        var zone = TimeZoneInfo.CreateCustomTimeZone($"test{hours}", TimeSpan.FromHours(hours), $"test{hours}", $"test{hours}");
        using var harness = new Harness(new DateTimeOffset(2026, 10, 11, 22, 30, 0, TimeSpan.Zero), zone);
        ConfigIndex index = Index(Rule("a.md", 5));

        ChangesResponse response = harness.Ask(index);

        response.ShouldBe(new ChangesResponse(HistoryState.Kept, Date(day)));
        harness.StoredDays(index).ShouldBe([day]);
        using JsonDocument file = harness.Read(index);
        file.RootElement.GetProperty("days")[0].GetProperty("takenAt").GetString().ShouldBe(takenAt);
    }

    [Fact]
    public void Changes_MidnightInTheLocalTimeZone_StartsANewDayWhateverTimeItIsInUtc()
    {
        var zone = TimeZoneInfo.CreateCustomTimeZone("test+3", TimeSpan.FromHours(3), "test+3", "test+3");
        using var harness = new Harness(new DateTimeOffset(2026, 10, 11, 20, 58, 0, TimeSpan.Zero), zone);
        ConfigIndex index = Index(Rule("a.md", 5));

        // 23:58 and 23:59 on the 11th in the zone: one day.
        harness.Ask(index).ShouldBe(new ChangesResponse(HistoryState.Kept, new DateOnly(2026, 10, 11)));
        harness.Clock.Advance(TimeSpan.FromMinutes(1));
        harness.Ask(index).ShouldBe(new ChangesResponse(HistoryState.Kept, new DateOnly(2026, 10, 11)));

        // 00:00 on the 12th: the next day.
        harness.Clock.Advance(TimeSpan.FromMinutes(1));
        ChangesResponse response = harness.Ask(index);

        response.PreviousDay!.Day.ShouldBe(new DateOnly(2026, 10, 11));
        harness.StoredDays(index).ShouldBe(["2026-10-11", "2026-10-12"]);
    }

    // ---- At the same time -------------------------------------------------------------------------------------------

    [Fact]
    public void Changes_ManyFirstRequestsAtOnce_KeepOneSummary()
    {
        using var harness = new Harness();
        ConfigIndex index = Index(Rule("a.md", 5), Rule("b.md", 6));
        const int Requests = 16;
        using var start = new Barrier(Requests);
        var responses = new ChangesResponse[Requests];
        Thread[] threads =
        [
            .. Enumerable.Range(0, Requests).Select(number => new Thread(() =>
            {
                start.SignalAndWait();
                responses[number] = harness.Ask(index);
            })),
        ];

        foreach (Thread thread in threads)
        {
            thread.Start();
        }

        foreach (Thread thread in threads)
        {
            thread.Join();
        }

        responses.ShouldAllBe(response => response == new ChangesResponse(HistoryState.Kept, Today));
        harness.Messages(LogLevel.Information).Length.ShouldBe(1);
        harness.Files().ShouldBe([HistoryFile.NameOf(SourceProfile.Claude, HistoryFile.RootOf(index))]);
        harness.StoredDays(index).ShouldBe(["2026-10-12"]);
    }

    // ---- Permissions ------------------------------------------------------------------------------------------------

    [Fact]
    [UnsupportedOSPlatform("windows")]
    public void Changes_OnUnix_MakesTheFoldersWithMode0700AndTheFileWithMode0600()
    {
        Assert.SkipWhen(OperatingSystem.IsWindows(), "Unix permissions.");
        using var harness = new Harness();
        ConfigIndex index = Index(Rule("a.md", 5));

        harness.Ask(index);

        const UnixFileMode Private = UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute;
        File.GetUnixFileMode(Path.Join(harness.State, "pusula")).ShouldBe(Private);
        File.GetUnixFileMode(harness.Folder).ShouldBe(Private);
        File.GetUnixFileMode(harness.FileOf(index)).ShouldBe(UnixFileMode.UserRead | UnixFileMode.UserWrite);
        harness.Files().Length.ShouldBe(1);
    }
}
