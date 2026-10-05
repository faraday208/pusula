using Pusula.Startup;
using Shouldly;
using Xunit;

namespace Pusula.UnitTests.Startup;

public sealed class NetworkWarningTests
{
    private const string Tail = ": anyone who can reach this address can read the folders shown here; there is no login.";

    [Theory]
    [InlineData("http://localhost:5190")]
    [InlineData("http://127.0.0.1:5190")]
    [InlineData("http://[::1]:5190")]
    public void For_ThisComputerOnly_IsNull(string address) =>
        NetworkWarning.For([address]).ShouldBeNull();

    [Fact]
    public void For_LocalhostOnBothFamilies_IsNull() =>
        NetworkWarning.For(["http://127.0.0.1:5190", "http://[::1]:5190"]).ShouldBeNull();

    [Fact]
    public void For_NoAddress_IsNull() =>
        NetworkWarning.For([]).ShouldBeNull();

    [Theory]
    [InlineData("http://192.0.2.10:5190")]
    [InlineData("http://0.0.0.0:5190")]
    [InlineData("http://[::]:5190")]
    [InlineData("http://pc.example.ts.net:5190")]
    public void For_AddressOtherDevicesCanReach_IsTheLineThatNamesIt(string address) =>
        NetworkWarning.For([address]).ShouldBe($"pusula: listening on {address}{Tail}");

    [Fact]
    public void For_SomeAddressesReachable_NamesOnlyThose() =>
        NetworkWarning.For(["http://localhost:5190", "http://192.0.2.10:5190", "http://198.51.100.7:5190"])
            .ShouldBe($"pusula: listening on http://192.0.2.10:5190, http://198.51.100.7:5190{Tail}");

    [Fact]
    public void For_AddressThatCannotBeRead_CountsAsReachable() =>
        NetworkWarning.For(["not an address"]).ShouldBe($"pusula: listening on not an address{Tail}");

    [Fact]
    public void For_NullAddresses_Throws() =>
        Should.Throw<ArgumentNullException>(() => NetworkWarning.For(null!));
}
