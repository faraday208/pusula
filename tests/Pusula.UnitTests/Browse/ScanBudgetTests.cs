using Pusula.Browse;
using Pusula.UnitTests.Support;
using Shouldly;
using Xunit;

namespace Pusula.UnitTests.Browse;

public sealed class ScanBudgetTests
{
    private static readonly TimeSpan Plenty = TimeSpan.FromMinutes(10);

    [Fact]
    public void TrySpend_WithinTheEntries_SaysYesUntilTheyAreUsedUp()
    {
        var budget = new ScanBudget(maxEntries: 3, Plenty);

        budget.TrySpend().ShouldBeTrue();
        budget.TrySpend().ShouldBeTrue();
        budget.TrySpend().ShouldBeTrue();
        budget.IsSpent.ShouldBeFalse();

        budget.TrySpend().ShouldBeFalse();
        budget.IsSpent.ShouldBeTrue();
    }

    [Fact]
    public void TrySpend_AfterItSaidNo_SaysNoForEver()
    {
        var budget = new ScanBudget(maxEntries: 1, Plenty);
        budget.TrySpend().ShouldBeTrue();
        budget.TrySpend().ShouldBeFalse();

        for (int i = 0; i < 5; i++)
        {
            budget.TrySpend().ShouldBeFalse();
        }

        budget.IsSpent.ShouldBeTrue();
    }

    [Fact]
    public void IsSpent_NothingAskedFor_IsFalse() =>
        new ScanBudget(maxEntries: 0, TimeSpan.Zero).IsSpent.ShouldBeFalse();

    [Fact]
    public void TrySpend_NoEntries_SaysNoAtOnce() =>
        new ScanBudget(maxEntries: 0, Plenty).TrySpend().ShouldBeFalse();

    [Fact]
    public void TrySpend_NoTime_SaysNoAtOnce() =>
        new ScanBudget(maxEntries: 1000, TimeSpan.Zero).TrySpend().ShouldBeFalse();

    [Fact]
    public void TrySpend_TimeRunsOut_SaysNoEvenWithEntriesLeft()
    {
        var budget = new ScanBudget(maxEntries: 1000, TimeSpan.FromMilliseconds(50));
        budget.TrySpend().ShouldBeTrue();

        Thread.Sleep(150);

        budget.TrySpend().ShouldBeFalse();
        budget.IsSpent.ShouldBeTrue();
    }

    [Fact]
    public void TrySpend_ClockThatMovesOnAtEveryAsk_RunsOutOfTimeAtTheAskThatReachesTheLimitWithoutAnyWaiting()
    {
        var clock = new SteppingTimeProvider(TimeSpan.FromMilliseconds(10));
        var budget = new ScanBudget(maxEntries: 1000, TimeSpan.FromMilliseconds(100), clock);

        // The clock was read when the budget started; each ask reads it once and finds it 10 ms further on: the ninth ask is at 90 ms, the tenth at 100.
        for (int i = 1; i <= 9; i++)
        {
            budget.TrySpend().ShouldBeTrue($"ask {i}");
        }

        budget.IsSpent.ShouldBeFalse();
        budget.TrySpend().ShouldBeFalse();
        budget.IsSpent.ShouldBeTrue();
        clock.Reads.ShouldBe(11);
    }

    [Fact]
    public void Taken_AsksThatWereAnsweredYes_AreCountedAndTheOneThatWasNotIsNot()
    {
        var budget = new ScanBudget(maxEntries: 3, Plenty);

        budget.Taken.ShouldBe(0);
        budget.TrySpend().ShouldBeTrue();
        budget.TrySpend().ShouldBeTrue();
        budget.Taken.ShouldBe(2);
        budget.TrySpend().ShouldBeTrue();
        budget.TrySpend().ShouldBeFalse();

        budget.Taken.ShouldBe(3);
        budget.TrySpend().ShouldBeFalse();
        budget.Taken.ShouldBe(3);
    }

    [Fact]
    public void SpentBy_BudgetThatRanOutOfEntries_SaysEntries()
    {
        var budget = new ScanBudget(maxEntries: 1, Plenty);

        budget.SpentBy.ShouldBeNull();
        budget.TrySpend().ShouldBeTrue();
        budget.SpentBy.ShouldBeNull();
        budget.TrySpend().ShouldBeFalse();

        budget.SpentBy.ShouldBe("entries");
    }

    [Fact]
    public void SpentBy_BudgetThatRanOutOfTime_SaysTimeAndCountsTheEntriesTakenBefore()
    {
        var clock = new SteppingTimeProvider(TimeSpan.FromMilliseconds(10));
        var budget = new ScanBudget(maxEntries: 1000, TimeSpan.FromMilliseconds(30), clock);

        // At 10 and 20 ms the answers are yes; at 30 ms the time is out.
        budget.TrySpend().ShouldBeTrue();
        budget.TrySpend().ShouldBeTrue();
        budget.TrySpend().ShouldBeFalse();

        budget.SpentBy.ShouldBe("time");
        budget.Taken.ShouldBe(2);
    }

    [Fact]
    public void SpentAt_BudgetThatRanOutOfTime_IsTheMomentOfTheAskThatWasRefused()
    {
        var clock = new SteppingTimeProvider(TimeSpan.FromMilliseconds(10));
        var budget = new ScanBudget(maxEntries: 1000, TimeSpan.FromMilliseconds(30), clock);

        budget.SpentAt.ShouldBe(TimeSpan.Zero);
        budget.TrySpend().ShouldBeTrue();
        budget.TrySpend().ShouldBeTrue();
        budget.SpentAt.ShouldBe(TimeSpan.Zero);
        budget.TrySpend().ShouldBeFalse();

        // The ask is at 30 ms, and it reads the clock once: no more is read for saying so.
        budget.SpentAt.ShouldBe(TimeSpan.FromMilliseconds(30));
        clock.Reads.ShouldBe(4);
    }

    [Fact]
    public void SpentAt_BudgetThatRanOutOfEntries_IsTheMomentOfTheAskThatWasRefused()
    {
        var clock = new SteppingTimeProvider(TimeSpan.FromMilliseconds(10));
        var budget = new ScanBudget(maxEntries: 2, Plenty, clock);

        budget.TrySpend().ShouldBeTrue();
        budget.TrySpend().ShouldBeTrue();
        budget.TrySpend().ShouldBeFalse();

        budget.SpentAt.ShouldBe(TimeSpan.FromMilliseconds(30));

        // Once spent it stays so, and the moment is not looked at again.
        budget.TrySpend().ShouldBeFalse();
        budget.SpentAt.ShouldBe(TimeSpan.FromMilliseconds(30));
        clock.Reads.ShouldBe(4);
    }

    [Fact]
    public void TrySpend_ClockThatIsNeverGiven_IsTheSystemOne() =>
        new ScanBudget(maxEntries: 1, TimeSpan.FromMinutes(10)).TrySpend().ShouldBeTrue();
}
