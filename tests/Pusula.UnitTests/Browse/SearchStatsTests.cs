using System.Diagnostics;
using Pusula.Browse;
using Pusula.UnitTests.Support;
using Shouldly;
using Xunit;

namespace Pusula.UnitTests.Browse;

public sealed class SearchStatsTests
{
    private static readonly TimeSpan Plenty = TimeSpan.FromMinutes(10);

    private static long Ticks(double milliseconds) => (long)(milliseconds * Stopwatch.Frequency / 1000);

    [Fact]
    public void Add_TimesOfSomeWork_AreCountedAndTheSlowestIsKeptWithItsFolder()
    {
        var part = new SearchStats.Part();

        part.Add(Ticks(2), entries: 10, "/a");
        part.Add(Ticks(7), entries: 40, "/b");
        part.Add(Ticks(3), entries: 5, "/c");

        part.Count.ShouldBe(3);
        part.Entries.ShouldBe(55);
        part.Milliseconds.ShouldBe(12, tolerance: 0.01);
        part.SlowestMilliseconds.ShouldBe(7, tolerance: 0.01);
        part.Slowest.ShouldBe("/b");
    }

    [Fact]
    public void Add_NothingYet_HasNoSlowest()
    {
        var part = new SearchStats.Part();

        part.Count.ShouldBe(0);
        part.Slowest.ShouldBeNull();
        part.Milliseconds.ShouldBe(0);
    }

    [Fact]
    public void AddNote_NotesOpened_AreCountedAndOnlyTheFolderOfTheSlowestIsKept()
    {
        var part = new SearchStats.ScanPart();

        part.AddNote(Ticks(1), "/home/x/a/one.md");
        part.AddNote(Ticks(9), "/home/x/b/two.md");
        part.AddNote(Ticks(4), "/home/x/c/three.md");

        part.NotesOpened.ShouldBe(3);
        part.NoteMilliseconds.ShouldBe(14, tolerance: 0.01);
        part.SlowestNoteMilliseconds.ShouldBe(9, tolerance: 0.01);
        part.SlowestNoteFolder.ShouldBe(Path.GetDirectoryName("/home/x/b/two.md"));
        part.SlowestNoteFolder!.ShouldNotContain("two");
    }

    [Fact]
    public void NoteBudget_BudgetThatIsNotSpent_NotesNothing()
    {
        var stats = new SearchStats();
        var budget = new ScanBudget(maxEntries: 5, Plenty);
        budget.TrySpend();

        stats.NoteBudget(budget, "the walk");

        stats.SpentDuring.ShouldBeNull();
        stats.SpentBy.ShouldBeNull();
    }

    [Fact]
    public void NoteBudget_BudgetThatIsSpent_NotesTheStageItEndedInOnceAndKeepsTheFirst()
    {
        var stats = new SearchStats();
        var budget = new ScanBudget(maxEntries: 2, Plenty);
        budget.TrySpend();
        budget.TrySpend();
        budget.TrySpend();

        stats.NoteBudget(budget, "the walk");
        stats.NoteBudget(budget, "something later");

        stats.SpentDuring.ShouldBe("the walk");
        stats.SpentBy.ShouldBe("entries");
        stats.SpentEntries.ShouldBe(2);
    }

    [Fact]
    public void NoteBudget_BudgetThatRanOutOfTime_SaysTimeAndTheMomentItRanOut()
    {
        var clock = new SteppingTimeProvider(TimeSpan.FromMilliseconds(10));
        var budget = new ScanBudget(maxEntries: 1000, TimeSpan.FromMilliseconds(20), clock);
        var stats = new SearchStats();
        budget.TrySpend();
        budget.TrySpend();

        stats.NoteBudget(budget, "looking into the candidates");

        // The ask that was refused was at 20 ms: that is the moment, and not when the stage ended.
        stats.SpentBy.ShouldBe("time");
        stats.SpentEntries.ShouldBe(1);
        stats.SpentAfter.ShouldBe(TimeSpan.FromMilliseconds(20));
    }

    [Fact]
    public void AccountedMilliseconds_StagesMeasuredOneAfterTheOther_AreAdded()
    {
        var stats = new SearchStats();
        stats.Walk.Add(Ticks(10), 1, "/w");
        stats.Probes.Add(Ticks(20), 1, "/p");
        stats.Scans.Add(Ticks(30), 1, "/s");
        stats.Describes.Add(Ticks(2), 1, "/d");

        stats.AccountedMilliseconds.ShouldBe(10 + 20 + 30 + 2, tolerance: 0.05);
    }
}
