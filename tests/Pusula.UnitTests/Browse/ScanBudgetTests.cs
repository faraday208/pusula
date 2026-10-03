using Pusula.Browse;
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
}
