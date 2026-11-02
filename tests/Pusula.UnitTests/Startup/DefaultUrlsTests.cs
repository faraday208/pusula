using System.Net;
using System.Net.Sockets;
using Microsoft.Extensions.Configuration;
using Pusula.Startup;
using Pusula.UnitTests.Support;
using Shouldly;
using Xunit;

namespace Pusula.UnitTests.Startup;

public sealed class DefaultUrlsTests
{
    private static readonly Dictionary<string, string?> Urls = new() { ["urls"] = "http://localhost:5190" };

    private static string? NoEnvironment(string name) => null;

    private static IConfigurationProvider[] Providers(Action<IConfigurationBuilder> configure)
    {
        var builder = new ConfigurationBuilder();
        configure(builder);
        return [.. builder.Build().Providers];
    }

    // ---- Which provider wins ----------------------------------------------------------------------------------------

    [Fact]
    public void IsDefault_OnlyTheBuiltInSettings_IsTrue() =>
        DefaultUrls.IsDefault(NoEnvironment, Providers(b => b.AddInMemoryCollection(Urls))).ShouldBeTrue();

    [Fact]
    public void IsDefault_JsonFileOverTheBuiltInSettings_IsTrue()
    {
        using var temp = new TempDirectory();
        string file = temp.Write("appsettings.json", """{ "Urls": "http://localhost:6000" }""");

        DefaultUrls.IsDefault(NoEnvironment, Providers(b => b.AddInMemoryCollection(Urls).AddJsonFile(file))).ShouldBeTrue();
    }

    [Fact]
    public void IsDefault_JsonStream_IsTrue()
    {
        using var stream = new MemoryStream("""{ "Urls": "http://localhost:6000" }"""u8.ToArray());

        DefaultUrls.IsDefault(NoEnvironment, Providers(b => b.AddJsonStream(stream))).ShouldBeTrue();
    }

    [Fact]
    public void IsDefault_CommandLineOverTheBuiltInSettings_IsFalse() =>
        DefaultUrls.IsDefault(NoEnvironment, Providers(b => b.AddInMemoryCollection(Urls).AddCommandLine(["--urls", "http://localhost:5190"]))).ShouldBeFalse();

    [Fact]
    public void IsDefault_EnvironmentVariableOverTheBuiltInSettings_IsFalse()
    {
        string name = "PUSULA_TEST_" + Guid.NewGuid().ToString("N") + "_URLS";
        Environment.SetEnvironmentVariable(name, "http://localhost:5190");
        try
        {
            DefaultUrls.IsDefault(NoEnvironment, Providers(b => b.AddInMemoryCollection(Urls).AddEnvironmentVariables(prefix: name[..^"URLS".Length]))).ShouldBeFalse();
        }
        finally
        {
            Environment.SetEnvironmentVariable(name, null);
        }
    }

    [Fact]
    public void IsDefault_ASettingOnTopThatHasNoUrls_DoesNotChangeTheAnswer() =>
        DefaultUrls.IsDefault(NoEnvironment, Providers(b => b
            .AddInMemoryCollection(Urls)
            .AddCommandLine(["--Pusula:OpenBrowser", "false"]))).ShouldBeTrue();

    [Fact]
    public void IsDefault_CommandLineBelowAJsonFile_IsTrueBecauseTheLastOneWins()
    {
        using var temp = new TempDirectory();
        string file = temp.Write("appsettings.json", """{ "Urls": "http://localhost:6000" }""");

        DefaultUrls.IsDefault(NoEnvironment, Providers(b => b.AddCommandLine(["--urls", "http://localhost:1"]).AddJsonFile(file))).ShouldBeTrue();
    }

    // The host's settings sit under an appsettings.json of the content root (a JSON file wins over them), so the variable itself is looked at.
    [Theory]
    [InlineData("ASPNETCORE_URLS")]
    [InlineData("DOTNET_URLS")]
    [InlineData("URLS")]
    public void IsDefault_EnvironmentVariableSetAndJsonFileOnTop_IsFalse(string variable)
    {
        using var temp = new TempDirectory();
        string file = temp.Write("appsettings.json", """{ "Urls": "http://localhost:5190" }""");

        DefaultUrls.IsDefault(name => name == variable ? "http://127.0.0.1:6000" : null, Providers(b => b.AddInMemoryCollection(Urls).AddJsonFile(file))).ShouldBeFalse();
    }

    [Fact]
    public void IsDefault_EnvironmentVariableEmpty_DoesNotCount() =>
        DefaultUrls.IsDefault(_ => "", Providers(b => b.AddInMemoryCollection(Urls))).ShouldBeTrue();

    [Fact]
    public void IsDefault_NoUrlsAtAll_IsFalse() =>
        DefaultUrls.IsDefault(NoEnvironment, Providers(b => b.AddInMemoryCollection(new Dictionary<string, string?> { ["a"] = "b" }))).ShouldBeFalse();

    // ---- Whether a port can be bound ---------------------------------------------------------------------------------

    [Fact]
    public void CanBind_PortThatSomethingListensOn_IsFalse()
    {
        var held = new TcpListener(IPAddress.Loopback, 0);
        held.Start();
        try
        {
            DefaultUrls.CanBind(((IPEndPoint)held.LocalEndpoint).Port).ShouldBeFalse();
        }
        finally
        {
            held.Stop();
        }
    }

    [Fact]
    public void CanBind_PortThatIsFree_IsTrue()
    {
        var probe = new TcpListener(IPAddress.Loopback, 0);
        probe.Start();
        int port = ((IPEndPoint)probe.LocalEndpoint).Port;
        probe.Stop();

        DefaultUrls.CanBind(port).ShouldBeTrue();
    }

    [Fact]
    public void CanBind_ClosesWhatItOpened()
    {
        var probe = new TcpListener(IPAddress.Loopback, 0);
        probe.Start();
        int port = ((IPEndPoint)probe.LocalEndpoint).Port;
        probe.Stop();

        DefaultUrls.CanBind(port).ShouldBeTrue();
        DefaultUrls.CanBind(port).ShouldBeTrue();
    }

    // ---- The choice ---------------------------------------------------------------------------------------------------

    [Fact]
    public void Fallback_DefaultUrlsAndPortTaken_IsAnyFreePortOnLoopback() =>
        DefaultUrls.Fallback(true, "http://localhost:5190", port => port != 5190).ShouldBe("http://127.0.0.1:0");

    [Fact]
    public void Fallback_DefaultUrlsAndPortFree_IsNone()
    {
        int? asked = null;

        DefaultUrls.Fallback(true, "http://localhost:5190", port => (asked = port) is not null).ShouldBeNull();
        asked.ShouldBe(5190);
    }

    [Fact]
    public void Fallback_ChosenUrls_IsNoneAndNothingIsAsked() =>
        DefaultUrls.Fallback(false, "http://localhost:5190", _ => throw new InvalidOperationException("must not be asked")).ShouldBeNull();

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not an address")]
    [InlineData("http://localhost:5190;http://localhost:5191")]
    [InlineData("http://localhost:0")]
    public void Fallback_NoOneAddressWithAPort_IsNone(string? urls) =>
        DefaultUrls.Fallback(true, urls, _ => false).ShouldBeNull();
}
