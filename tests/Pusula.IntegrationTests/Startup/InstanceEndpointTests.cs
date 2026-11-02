using System.Buffers.Text;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Logging.Abstractions;
using Pusula.IntegrationTests.Support;
using Pusula.Startup;
using Shouldly;
using Xunit;

namespace Pusula.IntegrationTests.Startup;

/// <summary>
/// <c>GET /api/instance</c> and the check that a second pusula makes with it. The proof is tied to the endpoint of the connection, so
/// the answers that depend on it run over a real Kestrel (the in-memory test server has no addresses); who is calling is simulated for the
/// refusals (see <see cref="SimulatedConnection"/>).
/// </summary>
public sealed class InstanceEndpointTests
{
    private static readonly string Challenge = Base64Url.EncodeToString(new byte[32]);

    private static UserDirectories DirectoriesOf(TempDirectory home, string state) => new(home.Path, home.Resolve("config"), state);

    // A state directory with the key of a pusula that opens the browser.
    private static TempDirectory StateWithKey()
    {
        var state = new TempDirectory();
        InstanceKey.EnsureExists(state.Path, [], NullLogger.Instance).ShouldBeTrue();
        return state;
    }

    private static PusulaFactory Listening(TempDirectory notes, TempDirectory home, string state)
    {
        var factory = new PusulaFactory(notes.Path, directories: DirectoriesOf(home, state));
        factory.UseKestrel(0);
        factory.StartServer();
        return factory;
    }

    private static string Instance(string challenge) => $"/api/instance?challenge={challenge}";

    private static async Task ShouldBeProblemAsync(HttpResponseMessage response, HttpStatusCode status, string code)
    {
        using JsonDocument json = await response.ReadJsonAsync();

        response.StatusCode.ShouldBe(status);
        response.Content.Headers.ContentType?.MediaType.ShouldBe("application/problem+json");
        json.RootElement.GetProperty("code").GetString().ShouldBe(code);
    }

    // ---- The endpoint, over a real connection -------------------------------------------------------------------------

    [Fact]
    public async Task GetInstance_RightChallengeOverARealConnection_ProvesWithTheKeyAndTheEndpoint()
    {
        using TempDirectory state = StateWithKey();
        using var notes = new TempDirectory();
        using var home = new TempDirectory();
        notes.Write("A.md", "# A\n");
        await using PusulaFactory factory = Listening(notes, home, state.Path);
        var address = factory.ClientOptions.BaseAddress;
        using var client = new HttpClient { BaseAddress = address };

        using HttpResponseMessage response = await client.GetResponseAsync(Instance(Challenge));
        using JsonDocument json = await response.ReadJsonAsync();

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        response.Headers.CacheControl?.NoStore.ShouldBeTrue();
        byte[] expected = InstanceKey.Proof(InstanceKey.Read(state.Path)!, Challenge, InstanceKey.EndpointOf(IPAddress.Parse(address.Host), address.Port));
        json.RootElement.GetProperty("proof").GetString().ShouldBe(Base64Url.EncodeToString(expected));
        json.RootElement.EnumerateObject().Count().ShouldBe(1);
    }

    [Theory]
    [InlineData("")]
    [InlineData("?challenge=")]
    [InlineData("?challenge=short")]
    [InlineData("?challenge=AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA=")]
    [InlineData("?challenge=AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA%2B")]
    [InlineData("?challenge=AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA")]
    public async Task GetInstance_ChallengeThatIsNotThe32BytesInBase64Url_Is400(string query)
    {
        using TempDirectory state = StateWithKey();
        using var notes = new TempDirectory();
        using var home = new TempDirectory();
        notes.Write("A.md", "# A\n");
        await using PusulaFactory factory = Listening(notes, home, state.Path);
        using var client = new HttpClient { BaseAddress = factory.ClientOptions.BaseAddress };

        using HttpResponseMessage response = await client.GetResponseAsync("/api/instance" + query);

        await ShouldBeProblemAsync(response, HttpStatusCode.BadRequest, "InvalidChallenge");
        response.Headers.CacheControl?.NoStore.ShouldBeTrue();
    }

    // A pusula that was started without the browser has made no key; one that finds the key loose does not trust it.
    [Fact]
    public async Task GetInstance_NoKey_Is404()
    {
        using var state = new TempDirectory();
        using var notes = new TempDirectory();
        using var home = new TempDirectory();
        notes.Write("A.md", "# A\n");
        await using PusulaFactory factory = Listening(notes, home, state.Path);
        using var client = new HttpClient { BaseAddress = factory.ClientOptions.BaseAddress };

        using HttpResponseMessage response = await client.GetResponseAsync(Instance(Challenge));

        await ShouldBeProblemAsync(response, HttpStatusCode.NotFound, "NoKey");
        Directory.GetFileSystemEntries(state.Path).ShouldBeEmpty();
    }

    [Fact]
    public async Task GetInstance_NoStateDirectory_Is404()
    {
        using var notes = new TempDirectory();
        using var home = new TempDirectory();
        notes.Write("A.md", "# A\n");
        await using PusulaFactory factory = Listening(notes, home, string.Empty);
        using var client = new HttpClient { BaseAddress = factory.ClientOptions.BaseAddress };

        using HttpResponseMessage response = await client.GetResponseAsync(Instance(Challenge));

        await ShouldBeProblemAsync(response, HttpStatusCode.NotFound, "NoKey");
    }

    // The key is read at every request: a key that is made (or taken away) while pusula runs counts at once.
    [Fact]
    public async Task GetInstance_KeyThatAppearsWhilePusulaRuns_IsUsedAtOnce()
    {
        using var state = new TempDirectory();
        using var notes = new TempDirectory();
        using var home = new TempDirectory();
        notes.Write("A.md", "# A\n");
        await using PusulaFactory factory = Listening(notes, home, state.Path);
        using var client = new HttpClient { BaseAddress = factory.ClientOptions.BaseAddress };
        using (HttpResponseMessage before = await client.GetResponseAsync(Instance(Challenge)))
        {
            before.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        }

        InstanceKey.EnsureExists(state.Path, [], NullLogger.Instance).ShouldBeTrue();

        using HttpResponseMessage after = await client.GetResponseAsync(Instance(Challenge));
        after.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    // A reverse proxy on this machine makes every client look as if it were here; its headers say it was not.
    [Theory]
    [InlineData("Forwarded", "for=192.0.2.10")]
    [InlineData("X-Forwarded-For", "192.0.2.10")]
    [InlineData("X-Forwarded-Host", "example.test")]
    [InlineData("X-Real-IP", "192.0.2.10")]
    public async Task GetInstance_ForwardedRequest_Is403(string header, string value)
    {
        using TempDirectory state = StateWithKey();
        using var notes = new TempDirectory();
        using var home = new TempDirectory();
        notes.Write("A.md", "# A\n");
        await using PusulaFactory factory = Listening(notes, home, state.Path);
        using var client = new HttpClient { BaseAddress = factory.ClientOptions.BaseAddress };
        using var request = new HttpRequestMessage(HttpMethod.Get, Instance(Challenge));
        request.Headers.TryAddWithoutValidation(header, value);

        using HttpResponseMessage response = await client.SendAsync(request, TestContext.Current.CancellationToken);

        await ShouldBeProblemAsync(response, HttpStatusCode.Forbidden, "Remote");
    }

    // Pusula:AllowRemoteEdit lifts nothing here: a request from another machine is never answered.
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task GetInstance_RequestFromAnotherMachine_Is403WhateverAllowRemoteEditSays(bool allowRemoteEdit)
    {
        using TempDirectory state = StateWithKey();
        using var notes = new TempDirectory();
        using var home = new TempDirectory();
        notes.Write("A.md", "# A\n");
        await using var factory = new PusulaFactory(notes.Path, simulateConnection: true, directories: DirectoriesOf(home, state.Path), allowRemoteEdit: allowRemoteEdit);
        using HttpClient client = factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, Instance(Challenge));
        request.Headers.Add(SimulatedConnection.RemoteHeader, "192.0.2.10");
        request.Headers.Add(SimulatedConnection.LocalHeader, "192.0.2.20");

        using HttpResponseMessage response = await client.SendAsync(request, TestContext.Current.CancellationToken);

        await ShouldBeProblemAsync(response, HttpStatusCode.Forbidden, "Remote");
    }

    // A connection with no address of the server end has no endpoint to tie a proof to.
    [Fact]
    public async Task GetInstance_ConnectionWithoutALocalAddress_Is404()
    {
        using TempDirectory state = StateWithKey();
        using var notes = new TempDirectory();
        using var home = new TempDirectory();
        notes.Write("A.md", "# A\n");
        await using var factory = new PusulaFactory(notes.Path, simulateConnection: true, directories: DirectoriesOf(home, state.Path));
        using HttpClient client = factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, Instance(Challenge));
        request.Headers.Add(SimulatedConnection.LocalHeader, SimulatedConnection.None);

        using HttpResponseMessage response = await client.SendAsync(request, TestContext.Current.CancellationToken);

        await ShouldBeProblemAsync(response, HttpStatusCode.NotFound, "NoKey");
    }

    // ---- The second pusula asks -----------------------------------------------------------------------------------

    [Fact]
    public async Task IsPusula_ARealPusulaOfTheSameUser_IsTrue()
    {
        using TempDirectory state = StateWithKey();
        using var notes = new TempDirectory();
        using var home = new TempDirectory();
        notes.Write("A.md", "# A\n");
        await using PusulaFactory factory = Listening(notes, home, state.Path);

        Browser.IsPusula(factory.ClientOptions.BaseAddress.ToString(), state.Path).ShouldBeTrue();
        Browser.AlreadyRunning(factory.ClientOptions.BaseAddress.ToString(), page => Browser.IsPusula(page, state.Path), static _ => true)
            .ShouldStartWith("pusula: already running at ");
    }

    // The key of another user: it answers, but with a proof that this key does not give.
    [Fact]
    public async Task IsPusula_APusulaWithAnotherKey_IsFalse()
    {
        using TempDirectory theirs = StateWithKey();
        using TempDirectory mine = StateWithKey();
        using var notes = new TempDirectory();
        using var home = new TempDirectory();
        notes.Write("A.md", "# A\n");
        await using PusulaFactory factory = Listening(notes, home, theirs.Path);

        Browser.IsPusula(factory.ClientOptions.BaseAddress.ToString(), mine.Path).ShouldBeFalse();
    }

    // A pusula that keeps no key (started without the browser, or an older version) is not opened: the old behaviour.
    [Fact]
    public async Task IsPusula_APusulaThatKeepsNoKey_IsFalse()
    {
        using TempDirectory mine = StateWithKey();
        using var theirs = new TempDirectory();
        using var notes = new TempDirectory();
        using var home = new TempDirectory();
        notes.Write("A.md", "# A\n");
        await using PusulaFactory factory = Listening(notes, home, theirs.Path);

        Browser.IsPusula(factory.ClientOptions.BaseAddress.ToString(), mine.Path).ShouldBeFalse();
    }

    [Fact]
    public async Task IsPusula_NoKeyOfMine_IsFalseAndTheSquatterIsNotAsked()
    {
        using var mine = new TempDirectory();
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        try
        {
            int port = ((IPEndPoint)listener.LocalEndpoint).Port;

            Browser.IsPusula($"http://127.0.0.1:{port}/", mine.Path).ShouldBeFalse();

            listener.Pending().ShouldBeFalse();
        }
        finally
        {
            listener.Stop();
        }

        await Task.CompletedTask;
    }

    // A program that took the port first and answers like the pusula of old (or just says what the proof would say if it knew).
    [Fact]
    public async Task IsPusula_ASquatterThatAnswersWithTheListOfSourcesOrAGuess_IsFalse()
    {
        using TempDirectory mine = StateWithKey();
        foreach (string body in new[] { """{ "sources": [], "canEdit": true, "machine": "pc", "version": "0.3.0" }""", $$"""{ "proof": "{{Base64Url.EncodeToString(new byte[32])}}" }""" })
        {
            var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            try
            {
                int port = ((IPEndPoint)listener.LocalEndpoint).Port;
                Task serving = ServeAsync(listener, static _ => Task.FromResult(("200 OK", string.Empty)), body);

                Browser.IsPusula($"http://127.0.0.1:{port}/", mine.Path).ShouldBeFalse();
                await serving;
            }
            finally
            {
                listener.Stop();
            }
        }
    }

    // A program on one port that passes the challenge on to the real pusula on another and returns its answer: the proof in it is for
    // the endpoint of the real pusula, not for the one that was asked.
    [Fact]
    public async Task IsPusula_ASquatterThatRelaysTheChallengeToTheRealPusula_IsFalse()
    {
        using TempDirectory state = StateWithKey();
        using var notes = new TempDirectory();
        using var home = new TempDirectory();
        notes.Write("A.md", "# A\n");
        await using PusulaFactory factory = Listening(notes, home, state.Path);
        using var real = new HttpClient { BaseAddress = factory.ClientOptions.BaseAddress };
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        try
        {
            int port = ((IPEndPoint)listener.LocalEndpoint).Port;
            Task serving = ServeAsync(listener, async target =>
            {
                using HttpResponseMessage answer = await real.GetAsync(target, TestContext.Current.CancellationToken);
                answer.StatusCode.ShouldBe(HttpStatusCode.OK);
                return ("200 OK", await answer.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
            }, body: null);

            Browser.IsPusula($"http://127.0.0.1:{port}/", state.Path).ShouldBeFalse();
            await serving;
        }
        finally
        {
            listener.Stop();
        }
    }

    // A program on port B that answers with a redirect to /api/instance of the real pusula on port A, challenge included. If the redirect
    // were followed, the proof of A would come back through the connection to B.
    [Fact]
    public async Task IsPusula_ASquatterThatRedirectsToTheRealPusula_IsFalse()
    {
        using TempDirectory state = StateWithKey();
        using var notes = new TempDirectory();
        using var home = new TempDirectory();
        notes.Write("A.md", "# A\n");
        await using PusulaFactory factory = Listening(notes, home, state.Path);
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        try
        {
            int port = ((IPEndPoint)listener.LocalEndpoint).Port;
            Task serving = ServeRedirectAsync(listener, factory.ClientOptions.BaseAddress);

            Browser.IsPusula($"http://127.0.0.1:{port}/", state.Path).ShouldBeFalse();
            await serving;
        }
        finally
        {
            listener.Stop();
        }
    }

    // The same with a handler that does follow redirects: the second connection and the port of the page reject it on their own.
    [Fact]
    public async Task IsPusulaAsync_RedirectThatIsFollowedToTheRealPusula_IsFalse()
    {
        using TempDirectory state = StateWithKey();
        using var notes = new TempDirectory();
        using var home = new TempDirectory();
        notes.Write("A.md", "# A\n");
        await using PusulaFactory factory = Listening(notes, home, state.Path);
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        try
        {
            int port = ((IPEndPoint)listener.LocalEndpoint).Port;
            Task serving = ServeRedirectAsync(listener, factory.ClientOptions.BaseAddress);
            var connection = new Browser.RecordedConnection();
            using var following = new SocketsHttpHandler { UseProxy = false, AllowAutoRedirect = true, ConnectCallback = connection.ConnectAsync };

            bool proved = await Browser.IsPusulaAsync($"http://127.0.0.1:{port}/", state.Path, following, () => (connection.First, connection.Count), TimeSpan.FromSeconds(5));
            await serving;

            proved.ShouldBeFalse();
            connection.Count.ShouldBe(2);
            connection.First!.Port.ShouldBe(port);

        }
        finally
        {
            listener.Stop();
        }
    }

    private static async Task ServeRedirectAsync(TcpListener listener, Uri real)
    {
        using TcpClient client = await listener.AcceptTcpClientAsync(TestContext.Current.CancellationToken);
        await using NetworkStream stream = client.GetStream();
        var buffer = new byte[8192];
        int read = await stream.ReadAsync(buffer, TestContext.Current.CancellationToken);
        string target = Encoding.ASCII.GetString(buffer, 0, read).Split(' ')[1];
        byte[] head = Encoding.ASCII.GetBytes($"HTTP/1.1 302 Found\r\nLocation: {new Uri(real, target)}\r\nContent-Length: 0\r\nConnection: close\r\n\r\n");
        await stream.WriteAsync(head, TestContext.Current.CancellationToken);
    }

    // One connection: reads the request, answers it (with the answer of a function of the request target, or a fixed body), and closes.
    private static async Task ServeAsync(TcpListener listener, Func<string, Task<(string Status, string Body)>> answer, string? body)
    {
        using TcpClient client = await listener.AcceptTcpClientAsync(TestContext.Current.CancellationToken);
        await using NetworkStream stream = client.GetStream();
        var buffer = new byte[8192];
        int read = await stream.ReadAsync(buffer, TestContext.Current.CancellationToken);
        string target = Encoding.ASCII.GetString(buffer, 0, read).Split(' ')[1];
        (string status, string text) = body is null ? await answer(target) : ("200 OK", body);
        byte[] bytes = Encoding.UTF8.GetBytes(text);
        byte[] head = Encoding.ASCII.GetBytes($"HTTP/1.1 {status}\r\nContent-Type: application/json\r\nContent-Length: {bytes.Length}\r\nConnection: close\r\n\r\n");
        await stream.WriteAsync(head, TestContext.Current.CancellationToken);
        await stream.WriteAsync(bytes, TestContext.Current.CancellationToken);
    }
}
