using Pusula.Updates;
using Shouldly;
using Xunit;

namespace Pusula.UnitTests.Updates;

public sealed class SemanticVersionTests
{
    private static SemanticVersion Parse(string text)
    {
        SemanticVersion.TryParse(text, out SemanticVersion? version).ShouldBeTrue(text);
        return version!;
    }

    // The example of the specification (section 11): each is lower than the next.
    [Fact]
    public void CompareTo_TheChainOfTheSpecification_IsInAscendingOrder()
    {
        string[] chain = ["1.0.0-alpha", "1.0.0-alpha.1", "1.0.0-alpha.beta", "1.0.0-beta", "1.0.0-beta.2", "1.0.0-beta.11", "1.0.0-rc.1", "1.0.0"];

        for (int low = 0; low < chain.Length; low++)
        {
            for (int high = 0; high < chain.Length; high++)
            {
                Math.Sign(Parse(chain[low]).CompareTo(Parse(chain[high]))).ShouldBe(Math.Sign(low.CompareTo(high)), $"{chain[low]} against {chain[high]}");
            }
        }
    }

    [Theory]
    [InlineData("0.4.0-rc.1", "0.4.0", -1)]
    [InlineData("0.4.0", "0.4.0-rc.1", 1)]
    [InlineData("0.4.0", "0.4.0", 0)]
    [InlineData("0.4.0-rc.1", "0.4.0-rc.1", 0)]
    [InlineData("0.10.0", "0.9.0", 1)]
    [InlineData("0.3.10", "0.3.9", 1)]
    [InlineData("1.0.0", "0.99.99", 1)]
    [InlineData("2.0.0", "10.0.0", -1)]
    [InlineData("0.3.0", "0.3.0-9", 1)]
    [InlineData("1.0.0-2", "1.0.0-10", -1)]
    [InlineData("1.0.0-9", "1.0.0-a", -1)]
    [InlineData("1.0.0-a", "1.0.0-9", 1)]
    [InlineData("1.0.0-Z", "1.0.0-a", -1)]
    [InlineData("1.0.0-a-b", "1.0.0-a", 1)]
    [InlineData("1.0.0-alpha", "1.0.0-alpha.0", -1)]
    [InlineData("1.0.0-rc.1.1", "1.0.0-rc.1", 1)]
    [InlineData("1.0.0-alpha.1", "1.0.0-beta", -1)]
    [InlineData("1.0.0-beta.1", "1.0.0-alpha.9", 1)]
    [InlineData("1.0.0-alpha.5", "1.0.0-alpha.beta", -1)]
    public void CompareTo_Pair_FollowsThePrecedenceOfSemVer(string left, string right, int expected) =>
        Math.Sign(Parse(left).CompareTo(Parse(right))).ShouldBe(expected);

    [Fact]
    public void CompareTo_ZerosInFront_DoNotCount() => Parse("1.2.3").CompareTo(Parse("01.02.03")).ShouldBe(0);

    [Fact]
    public void CompareTo_NumbersLongerThanAnyInteger_AreComparedAsNumbers() =>
        Parse("99999999999999999999.0.0").CompareTo(Parse("99999999999999999998.99999999999999999999.0")).ShouldBeGreaterThan(0);

    [Fact]
    public void CompareTo_Null_IsLower() => Parse("0.0.0").CompareTo(null).ShouldBeGreaterThan(0);

    [Theory]
    [InlineData("0.3.0")]
    [InlineData("1.2.3-rc.1")]
    [InlineData("1.2.3-0")]
    [InlineData("10.20.30-alpha-1.x-y.7")]
    public void TryParse_AVersion_IsKeptAsWritten(string text) => Parse(text).Text.ShouldBe(text);

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("0.3")]
    [InlineData("0.3.0.1")]
    [InlineData("v0.3.0")]
    [InlineData("0.3.0 ")]
    [InlineData("0.3.0\n")]
    [InlineData("0.3.0-")]
    [InlineData("0.3.0-rc..1")]
    [InlineData("0.3.0-rc.")]
    [InlineData("0.3.0-rc_1")]
    [InlineData("0.3.0+build")]
    [InlineData("a.b.c")]
    [InlineData("-1.0.0")]
    [InlineData("٠.0.0")]
    public void TryParse_NotAVersion_IsFalse(string? text)
    {
        SemanticVersion.TryParse(text, out SemanticVersion? version).ShouldBeFalse();
        version.ShouldBeNull();
    }
}

public sealed class ReleaseTagTests
{
    [Theory]
    [InlineData("v0.4.0")]
    [InlineData("v0.4.0-rc.1")]
    [InlineData("v10.20.30-alpha-1.x")]
    public void TryParse_ATag_HasItsVersionAndItsPage(string text)
    {
        ReleaseTag.TryParse(text, out ReleaseTag? tag).ShouldBeTrue();

        tag!.Text.ShouldBe(text);
        tag.Version.Text.ShouldBe(text[1..]);
        tag.Url.ShouldBe("https://github.com/faraday208/pusula/releases/tag/" + text);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("0.4.0")]
    [InlineData("V0.4.0")]
    [InlineData("v0.4")]
    [InlineData("latest")]
    [InlineData("v0.4.0 ")]
    [InlineData("v0.4.0/../../x")]
    [InlineData("v0.4.0?x=1")]
    [InlineData("v0.4.0#x")]
    [InlineData("v0.4.0-rc 1")]
    [InlineData("vv0.4.0")]
    public void TryParse_NotATag_IsFalse(string? text)
    {
        ReleaseTag.TryParse(text, out ReleaseTag? tag).ShouldBeFalse();
        tag.ShouldBeNull();
    }

    [Fact]
    public void TryParse_ATagThatIsTooLong_IsFalse()
    {
        ReleaseTag.TryParse("v1.0.0-" + new string('a', 80), out _).ShouldBeFalse();
        ReleaseTag.TryParse("v1.0.0-" + new string('a', 40), out _).ShouldBeTrue();
    }
}
