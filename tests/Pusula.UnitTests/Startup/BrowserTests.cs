using System.Net;
using System.Text;
using Pusula.Startup;
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

    // ---- IsPusulaAsync: what answers at the address ----------------------------------------------------------------

    private const string Page = "http://localhost:5190/";

    [Fact]
    public async Task IsPusulaAsync_ListOfSourcesWithAVersion_IsTrue()
    {
        using var handler = new Answer(HttpStatusCode.OK, """{ "sources": [], "canEdit": true, "machine": "pc", "version": "0.3.0" }""");

        (await Browser.IsPusulaAsync(Page, handler, TimeSpan.FromSeconds(5))).ShouldBeTrue();

        handler.Asked.ShouldBe(["http://localhost:5190/api/sources"]);
    }

    [Theory]
    [InlineData("""{ "sources": [] }""")]
    [InlineData("""{ "version": "0.3.0" }""")]
    [InlineData("""{ "sources": {}, "version": "0.3.0" }""")]
    [InlineData("""{ "sources": [], "version": 3 }""")]
    [InlineData("""[ "sources", "version" ]""")]
    [InlineData("<html>another server</html>")]
    [InlineData("")]
    public async Task IsPusulaAsync_AnswerThatIsNotTheListOfAPusula_IsFalse(string body)
    {
        using var handler = new Answer(HttpStatusCode.OK, body);

        (await Browser.IsPusulaAsync(Page, handler, TimeSpan.FromSeconds(5))).ShouldBeFalse();
    }

    [Theory]
    [InlineData(HttpStatusCode.NotFound)]
    [InlineData(HttpStatusCode.BadRequest)]
    [InlineData(HttpStatusCode.InternalServerError)]
    public async Task IsPusulaAsync_StatusThatIsNotSuccess_IsFalse(HttpStatusCode status)
    {
        using var handler = new Answer(status, """{ "sources": [], "version": "0.3.0" }""");

        (await Browser.IsPusulaAsync(Page, handler, TimeSpan.FromSeconds(5))).ShouldBeFalse();
    }

    // The answer of the address itself counts: a redirect is not followed (the application's handler does not follow it either).
    [Fact]
    public async Task IsPusulaAsync_Redirect_IsFalse()
    {
        using var handler = new Answer(HttpStatusCode.Found, """{ "sources": [], "version": "0.3.0" }""");

        (await Browser.IsPusulaAsync(Page, handler, TimeSpan.FromSeconds(5))).ShouldBeFalse();
    }

    // Whatever holds the port may send more than the list of a pusula: it is not read into memory whole.
    [Fact]
    public async Task IsPusulaAsync_AnswerLongerThanTheLimit_IsFalse()
    {
        string padding = new(' ', Browser.ProbeMaxBytes);
        using var handler = new Answer(HttpStatusCode.OK, $$"""{ "sources": [], "version": "0.3.0" }{{padding}}""");

        (await Browser.IsPusulaAsync(Page, handler, TimeSpan.FromSeconds(5))).ShouldBeFalse();
    }

    [Fact]
    public async Task IsPusulaAsync_AnswerJustUnderTheLimit_IsRead()
    {
        const string Json = """{ "sources": [], "version": "0.3.0" }""";
        using var handler = new Answer(HttpStatusCode.OK, Json + new string(' ', Browser.ProbeMaxBytes - Json.Length));

        (await Browser.IsPusulaAsync(Page, handler, TimeSpan.FromSeconds(5))).ShouldBeTrue();
    }

    [Fact]
    public async Task IsPusulaAsync_ConnectionThatFails_IsFalse()
    {
        using var handler = new Answer(new HttpRequestException("refused"));

        (await Browser.IsPusulaAsync(Page, handler, TimeSpan.FromSeconds(5))).ShouldBeFalse();
    }

    // A program that takes the connection and never answers (the port is held by something that is not a web server).
    [Fact]
    public async Task IsPusulaAsync_NoAnswerInTime_IsFalse()
    {
        using var handler = new Answer(silent: true);

        (await Browser.IsPusulaAsync(Page, handler, TimeSpan.FromMilliseconds(100))).ShouldBeFalse();
    }

    // A real request to a port nothing listens on, the way the application asks.
    [Fact]
    public void IsPusula_NothingListens_IsFalse()
    {
        var listener = new System.Net.Sockets.TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        int port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();

        Browser.IsPusula($"http://127.0.0.1:{port}/").ShouldBeFalse();
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

    /// <summary>A handler that answers every request the same way, and remembers what was asked.</summary>
    private sealed class Answer : HttpMessageHandler
    {
        private readonly HttpStatusCode _status;
        private readonly string _body = string.Empty;
        private readonly Exception? _failure;
        private readonly bool _silent;

        public Answer(HttpStatusCode status, string body)
        {
            _status = status;
            _body = body;
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

            return new HttpResponseMessage(_status) { Content = new StringContent(_body, Encoding.UTF8, "application/json") };
        }
    }
}
