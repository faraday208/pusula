using System.Reflection;
using Pusula.Startup;
using Shouldly;
using Xunit;

namespace Pusula.UnitTests.Startup;

public sealed class AppVersionTests
{
    // The SDK adds the commit after a "+": it is not part of the version.
    [Theory]
    [InlineData("0.1.0", "0.1.0")]
    [InlineData("0.1.0+0123456789abcdef0123456789abcdef01234567", "0.1.0")]
    [InlineData("1.2.3-rc.1+build.5", "1.2.3-rc.1")]
    [InlineData("  0.1.0 +abc", "0.1.0")]
    [InlineData("0.1.0+abc+def", "0.1.0")]
    public void FromInformationalVersion_WithOrWithoutTheCommit_IsTheVersionOnly(string informational, string expected) =>
        AppVersion.FromInformationalVersion(informational).ShouldBe(expected);

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("+1a2b3c4")]
    public void FromInformationalVersion_NothingBeforeThePlus_IsZero(string? informational) =>
        AppVersion.FromInformationalVersion(informational).ShouldBe("0.0.0");

    [Fact]
    public void Current_IsTheInformationalVersionOfTheAssemblyWithoutTheCommit()
    {
        string? informational = typeof(AppVersion).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;

        informational.ShouldNotBeNull().ShouldStartWith(AppVersion.Current);
        AppVersion.Current.ShouldNotContain('+');
        AppVersion.Current.ShouldMatch(@"^\d+\.\d+\.\d+(?:-[0-9A-Za-z.-]+)?$");
        AppVersion.Current.ShouldNotBe("0.0.0");
    }

    [Fact]
    public void Line_IsTheNameAndTheVersionOnOneLine()
    {
        AppVersion.Line.ShouldMatch(@"^pusula \d+\.\d+\.\d+(?:-[0-9A-Za-z.-]+)?$");
        AppVersion.Line.ShouldEndWith(AppVersion.Current);
    }
}
