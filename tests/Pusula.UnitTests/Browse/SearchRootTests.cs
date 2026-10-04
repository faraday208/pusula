using Pusula.Browse;
using Shouldly;
using Xunit;

namespace Pusula.UnitTests.Browse;

public sealed class SearchRootTests
{
    private static SearchRoot Skipping(params string[] names) => new("/drive", MaxDepth: 4) { SkippedAtTop = names };

    [Fact]
    public void Skips_RootThatNamesNothing_SkipsNothing()
    {
        var root = new SearchRoot("/drive", MaxDepth: 4);

        root.SkippedAtTop.ShouldBeEmpty();
        root.Skips("Windows").ShouldBeFalse();
    }

    [Theory]
    [InlineData("Windows", true)]
    [InlineData("Program Files (x86)", true)]
    [InlineData("Windows2", false)]
    [InlineData("MyWindows", false)]
    [InlineData("Program Files", false)]
    [InlineData("", false)]
    public void Skips_Name_IsSkippedWhenItIsOneOfTheNames(string name, bool expected) =>
        Skipping("Windows", "Program Files (x86)").Skips(name).ShouldBe(expected);

    [Fact]
    public void Skips_NameThatDiffersOnlyInCase_IsSkippedWhereThePlatformIgnoresCase() =>
        Skipping("Windows").Skips("WINDOWS").ShouldBe(OperatingSystem.IsWindows());

    [Theory]
    [InlineData("$Recycle.Bin", true)]
    [InlineData("$WinREAgent", true)]
    [InlineData("$", true)]
    [InlineData("Recycle$", false)]
    [InlineData("Recycle.Bin", false)]
    [InlineData("", false)]
    public void Skips_NameThatEndsWithAStar_StandsForEveryNameThatStartsWithTheRest(string name, bool expected) =>
        Skipping("$*").Skips(name).ShouldBe(expected);

    [Fact]
    public void Skips_StarThatIsNotAtTheEnd_IsPartOfTheName()
    {
        SearchRoot root = Skipping("a*b");

        root.Skips("a*b").ShouldBeTrue();
        root.Skips("axb").ShouldBeFalse();
        root.Skips("a").ShouldBeFalse();
    }

    [Fact]
    public void Equals_RootsWithTheSamePathDepthAndNames_AreEqualWhateverListHoldsTheNames()
    {
        var left = new SearchRoot("/drive", MaxDepth: 4) { SkippedAtTop = ["a", "b"] };
        var right = new SearchRoot("/drive", MaxDepth: 4) { SkippedAtTop = new List<string> { "a", "b" } };

        left.Equals(right).ShouldBeTrue();
        (left == right).ShouldBeTrue();
        left.GetHashCode().ShouldBe(right.GetHashCode());
        new SearchRoot("/drive", MaxDepth: 4).Equals(new SearchRoot("/drive", MaxDepth: 4)).ShouldBeTrue();
    }

    [Fact]
    public void ProbesLastLevel_RootThatSaysNothing_DoesNotAskItsLastLevel() =>
        new SearchRoot("/drive", MaxDepth: 4).ProbesLastLevel.ShouldBeFalse();

    [Fact]
    public void Equals_RootsThatDifferInAskingTheirLastLevel_AreNotEqual()
    {
        var asking = new SearchRoot("/drive", MaxDepth: 4) { ProbesLastLevel = true };
        var other = new SearchRoot("/drive", MaxDepth: 4);

        asking.Equals(other).ShouldBeFalse();
        (asking == other).ShouldBeFalse();
        asking.Equals(new SearchRoot("/drive", MaxDepth: 4) { ProbesLastLevel = true }).ShouldBeTrue();
        asking.GetHashCode().ShouldBe(new SearchRoot("/drive", MaxDepth: 4) { ProbesLastLevel = true }.GetHashCode());
    }

    [Fact]
    public void Equals_RootsThatDifferInPathDepthOrNames_AreNotEqual()
    {
        var root = new SearchRoot("/drive", MaxDepth: 4) { SkippedAtTop = ["a", "b"] };

        root.Equals(new SearchRoot("/other", MaxDepth: 4) { SkippedAtTop = ["a", "b"] }).ShouldBeFalse();
        root.Equals(new SearchRoot("/drive", MaxDepth: 3) { SkippedAtTop = ["a", "b"] }).ShouldBeFalse();
        root.Equals(new SearchRoot("/drive", MaxDepth: 4) { SkippedAtTop = ["b", "a"] }).ShouldBeFalse();
        root.Equals(new SearchRoot("/drive", MaxDepth: 4) { SkippedAtTop = ["a"] }).ShouldBeFalse();
        root.Equals(new SearchRoot("/drive", MaxDepth: 4)).ShouldBeFalse();
        root.Equals((SearchRoot?)null).ShouldBeFalse();
    }
}
