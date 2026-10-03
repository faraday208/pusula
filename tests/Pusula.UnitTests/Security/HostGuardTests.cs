using Pusula.Security;
using Shouldly;
using Xunit;

namespace Pusula.UnitTests.Security;

public sealed class HostGuardTests
{
    private const string Machine = "testhost";

    private static bool IsAllowed(string? host, params string[] allowedHosts) => HostGuard.IsAllowed(host, Machine, allowedHosts);

    [Theory]
    [InlineData("localhost")]
    [InlineData("localhost:5190")]
    [InlineData("LOCALHOST")]
    [InlineData("LocalHost:80")]
    [InlineData("app.localhost")]
    [InlineData("a.b.localhost:1234")]
    [InlineData("APP.LOCALHOST")]
    public void IsAllowed_LocalhostAndItsSubdomains_IsTrue(string host) =>
        IsAllowed(host).ShouldBeTrue();

    [Theory]
    [InlineData("127.0.0.1")]
    [InlineData("127.0.0.1:5190")]
    [InlineData("10.1.2.3")]
    [InlineData("192.168.0.10:5190")]
    [InlineData("100.64.0.10:80")]
    [InlineData("0.0.0.0")]
    [InlineData("255.255.255.255:65535")]
    [InlineData("8.8.8.8")]
    public void IsAllowed_Ipv4Literal_IsTrue(string host) =>
        IsAllowed(host).ShouldBeTrue();

    [Theory]
    [InlineData("[::1]")]
    [InlineData("[::1]:5190")]
    [InlineData("[fe80::1]:80")]
    [InlineData("[2001:db8::1]")]
    [InlineData("[::ffff:127.0.0.1]:5190")]
    [InlineData("[2001:DB8:0:0:0:0:0:1]:1")]
    public void IsAllowed_Ipv6Literal_IsTrue(string host) =>
        IsAllowed(host).ShouldBeTrue();

    [Theory]
    [InlineData("testhost")]
    [InlineData("testhost:5190")]
    [InlineData("TESTHOST")]
    [InlineData("TestHost:80")]
    public void IsAllowed_NameOfThisMachine_IsTrueIgnoringCase(string host) =>
        IsAllowed(host).ShouldBeTrue();

    [Theory]
    [InlineData("node.tailnet-example.ts.net")]
    [InlineData("node.tailnet-example.ts.net:5190")]
    [InlineData("MYBOX.TS.NET")]
    [InlineData("a.ts.net")]
    public void IsAllowed_TailnetNames_IsTrue(string host) =>
        IsAllowed(host).ShouldBeTrue();

    [Theory]
    [InlineData("pusula.example.com")]
    [InlineData("PUSULA.EXAMPLE.COM:8443")]
    public void IsAllowed_NameFromTheAllowedList_IsTrueIgnoringCase(string host) =>
        IsAllowed(host, "other.example", "Pusula.Example.Com").ShouldBeTrue();

    [Theory]
    [InlineData("evil.example")]
    [InlineData("evil.example:5190")]
    [InlineData("pusula.example.com")]
    [InlineData("other-pc")]
    [InlineData("testhost.evil.example")]
    [InlineData("testhost2")]
    [InlineData("a.testhost")]
    public void IsAllowed_AnyOtherName_IsFalse(string host) =>
        IsAllowed(host).ShouldBeFalse();

    [Fact]
    public void IsAllowed_NameThatOnlyLooksLikeTheAllowedOnes_IsFalse()
    {
        IsAllowed("pusula.example.com.evil.example", "pusula.example.com").ShouldBeFalse();
        IsAllowed("xpusula.example.com", "pusula.example.com").ShouldBeFalse();
        IsAllowed("example.com", "pusula.example.com").ShouldBeFalse();
    }

    [Theory]
    [InlineData("localhost.evil.example")]
    [InlineData("evillocalhost")]
    [InlineData("notlocalhost:80")]
    [InlineData("localhost.")]
    [InlineData(".localhost")]
    [InlineData("localhost.localhost.evil.com")]
    [InlineData("ts.net")]
    [InlineData(".ts.net")]
    [InlineData("evilts.net")]
    [InlineData("evil.ts.net.example")]
    [InlineData("ts.net.evil.example")]
    public void IsAllowed_NamesThatOnlyContainLocalhostOrTsNet_IsFalse(string host) =>
        IsAllowed(host).ShouldBeFalse();

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData(":5190")]
    [InlineData("  localhost")]
    [InlineData("localhost  ")]
    [InlineData("local host")]
    public void IsAllowed_MissingOrBlankHost_IsFalse(string? host) =>
        IsAllowed(host).ShouldBeFalse();

    [Theory]
    [InlineData("localhost@evil.example")]
    [InlineData("user:pass@localhost")]
    [InlineData("localhost:80@evil.example")]
    [InlineData("evil.example#localhost")]
    [InlineData("evil.example?localhost")]
    [InlineData("localhost/evil.example")]
    [InlineData("evil.example\\localhost")]
    [InlineData("localhost,evil.example")]
    [InlineData("localhost;evil.example")]
    [InlineData("evil.example:80,localhost:80")]
    [InlineData("localhost%00")]
    [InlineData("loc\u00e4lhost")]
    [InlineData("localhost\tx")]
    [InlineData("localhost\r\nX: y")]
    public void IsAllowed_HostWithCharactersThatAreNotPartOfAName_IsFalse(string host) =>
        IsAllowed(host, host).ShouldBeFalse();

    [Theory]
    [InlineData("localhost:")]
    [InlineData("localhost:abc")]
    [InlineData("localhost:99999")]
    [InlineData("localhost:65536")]
    [InlineData("localhost:-1")]
    [InlineData("localhost:80:80")]
    [InlineData("localhost: 80")]
    [InlineData("localhost:123456")]
    [InlineData("127.0.0.1:")]
    [InlineData("127.0.0.1:port")]
    [InlineData("[::1]:")]
    [InlineData("[::1]:abc")]
    [InlineData("[::1]:99999")]
    [InlineData("[::1]5190")]
    public void IsAllowed_InvalidPort_IsFalse(string host) =>
        IsAllowed(host).ShouldBeFalse();

    [Theory]
    [InlineData("localhost:0")]
    [InlineData("localhost:00080")]
    [InlineData("localhost:65535")]
    public void IsAllowed_AnyPortInRange_IsTrue(string host) =>
        IsAllowed(host).ShouldBeTrue();

    [Theory]
    [InlineData("127.1")]
    [InlineData("0x7f.0.0.1")]
    [InlineData("2130706433")]
    [InlineData("127.0.0.01")]
    [InlineData("127.000.000.001")]
    [InlineData("1.2.3")]
    [InlineData("1.2.3.4.5")]
    [InlineData("256.1.1.1")]
    [InlineData("123")]
    [InlineData("1.2.3.4.evil.example")]
    public void IsAllowed_IpAddressNotInDottedQuadForm_IsNotTreatedAsAnIpLiteral(string host) =>
        IsAllowed(host).ShouldBeFalse();

    [Theory]
    [InlineData("::1")]
    [InlineData("::1:5190")]
    [InlineData("[::1")]
    [InlineData("[::1]x")]
    [InlineData("[]")]
    [InlineData("[]:80")]
    [InlineData("[not-an-ip]")]
    [InlineData("[127.0.0.1]")]
    [InlineData("[fe80::1%eth0]")]
    [InlineData("[fe80::1%25eth0]:80")]
    [InlineData("[::1]]")]
    [InlineData("[[::1]]")]
    [InlineData("[localhost]")]
    public void IsAllowed_MalformedIpv6_IsFalse(string host) =>
        IsAllowed(host).ShouldBeFalse();

    [Theory]
    [InlineData("a..localhost")]
    [InlineData("..")]
    [InlineData(".")]
    [InlineData("a.")]
    [InlineData(".a")]
    [InlineData("a..b")]
    public void IsAllowed_NameWithEmptyLabels_IsFalse(string host) =>
        IsAllowed(host, host).ShouldBeFalse();

    [Fact]
    public void IsAllowed_NameLongerThanADnsName_IsFalse()
    {
        string longName = new string('a', 250) + ".localhost";

        IsAllowed(longName).ShouldBeFalse();
        IsAllowed(new string('a', 243) + ".localhost").ShouldBeTrue();
    }

    [Fact]
    public void IsAllowed_AllowedListEntries_AreNotMatchedByPrefixSuffixOrPort()
    {
        IsAllowed("pusula.example.com:8443", "pusula.example.com:8443").ShouldBeFalse();
        IsAllowed("pusula", "pusula.example.com").ShouldBeFalse();
        IsAllowed("a-b", "a-b2", "x").ShouldBeFalse();
    }

    [Fact]
    public void IsAllowed_NoMachineName_NeverMatchesAName() =>
        HostGuard.IsAllowed("evil.example", string.Empty, []).ShouldBeFalse();

    [Theory]
    [InlineData("localhost:5190", "localhost", false)]
    [InlineData("my-host_1.example.com", "my-host_1.example.com", false)]
    [InlineData("127.0.0.1:1", "127.0.0.1", true)]
    [InlineData("[::1]:5190", "[::1]", true)]
    [InlineData("[::1]", "[::1]", true)]
    public void TryParse_ValidHeader_SplitsOffThePort(string header, string expectedHost, bool expectedIsIp)
    {
        HostGuard.TryParse(header, out string host, out bool isIp).ShouldBeTrue();

        host.ShouldBe(expectedHost);
        isIp.ShouldBe(expectedIsIp);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("a b")]
    [InlineData("[::1")]
    public void TryParse_InvalidHeader_ReturnsFalseAndNothing(string? header)
    {
        HostGuard.TryParse(header, out string host, out bool isIp).ShouldBeFalse();

        host.ShouldBeEmpty();
        isIp.ShouldBeFalse();
    }
}
