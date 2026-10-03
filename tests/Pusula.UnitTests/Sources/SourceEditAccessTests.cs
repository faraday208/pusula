using System.Net;
using Microsoft.AspNetCore.Http;
using Pusula.Sources;
using Shouldly;
using Xunit;

namespace Pusula.UnitTests.Sources;

public sealed class SourceEditAccessTests
{
    private static IPAddress? Address(string? text) => text is null ? null : IPAddress.Parse(text);

    private static DefaultHttpContext Context(string? remote, string? local, string scheme = "http", string host = "localhost:5190", (string Name, string Value)[]? headers = null)
    {
        var context = new DefaultHttpContext();
        context.Connection.RemoteIpAddress = Address(remote);
        context.Connection.LocalIpAddress = Address(local);
        context.Request.Scheme = scheme;
        context.Request.Host = new HostString(host);
        foreach ((string name, string value) in headers ?? [])
        {
            context.Request.Headers[name] = value;
        }

        return context;
    }

    // ---- The machine --------------------------------------------------------------------------------------------

    [Theory]
    [InlineData("127.0.0.1", null)]
    [InlineData("127.8.9.10", "192.0.2.10")]
    [InlineData("::1", null)]
    [InlineData("::1", "fd7a:115c:a1e0::1")]
    [InlineData("::ffff:127.0.0.1", null)]
    public void IsSameMachine_LoopbackAddress_IsThisMachineWhateverTheServerAddressIs(string remote, string? local) =>
        SourceEditAccess.IsSameMachine(Address(remote), Address(local)).ShouldBeTrue();

    [Theory]
    [InlineData("192.0.2.10", "192.0.2.10")]
    [InlineData("100.64.0.1", "100.64.0.1")]
    [InlineData("fd7a:115c:a1e0::1", "fd7a:115c:a1e0::1")]
    [InlineData("::ffff:192.0.2.10", "192.0.2.10")]
    [InlineData("192.0.2.10", "::ffff:192.0.2.10")]
    [InlineData("::ffff:192.0.2.10", "::ffff:192.0.2.10")]
    public void IsSameMachine_ClientAtTheVeryAddressItConnectedTo_IsThisMachine(string remote, string local) =>
        SourceEditAccess.IsSameMachine(Address(remote), Address(local)).ShouldBeTrue();

    [Theory]
    [InlineData("198.51.100.50", "192.0.2.10")]
    [InlineData("100.64.0.2", "100.64.0.1")]
    [InlineData("::ffff:198.51.100.50", "192.0.2.10")]
    [InlineData("fd7a:115c:a1e0::2", "fd7a:115c:a1e0::1")]
    [InlineData("198.51.100.50", "127.0.0.1")]
    [InlineData("198.51.100.50", null)]
    [InlineData("203.0.113.9", null)]
    public void IsSameMachine_OtherAddress_IsAnotherMachine(string remote, string? local) =>
        SourceEditAccess.IsSameMachine(Address(remote), Address(local)).ShouldBeFalse();

    [Theory]
    [InlineData(null, null)]
    [InlineData(null, "127.0.0.1")]
    [InlineData(null, "192.0.2.10")]
    public void IsSameMachine_NoRemoteAddress_IsNotThisMachine(string? remote, string? local) =>
        SourceEditAccess.IsSameMachine(Address(remote), Address(local)).ShouldBeFalse();

    // ---- The proxy ----------------------------------------------------------------------------------------------

    [Theory]
    [InlineData("Forwarded", "for=203.0.113.9")]
    [InlineData("forwarded", "for=203.0.113.9;proto=https")]
    [InlineData("X-Forwarded-For", "203.0.113.9")]
    [InlineData("x-forwarded-for", "203.0.113.9, 10.0.0.2")]
    [InlineData("X-Forwarded-Host", "pusula.example")]
    [InlineData("X-Real-IP", "203.0.113.9")]
    [InlineData("x-real-ip", "203.0.113.9")]
    [InlineData("X-Forwarded-For", "")]
    [InlineData("X-Real-IP", "")]
    public void IsForwarded_RequestWithAForwardingHeader_IsForwardedWhateverTheHeaderSays(string header, string value) =>
        SourceEditAccess.IsForwarded(Context("127.0.0.1", null, headers: [(header, value)]).Request).ShouldBeTrue();

    [Fact]
    public void IsForwarded_RequestWithoutAForwardingHeader_IsNot()
    {
        SourceEditAccess.IsForwarded(Context("127.0.0.1", null).Request).ShouldBeFalse();
        SourceEditAccess.IsForwarded(Context("127.0.0.1", null, headers: [("Accept", "application/json"), ("User-Agent", "curl/8"), ("Origin", "http://localhost:5190")]).Request).ShouldBeFalse();
    }

    [Theory]
    [InlineData("Forwarded", "for=203.0.113.9")]
    [InlineData("X-Forwarded-For", "203.0.113.9")]
    [InlineData("X-Forwarded-Host", "pusula.example")]
    [InlineData("X-Real-IP", "203.0.113.9")]
    public void IsSameMachine_ConnectionFromThisMachineThatAProxyForwarded_IsNotThisMachine(string header, string value)
    {
        // A proxy on this machine: the connection says loopback (or the machine's own address), the header says better.
        SourceEditAccess.IsSameMachine(Context("127.0.0.1", null, headers: [(header, value)])).ShouldBeFalse();
        SourceEditAccess.IsSameMachine(Context("::1", "::1", headers: [(header, value)])).ShouldBeFalse();
        SourceEditAccess.IsSameMachine(Context("192.0.2.10", "192.0.2.10", headers: [(header, value)])).ShouldBeFalse();
    }

    [Fact]
    public void IsSameMachine_ConnectionFromThisMachineWithoutAProxy_IsThisMachine()
    {
        SourceEditAccess.IsSameMachine(Context("127.0.0.1", null)).ShouldBeTrue();
        SourceEditAccess.IsSameMachine(Context("192.0.2.10", "192.0.2.10")).ShouldBeTrue();
        SourceEditAccess.IsSameMachine(Context("198.51.100.50", "192.0.2.10")).ShouldBeFalse();
        SourceEditAccess.IsSameMachine(Context(null, null)).ShouldBeFalse();
    }

    // ---- The page -----------------------------------------------------------------------------------------------

    [Fact]
    public void IsSameOrigin_NeitherOriginNorFetchSite_IsNotAWebPageAndPasses() =>
        SourceEditAccess.IsSameOrigin(Context("127.0.0.1", null).Request).ShouldBeTrue();

    [Theory]
    [InlineData("http", "localhost:5190", "http://localhost:5190")]
    [InlineData("http", "localhost:5190", "HTTP://LOCALHOST:5190")]
    [InlineData("http", "LocalHost:5190", "http://localhost:5190")]
    [InlineData("http", "localhost", "http://localhost")]
    [InlineData("http", "localhost", "http://localhost:80")]
    [InlineData("http", "localhost:80", "http://localhost")]
    [InlineData("https", "pusula.example", "https://pusula.example")]
    [InlineData("https", "pusula.example", "https://pusula.example:443")]
    [InlineData("http", "[::1]:5190", "http://[::1]:5190")]
    [InlineData("http", "127.0.0.1:5190", "http://127.0.0.1:5190")]
    public void IsSameOrigin_OriginOfThisServer_Passes(string scheme, string host, string origin) =>
        SourceEditAccess.IsSameOrigin(Context("127.0.0.1", null, scheme, host, [("Origin", origin)]).Request).ShouldBeTrue();

    [Theory]
    [InlineData("http", "localhost:5190", "http://evil.example")]
    [InlineData("http", "localhost:5190", "http://localhost:5191")]
    [InlineData("http", "localhost:5190", "http://localhost")]
    [InlineData("http", "localhost:5190", "https://localhost:5190")]
    [InlineData("http", "localhost:5190", "http://localhost.evil.example:5190")]
    [InlineData("http", "localhost:5190", "http://127.0.0.1:5190")]
    [InlineData("http", "localhost", "https://localhost")]
    [InlineData("https", "pusula.example", "http://pusula.example")]
    [InlineData("http", "localhost:5190", "null")]
    [InlineData("http", "localhost:5190", "")]
    [InlineData("http", "localhost:5190", "localhost:5190")]
    [InlineData("http", "localhost:5190", "chrome-extension://abcdefgh")]
    [InlineData("http", "[::1]:5190", "http://[::2]:5190")]
    public void IsSameOrigin_OriginOfAnotherPageOrAnUnusableOne_Fails(string scheme, string host, string origin) =>
        SourceEditAccess.IsSameOrigin(Context("127.0.0.1", null, scheme, host, [("Origin", origin)]).Request).ShouldBeFalse();

    [Fact]
    public void IsSameOrigin_SeveralOriginHeaders_Fails()
    {
        DefaultHttpContext context = Context("127.0.0.1", null);
        context.Request.Headers.Append("Origin", "http://localhost:5190");
        context.Request.Headers.Append("Origin", "http://localhost:5190");

        SourceEditAccess.IsSameOrigin(context.Request).ShouldBeFalse();
    }

    [Theory]
    [InlineData("same-origin", true)]
    [InlineData("Same-Origin", true)]
    [InlineData("cross-site", false)]
    [InlineData("same-site", false)]
    [InlineData("none", false)]
    [InlineData("", false)]
    [InlineData("anything", false)]
    public void IsSameOrigin_FetchSite_MustSaySameOrigin(string site, bool expected) =>
        SourceEditAccess.IsSameOrigin(Context("127.0.0.1", null, headers: [("Sec-Fetch-Site", site)]).Request).ShouldBe(expected);

    // A request that only reads (the folder browser) may be one that the user made by hand: Sec-Fetch-Site says "none".
    [Theory]
    [InlineData("none", true)]
    [InlineData("None", true)]
    [InlineData("same-origin", true)]
    [InlineData("cross-site", false)]
    [InlineData("same-site", false)]
    [InlineData("", false)]
    [InlineData("anything", false)]
    public void IsSameOrigin_FetchSiteWhenUserInitiatedRequestsAreAllowed_IsSameOriginOrNone(string site, bool expected) =>
        SourceEditAccess.IsSameOrigin(Context("127.0.0.1", null, headers: [("Sec-Fetch-Site", site)]).Request, allowUserInitiated: true).ShouldBe(expected);

    [Fact]
    public void IsSameOrigin_FetchSiteNone_IsRefusedUnlessUserInitiatedRequestsAreAllowed()
    {
        HttpRequest request = Context("127.0.0.1", null, headers: [("Sec-Fetch-Site", "none")]).Request;

        SourceEditAccess.IsSameOrigin(request).ShouldBeFalse();
        SourceEditAccess.IsSameOrigin(request, allowUserInitiated: false).ShouldBeFalse();
        SourceEditAccess.IsSameOrigin(request, allowUserInitiated: true).ShouldBeTrue();
    }

    // The leniency is for Sec-Fetch-Site only: the Origin of a page of another server is refused all the same.
    [Fact]
    public void IsSameOrigin_UserInitiatedRequestsAllowed_StillRefusesAnotherOrigin()
    {
        SourceEditAccess.IsSameOrigin(Context("127.0.0.1", null, headers: [("Origin", "http://evil.example"), ("Sec-Fetch-Site", "none")]).Request, allowUserInitiated: true).ShouldBeFalse();
        SourceEditAccess.IsSameOrigin(Context("127.0.0.1", null, headers: [("Origin", "http://evil.example")]).Request, allowUserInitiated: true).ShouldBeFalse();
        SourceEditAccess.IsSameOrigin(Context("127.0.0.1", null, headers: [("Origin", "http://localhost:5190"), ("Sec-Fetch-Site", "none")]).Request, allowUserInitiated: true).ShouldBeTrue();
    }

    [Fact]
    public void Refusal_UserInitiatedRequest_IsRefusedForChangingSourcesAndAcceptedForReading()
    {
        DefaultHttpContext byHand = Context("127.0.0.1", null, headers: [("Sec-Fetch-Site", "none")]);

        SourceEditAccess.Refusal(byHand, SourcesFile).ShouldBe(EditError.CrossOrigin);
        SourceEditAccess.Refusal(byHand, SourcesFile, allowUserInitiated: true).ShouldBeNull();
    }

    [Fact]
    public void Refusal_UserInitiatedRequestsAllowed_KeepsTheOrderRemoteCommandLineCrossOrigin()
    {
        (string Name, string Value)[] foreign = [("Sec-Fetch-Site", "cross-site")];

        SourceEditAccess.Refusal(Context("198.51.100.50", "192.0.2.10", headers: foreign), sourcesFile: null, allowUserInitiated: true).ShouldBe(EditError.Remote);
        SourceEditAccess.Refusal(Context("127.0.0.1", null, headers: foreign), sourcesFile: null, allowUserInitiated: true).ShouldBe(EditError.CommandLine);
        SourceEditAccess.Refusal(Context("127.0.0.1", null, headers: foreign), SourcesFile, allowUserInitiated: true).ShouldBe(EditError.CrossOrigin);
        SourceEditAccess.Refusal(Context("198.51.100.50", "192.0.2.10", headers: [("Sec-Fetch-Site", "none")]), SourcesFile, allowRemote: true, allowUserInitiated: true).ShouldBeNull();
    }

    [Fact]
    public void IsSameOrigin_BothHeaders_BothMustAgree()
    {
        const string Origin = "http://localhost:5190";

        SourceEditAccess.IsSameOrigin(Context("127.0.0.1", null, headers: [("Origin", Origin), ("Sec-Fetch-Site", "same-origin")]).Request).ShouldBeTrue();
        SourceEditAccess.IsSameOrigin(Context("127.0.0.1", null, headers: [("Origin", Origin), ("Sec-Fetch-Site", "cross-site")]).Request).ShouldBeFalse();
        SourceEditAccess.IsSameOrigin(Context("127.0.0.1", null, headers: [("Origin", "http://evil.example"), ("Sec-Fetch-Site", "same-origin")]).Request).ShouldBeFalse();
    }

    // ---- What may be done ---------------------------------------------------------------------------------------

    private const string SourcesFile = "/home/test/.config/pusula/sources.json";

    [Fact]
    public void Blocked_SameMachineAndASourcesFile_IsNotBlocked() =>
        SourceEditAccess.Blocked(Context("127.0.0.1", null), SourcesFile).ShouldBeNull();

    [Fact]
    public void Blocked_AnotherMachine_IsRemote() =>
        SourceEditAccess.Blocked(Context("198.51.100.50", "192.0.2.10"), SourcesFile).ShouldBe(EditBlock.Remote);

    [Fact]
    public void Blocked_NoRemoteAddress_IsRemote() =>
        SourceEditAccess.Blocked(Context(null, null), SourcesFile).ShouldBe(EditBlock.Remote);

    [Theory]
    [InlineData("Forwarded", "for=203.0.113.9")]
    [InlineData("X-Forwarded-For", "203.0.113.9")]
    [InlineData("X-Forwarded-Host", "pusula.example")]
    [InlineData("X-Real-IP", "203.0.113.9")]
    public void Blocked_RequestThatAProxyForwarded_IsRemoteEvenFromTheLoopbackAddressAndBeforeTheCommandLine(string header, string value)
    {
        SourceEditAccess.Blocked(Context("127.0.0.1", null, headers: [(header, value)]), SourcesFile).ShouldBe(EditBlock.Remote);
        SourceEditAccess.Blocked(Context("127.0.0.1", null, headers: [(header, value)]), sourcesFile: null).ShouldBe(EditBlock.Remote);
        SourceEditAccess.Refusal(Context("127.0.0.1", null, headers: [(header, value), ("Origin", "http://evil.example")]), SourcesFile).ShouldBe(EditError.Remote);
    }

    [Fact]
    public void Blocked_ListFromTheCommandLine_IsCommandLine() =>
        SourceEditAccess.Blocked(Context("127.0.0.1", null), sourcesFile: null).ShouldBe(EditBlock.CommandLine);

    [Fact]
    public void Blocked_AnotherMachineAndTheCommandLine_IsRemoteFirst() =>
        SourceEditAccess.Blocked(Context("198.51.100.50", "192.0.2.10"), sourcesFile: null).ShouldBe(EditBlock.Remote);

    [Fact]
    public void Refusal_AllowedRequest_IsNull()
    {
        SourceEditAccess.Refusal(Context("127.0.0.1", null), SourcesFile).ShouldBeNull();
        SourceEditAccess.Refusal(Context("192.0.2.10", "192.0.2.10", headers: [("Origin", "http://localhost:5190"), ("Sec-Fetch-Site", "same-origin")]), SourcesFile).ShouldBeNull();
    }

    [Fact]
    public void Refusal_ForeignPageOnThisMachine_IsCrossOrigin()
    {
        SourceEditAccess.Refusal(Context("127.0.0.1", null, headers: [("Origin", "http://evil.example")]), SourcesFile).ShouldBe(EditError.CrossOrigin);
        SourceEditAccess.Refusal(Context("::1", null, headers: [("Sec-Fetch-Site", "cross-site")]), SourcesFile).ShouldBe(EditError.CrossOrigin);
    }

    [Fact]
    public void Refusal_EveryReason_ComesInTheOrderRemoteCommandLineCrossOrigin()
    {
        (string Name, string Value)[] foreign = [("Origin", "http://evil.example")];

        SourceEditAccess.Refusal(Context("198.51.100.50", "192.0.2.10", headers: foreign), sourcesFile: null).ShouldBe(EditError.Remote);
        SourceEditAccess.Refusal(Context("127.0.0.1", null, headers: foreign), sourcesFile: null).ShouldBe(EditError.CommandLine);
        SourceEditAccess.Refusal(Context("127.0.0.1", null, headers: foreign), SourcesFile).ShouldBe(EditError.CrossOrigin);
    }

    // ---- Pusula:AllowRemoteEdit ---------------------------------------------------------------------------------
    // It lifts "from this machine" and nothing else: the sources file and the page of this server are asked for all the same.

    [Theory]
    [InlineData("198.51.100.50", "192.0.2.10")]
    [InlineData("::ffff:198.51.100.50", "192.0.2.10")]
    [InlineData("100.64.0.2", "100.64.0.1")]
    [InlineData("198.51.100.50", null)]
    [InlineData(null, null)]
    public void Blocked_AnotherMachineWhileRemoteEditIsAllowed_IsNotBlocked(string? remote, string? local) =>
        SourceEditAccess.Blocked(Context(remote, local), SourcesFile, allowRemote: true).ShouldBeNull();

    [Theory]
    [InlineData("Forwarded", "for=203.0.113.9")]
    [InlineData("X-Forwarded-For", "203.0.113.9")]
    [InlineData("X-Forwarded-Host", "pusula.example")]
    [InlineData("X-Real-IP", "203.0.113.9")]
    public void Blocked_RequestThatAProxyForwardedWhileRemoteEditIsAllowed_IsNotBlocked(string header, string value)
    {
        SourceEditAccess.Blocked(Context("127.0.0.1", null, headers: [(header, value)]), SourcesFile, allowRemote: true).ShouldBeNull();
        SourceEditAccess.Blocked(Context("198.51.100.50", "192.0.2.10", headers: [(header, value)]), SourcesFile, allowRemote: true).ShouldBeNull();
    }

    [Fact]
    public void Blocked_WhileRemoteEditIsNotAllowed_IsRemoteForAnotherMachineWhetherTheSettingIsGivenOrNot()
    {
        SourceEditAccess.Blocked(Context("198.51.100.50", "192.0.2.10"), SourcesFile).ShouldBe(EditBlock.Remote);
        SourceEditAccess.Blocked(Context("198.51.100.50", "192.0.2.10"), SourcesFile, allowRemote: false).ShouldBe(EditBlock.Remote);
        SourceEditAccess.Blocked(Context("127.0.0.1", null, headers: [("X-Forwarded-For", "203.0.113.9")]), SourcesFile, allowRemote: false).ShouldBe(EditBlock.Remote);
    }

    [Fact]
    public void Blocked_ListFromTheCommandLineWhileRemoteEditIsAllowed_IsCommandLineWhoeverAsks()
    {
        SourceEditAccess.Blocked(Context("198.51.100.50", "192.0.2.10"), sourcesFile: null, allowRemote: true).ShouldBe(EditBlock.CommandLine);
        SourceEditAccess.Blocked(Context("127.0.0.1", null), sourcesFile: null, allowRemote: true).ShouldBe(EditBlock.CommandLine);
        SourceEditAccess.Blocked(Context(null, null), sourcesFile: null, allowRemote: true).ShouldBe(EditBlock.CommandLine);
    }

    [Fact]
    public void Refusal_AnotherMachineWithAPageOfThisServerWhileRemoteEditIsAllowed_IsAccepted()
    {
        (string Name, string Value)[] ownPage = [("Origin", "http://localhost:5190"), ("Sec-Fetch-Site", "same-origin")];

        SourceEditAccess.Refusal(Context("198.51.100.50", "192.0.2.10", headers: ownPage), SourcesFile, allowRemote: true).ShouldBeNull();

        // A script on another machine is not a web page: it has neither header and passes, as it does on this machine.
        SourceEditAccess.Refusal(Context("198.51.100.50", "192.0.2.10"), SourcesFile, allowRemote: true).ShouldBeNull();
    }

    [Fact]
    public void Refusal_AnotherPageWhileRemoteEditIsAllowed_IsStillCrossOrigin()
    {
        SourceEditAccess.Refusal(Context("198.51.100.50", "192.0.2.10", headers: [("Origin", "http://evil.example")]), SourcesFile, allowRemote: true).ShouldBe(EditError.CrossOrigin);
        SourceEditAccess.Refusal(Context("198.51.100.50", "192.0.2.10", headers: [("Sec-Fetch-Site", "cross-site")]), SourcesFile, allowRemote: true).ShouldBe(EditError.CrossOrigin);
        SourceEditAccess.Refusal(Context("127.0.0.1", null, headers: [("Origin", "null")]), SourcesFile, allowRemote: true).ShouldBe(EditError.CrossOrigin);
    }

    [Fact]
    public void Refusal_EveryReasonWhileRemoteEditIsAllowed_ComesInTheOrderCommandLineCrossOrigin()
    {
        (string Name, string Value)[] foreign = [("Origin", "http://evil.example")];

        SourceEditAccess.Refusal(Context("198.51.100.50", "192.0.2.10", headers: foreign), sourcesFile: null, allowRemote: true).ShouldBe(EditError.CommandLine);
        SourceEditAccess.Refusal(Context("198.51.100.50", "192.0.2.10", headers: foreign), SourcesFile, allowRemote: true).ShouldBe(EditError.CrossOrigin);

        // And without the setting the other machine is refused first, as before.
        SourceEditAccess.Refusal(Context("198.51.100.50", "192.0.2.10", headers: foreign), SourcesFile).ShouldBe(EditError.Remote);
    }
}
