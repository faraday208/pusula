using System.Net;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Logging.Abstractions;
using Pusula.Startup;
using Pusula.UnitTests.Support;
using Shouldly;
using Xunit;

namespace Pusula.UnitTests.Startup;

public sealed class BrowserTests
{
    private static readonly Func<string, string?> NoDesktop = static _ => null;

    // ---- ShouldOpen: the setting, the environment, and a desktop to open it on -------------------------------------

    [Fact]
    public void ShouldOpen_NotSetOutsideDevelopmentOnWindows_IsTrue() =>
        Browser.ShouldOpen(setting: null, isDevelopment: false, isWindows: true, isMacOS: false, NoDesktop).ShouldBeTrue();

    [Fact]
    public void ShouldOpen_NotSetOutsideDevelopmentOnMacOS_IsTrue() =>
        Browser.ShouldOpen(setting: null, isDevelopment: false, isWindows: false, isMacOS: true, NoDesktop).ShouldBeTrue();

    // dotnet run: contributors start it again and again.
    [Fact]
    public void ShouldOpen_NotSetInDevelopment_IsFalse() =>
        Browser.ShouldOpen(setting: null, isDevelopment: true, isWindows: true, isMacOS: false, NoDesktop).ShouldBeFalse();

    [Fact]
    public void ShouldOpen_SetToTrueInDevelopment_IsTrue() =>
        Browser.ShouldOpen(setting: true, isDevelopment: true, isWindows: true, isMacOS: false, NoDesktop).ShouldBeTrue();

    // --no-browser
    [Fact]
    public void ShouldOpen_SetToFalse_IsFalseEverywhere()
    {
        Browser.ShouldOpen(setting: false, isDevelopment: false, isWindows: true, isMacOS: false, NoDesktop).ShouldBeFalse();
        Browser.ShouldOpen(setting: false, isDevelopment: false, isWindows: false, isMacOS: true, NoDesktop).ShouldBeFalse();
        Browser.ShouldOpen(setting: false, isDevelopment: false, isWindows: false, isMacOS: false, static _ => ":0").ShouldBeFalse();
    }

    // Over SSH, on a server, in CI.
    [Fact]
    public void ShouldOpen_LinuxWithoutADesktopSession_IsFalseEvenWhenSetToTrue()
    {
        Browser.ShouldOpen(setting: null, isDevelopment: false, isWindows: false, isMacOS: false, NoDesktop).ShouldBeFalse();
        Browser.ShouldOpen(setting: true, isDevelopment: false, isWindows: false, isMacOS: false, static _ => "").ShouldBeFalse();
    }

    [Theory]
    [InlineData("DISPLAY", ":0")]
    [InlineData("WAYLAND_DISPLAY", "wayland-0")]
    public void ShouldOpen_LinuxWithADesktopSession_IsTrue(string variable, string value) =>
        Browser.ShouldOpen(setting: null, isDevelopment: false, isWindows: false, isMacOS: false, name => name == variable ? value : null).ShouldBeTrue();

    // The browser would open on the screen of the remote computer, not in front of the person.
    [Theory]
    [InlineData("SSH_CONNECTION", "192.0.2.10 50000 192.0.2.20 22")]
    [InlineData("SSH_TTY", "/dev/pts/0")]
    public void ShouldOpen_InAnSshSession_IsFalseOnEverySystemUnlessSetToTrue(string variable, string value)
    {
        Func<string, string?> environment = name => name == variable ? value : name == "DISPLAY" ? ":0" : null;

        Browser.ShouldOpen(setting: null, isDevelopment: false, isWindows: true, isMacOS: false, environment).ShouldBeFalse();
        Browser.ShouldOpen(setting: null, isDevelopment: false, isWindows: false, isMacOS: true, environment).ShouldBeFalse();
        Browser.ShouldOpen(setting: null, isDevelopment: false, isWindows: false, isMacOS: false, environment).ShouldBeFalse();
        Browser.ShouldOpen(setting: true, isDevelopment: false, isWindows: false, isMacOS: true, environment).ShouldBeTrue();
    }

    [Fact]
    public void ShouldOpen_NullEnvironment_Throws() =>
        Should.Throw<ArgumentNullException>(() => Browser.ShouldOpen(null, false, false, false, null!));

    // ---- PageAddress: which address to open ------------------------------------------------------------------------

    [Theory]
    [InlineData("http://localhost:5190", "http://localhost:5190/")]
    [InlineData("http://127.0.0.1:5191", "http://127.0.0.1:5191/")]
    [InlineData("http://[::1]:5190", "http://[::1]:5190/")]
    [InlineData("https://localhost:5443", "https://localhost:5443/")]
    [InlineData("  http://localhost:5190  ", "http://localhost:5190/")]
    public void PageAddress_OneAddressOfThisComputer_IsThatAddress(string address, string expected) =>
        Browser.PageAddress([address]).ShouldBe(expected);

    // Every interface, as the server reports it and as the settings write it: the page is opened on this computer.
    [Theory]
    [InlineData("http://0.0.0.0:5190")]
    [InlineData("http://[::]:5190")]
    [InlineData("http://*:5190")]
    [InlineData("http://+:5190")]
    public void PageAddress_AddressOfEveryInterface_IsLocalhost(string address) =>
        Browser.PageAddress([address]).ShouldBe("http://localhost:5190/");

    [Fact]
    public void PageAddress_AnAddressOfThisComputerAmongOthers_IsChosen() =>
        Browser.PageAddress(["http://192.0.2.10:5190", "http://localhost:5190"]).ShouldBe("http://localhost:5190/");

    [Fact]
    public void PageAddress_OnlyAddressesOtherDevicesCanReach_IsTheFirst() =>
        Browser.PageAddress(["http://192.0.2.10:5190", "http://198.51.100.7:5190"]).ShouldBe("http://192.0.2.10:5190/");

    [Theory]
    [InlineData("not an address")]
    [InlineData("file:///etc/passwd")]
    [InlineData("ftp://localhost:21")]
    [InlineData("")]
    public void PageAddress_NothingThatIsAWebAddress_IsNull(string address) =>
        Browser.PageAddress([address]).ShouldBeNull();

    [Fact]
    public void PageAddress_NoAddress_IsNull() =>
        Browser.PageAddress([]).ShouldBeNull();

    [Fact]
    public void PageAddress_AddressThatIsNotAWebAddressBeforeOneThatIs_IsSkipped() =>
        Browser.PageAddress(["file:///x", "http://192.0.2.10:5190"]).ShouldBe("http://192.0.2.10:5190/");

    // ---- AlreadyRunning: the port is taken; a second double click ---------------------------------------------------

    [Fact]
    public void AlreadyRunning_APusulaAnswers_OpensItAndSaysSo()
    {
        var opened = new List<string>();

        string? line = Browser.AlreadyRunning("http://localhost:5190", static _ => true, address =>
        {
            opened.Add(address);
            return true;
        });

        opened.ShouldBe(["http://localhost:5190/"]);
        line.ShouldBe("pusula: already running at http://localhost:5190/; opened it in the browser.");
    }

    [Fact]
    public void AlreadyRunning_APusulaAnswersButNoBrowserOpens_SaysWhereItIs() =>
        Browser.AlreadyRunning("http://localhost:5190", static _ => true, static _ => false)
            .ShouldBe("pusula: already running at http://localhost:5190/; open that address in your browser.");

    // Another program: the line of the entry point about the port that is in use.
    [Fact]
    public void AlreadyRunning_SomethingElseHoldsThePort_IsNullAndOpensNothing() =>
        Browser.AlreadyRunning("http://localhost:5190", static _ => false, static _ => throw new InvalidOperationException("Nothing is to be opened."))
            .ShouldBeNull();

    [Fact]
    public void AlreadyRunning_SeveralAddresses_AsksTheOneOfThisComputer()
    {
        var asked = new List<string>();

        Browser.AlreadyRunning("http://192.0.2.10:5190; http://localhost:5190", address =>
        {
            asked.Add(address);
            return false;
        }, static _ => true);

        asked.ShouldBe(["http://localhost:5190/"]);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not an address")]
    public void AlreadyRunning_NoAddressKnown_IsNullAndAsksNothing(string? urls) =>
        Browser.AlreadyRunning(urls, static _ => throw new InvalidOperationException("Nothing is to be asked."), static _ => true).ShouldBeNull();

    // ---- IsPusulaAsync: what answers at the address has to prove it -------------------------------------------------

    private const string Page = "http://localhost:5190/";
    private static readonly IPEndPoint Local = new(IPAddress.Loopback, 5190);
    private static readonly TimeSpan Plenty = TimeSpan.FromSeconds(5);

    // A state directory with the key that a pusula that opens the browser has made; null state: no key.
    private static (TempDirectory State, byte[] Key) Keyed()
    {
        var state = new TempDirectory();
        InstanceKey.EnsureExists(state.Path, [], NullLogger.Instance).ShouldBeTrue();
        return (state, InstanceKey.Read(state.Path)!);
    }

    private static string ProofFor(byte[] key, HttpRequestMessage request, IPEndPoint endpoint)
    {
        string challenge = System.Web.HttpUtility.ParseQueryString(request.RequestUri!.Query)["challenge"]!;
        return Convert.ToBase64String(InstanceKey.Proof(key, challenge, InstanceKey.EndpointOf(endpoint.Address, endpoint.Port)))
            .TrimEnd('=').Replace('+', '-').Replace('/', '_');
    }

    [Fact]
    public async Task IsPusulaAsync_TheRightProofForTheEndpointItConnectedTo_IsTrue()
    {
        (TempDirectory state, byte[] key) = Keyed();
        using (state)
        {
            using var handler = new Answer(request => $$"""{ "proof": "{{ProofFor(key, request, Local)}}" }""");

            (await Browser.IsPusulaAsync(Page, state.Path, handler, () => (Local, 1), Plenty)).ShouldBeTrue();

            handler.Asked.Count.ShouldBe(1);
            handler.Asked[0].ShouldStartWith("http://localhost:5190/api/instance?challenge=");
            InstanceKey.IsChallenge(handler.Asked[0]["http://localhost:5190/api/instance?challenge=".Length..]).ShouldBeTrue();
        }
    }

    // The connection of the application may be an IPv6 socket that carries an IPv4 address.
    [Fact]
    public async Task IsPusulaAsync_ConnectionThatWritesAnIPv4AddressAsIPv6_IsTheIPv4Endpoint()
    {
        (TempDirectory state, byte[] key) = Keyed();
        using (state)
        {
            using var handler = new Answer(request => $$"""{ "proof": "{{ProofFor(key, request, Local)}}" }""");

            (await Browser.IsPusulaAsync(Page, state.Path, handler, () => (new IPEndPoint(IPAddress.Loopback.MapToIPv6(), 5190), 1), Plenty)).ShouldBeTrue();
        }
    }

    // Something that read no key: it cannot know the proof.
    [Fact]
    public async Task IsPusulaAsync_ProofOfAnotherKey_IsFalse()
    {
        (TempDirectory state, _) = Keyed();
        using (state)
        {
            using var handler = new Answer(request => $$"""{ "proof": "{{ProofFor(RandomNumberGenerator.GetBytes(32), request, Local)}}" }""");

            (await Browser.IsPusulaAsync(Page, state.Path, handler, () => (Local, 1), Plenty)).ShouldBeFalse();
        }
    }

    // A program on this port that passes the challenge on to the real pusula on another port: the proof it gets is for that endpoint.
    [Theory]
    [InlineData("127.0.0.1", 5191)]
    [InlineData("127.0.0.2", 5190)]
    [InlineData("::1", 5190)]
    public async Task IsPusulaAsync_ProofOfAnotherEndpointRelayed_IsFalse(string address, int port)
    {
        (TempDirectory state, byte[] key) = Keyed();
        using (state)
        {
            var relayed = new IPEndPoint(IPAddress.Parse(address), port);
            using var handler = new Answer(request => $$"""{ "proof": "{{ProofFor(key, request, relayed)}}" }""");

            (await Browser.IsPusulaAsync(Page, state.Path, handler, () => (Local, 1), Plenty)).ShouldBeFalse();
        }
    }

    // The answer of a pusula that is older than the proof: the list of its sources.
    [Theory]
    [InlineData("""{ "sources": [], "canEdit": true, "machine": "pc", "version": "0.3.0" }""")]
    [InlineData("""{ "proof": 3 }""")]
    [InlineData("""{ "proof": null }""")]
    [InlineData("""{ "proof": "" }""")]
    [InlineData("""{ "proof": "not base64url !" }""")]
    [InlineData("""{ "proof": "AAAA" }""")]
    [InlineData("""[ "proof" ]""")]
    [InlineData("<html>another server</html>")]
    [InlineData("")]
    public async Task IsPusulaAsync_AnswerThatIsNotAProof_IsFalse(string body)
    {
        (TempDirectory state, _) = Keyed();
        using (state)
        {
            using var handler = new Answer(_ => body);

            (await Browser.IsPusulaAsync(Page, state.Path, handler, () => (Local, 1), Plenty)).ShouldBeFalse();
        }
    }

    // A pusula that has no key (started without the browser) or is older than the endpoint answers 404 or 403.
    [Theory]
    [InlineData(HttpStatusCode.NotFound)]
    [InlineData(HttpStatusCode.Forbidden)]
    [InlineData(HttpStatusCode.BadRequest)]
    [InlineData(HttpStatusCode.InternalServerError)]
    public async Task IsPusulaAsync_StatusThatIsNotSuccess_IsFalse(HttpStatusCode status)
    {
        (TempDirectory state, byte[] key) = Keyed();
        using (state)
        {
            using var handler = new Answer(request => $$"""{ "proof": "{{ProofFor(key, request, Local)}}" }""", status);

            (await Browser.IsPusulaAsync(Page, state.Path, handler, () => (Local, 1), Plenty)).ShouldBeFalse();
        }
    }

    // A second connection means that the request was sent on (a redirect was followed), whatever the proof is.
    [Fact]
    public async Task IsPusulaAsync_MoreThanOneConnection_IsFalse()
    {
        (TempDirectory state, byte[] key) = Keyed();
        using (state)
        {
            using var handler = new Answer(request => $$"""{ "proof": "{{ProofFor(key, request, Local)}}" }""");

            (await Browser.IsPusulaAsync(Page, state.Path, handler, () => (Local, 2), Plenty)).ShouldBeFalse();
        }
    }

    // The proof of a pusula on another port is not the proof of the page that was asked, even from a single connection.
    [Fact]
    public async Task IsPusulaAsync_ConnectedToAnotherPortThanThePage_IsFalse()
    {
        (TempDirectory state, byte[] key) = Keyed();
        using (state)
        {
            var other = new IPEndPoint(IPAddress.Loopback, 5191);
            using var handler = new Answer(request => $$"""{ "proof": "{{ProofFor(key, request, other)}}" }""");

            (await Browser.IsPusulaAsync(Page, state.Path, handler, () => (other, 1), Plenty)).ShouldBeFalse();
        }
    }

    // The connection was not recorded (the handler did not connect): there is no endpoint to check the proof against.
    [Fact]
    public async Task IsPusulaAsync_NoEndpointWasConnectedTo_IsFalse()
    {
        (TempDirectory state, byte[] key) = Keyed();
        using (state)
        {
            using var handler = new Answer(request => $$"""{ "proof": "{{ProofFor(key, request, Local)}}" }""");

            (await Browser.IsPusulaAsync(Page, state.Path, handler, () => (null, 1), Plenty)).ShouldBeFalse();
        }
    }

    // Whatever holds the port may send more than a proof: it is not read into memory whole.
    [Fact]
    public async Task IsPusulaAsync_AnswerLongerThanTheLimit_IsFalse()
    {
        (TempDirectory state, byte[] key) = Keyed();
        using (state)
        {
            using var handler = new Answer(request => $$"""{ "proof": "{{ProofFor(key, request, Local)}}" }""" + new string(' ', Browser.ProbeMaxBytes));

            (await Browser.IsPusulaAsync(Page, state.Path, handler, () => (Local, 1), Plenty)).ShouldBeFalse();
        }
    }

    [Fact]
    public async Task IsPusulaAsync_AnswerJustUnderTheLimit_IsRead()
    {
        (TempDirectory state, byte[] key) = Keyed();
        using (state)
        {
            using var handler = new Answer(request =>
            {
                string json = $$"""{ "proof": "{{ProofFor(key, request, Local)}}" }""";
                return json + new string(' ', Browser.ProbeMaxBytes - json.Length);
            });

            (await Browser.IsPusulaAsync(Page, state.Path, handler, () => (Local, 1), Plenty)).ShouldBeTrue();
        }
    }

    [Fact]
    public async Task IsPusulaAsync_ConnectionThatFails_IsFalse()
    {
        (TempDirectory state, _) = Keyed();
        using (state)
        {
            using var handler = new Answer(new HttpRequestException("refused"));

            (await Browser.IsPusulaAsync(Page, state.Path, handler, () => (Local, 1), Plenty)).ShouldBeFalse();
        }
    }

    // A program that takes the connection and never answers (the port is held by something that is not a web server).
    [Fact]
    public async Task IsPusulaAsync_NoAnswerInTime_IsFalse()
    {
        (TempDirectory state, _) = Keyed();
        using (state)
        {
            using var handler = new Answer(silent: true);

            (await Browser.IsPusulaAsync(Page, state.Path, handler, () => (Local, 1), TimeSpan.FromMilliseconds(100))).ShouldBeFalse();
        }
    }

    // Without a key nothing can be proved, and nothing is asked: the address is not even connected to.
    [Fact]
    public async Task IsPusulaAsync_NoKey_IsFalseAndSendsNoRequest()
    {
        using var state = new TempDirectory();
        using var handler = new Answer(_ => throw new InvalidOperationException("Nothing is to be asked."));

        (await Browser.IsPusulaAsync(Page, state.Path, handler, () => (Local, 1), Plenty)).ShouldBeFalse();
        (await Browser.IsPusulaAsync(Page, string.Empty, handler, () => (Local, 1), Plenty)).ShouldBeFalse();

        handler.Asked.ShouldBeEmpty();
    }

    // A real request to a port nothing listens on, the way the application asks.
    [Fact]
    public void IsPusula_NothingListens_IsFalse()
    {
        (TempDirectory state, _) = Keyed();
        using (state)
        {
            var listener = new System.Net.Sockets.TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            int port = ((IPEndPoint)listener.LocalEndpoint).Port;
            listener.Stop();

            Browser.IsPusula($"http://127.0.0.1:{port}/", state.Path).ShouldBeFalse();
        }
    }

    // Something that holds the port and answers with the list of sources of a pusula, the way a squatter would.
    [Fact]
    public async Task IsPusula_ASquatterThatAnswersLikeAPusulaOfAnOlderVersion_IsFalse()
    {
        (TempDirectory state, _) = Keyed();
        using (state)
        {
            var listener = new System.Net.Sockets.TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            try
            {
                int port = ((IPEndPoint)listener.LocalEndpoint).Port;
                Task serving = ServeAsync(listener, """{ "sources": [], "version": "0.3.0" }""");

                Browser.IsPusula($"http://127.0.0.1:{port}/", state.Path).ShouldBeFalse();
                await serving;
            }
            finally
            {
                listener.Stop();
            }
        }
    }

    private static async Task ServeAsync(System.Net.Sockets.TcpListener listener, string body)
    {
        using System.Net.Sockets.TcpClient client = await listener.AcceptTcpClientAsync();
        await using System.Net.Sockets.NetworkStream stream = client.GetStream();
        var buffer = new byte[4096];
        _ = await stream.ReadAsync(buffer);
        byte[] answer = Encoding.UTF8.GetBytes($"HTTP/1.1 200 OK\r\nContent-Type: application/json\r\nContent-Length: {body.Length}\r\nConnection: close\r\n\r\n{body}");
        await stream.WriteAsync(answer);
    }

    // ---- StartInfo: how the browser is started ---------------------------------------------------------------------

    [Fact]
    public void StartInfo_Windows_LetsTheShellOpenTheAddress()
    {
        System.Diagnostics.ProcessStartInfo start = Browser.StartInfo(Page, isWindows: true, isMacOS: false);

        start.FileName.ShouldBe(Page);
        start.UseShellExecute.ShouldBeTrue();
    }

    [Theory]
    [InlineData(true, "open")]
    [InlineData(false, "xdg-open")]
    public void StartInfo_MacOSAndLinux_GiveTheAddressAsTheOneArgumentOfTheOpener(bool isMacOS, string opener)
    {
        System.Diagnostics.ProcessStartInfo start = Browser.StartInfo(Page, isWindows: false, isMacOS);

        start.FileName.ShouldBe(opener);
        start.ArgumentList.ShouldBe([Page]);
        start.Arguments.ShouldBeEmpty();
        start.UseShellExecute.ShouldBeFalse();
    }

    /// <summary>A handler that answers every request from what it asked, and remembers what was asked.</summary>
    private sealed class Answer : HttpMessageHandler
    {
        private readonly HttpStatusCode _status;
        private readonly Func<HttpRequestMessage, string>? _body;
        private readonly Exception? _failure;
        private readonly bool _silent;

        public Answer(Func<HttpRequestMessage, string> body, HttpStatusCode status = HttpStatusCode.OK)
        {
            _body = body;
            _status = status;
        }

        public Answer(Exception failure) => _failure = failure;

        public Answer(bool silent) => _silent = silent;

        public List<string> Asked { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Asked.Add(request.RequestUri!.ToString());
            if (_failure is not null)
            {
                throw _failure;
            }

            if (_silent)
            {
                await Task.Delay(Timeout.Infinite, cancellationToken);
            }

            return new HttpResponseMessage(_status) { Content = new StringContent(_body!(request), Encoding.UTF8, "application/json") };
        }
    }
}
