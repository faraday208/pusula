using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Runtime.Versioning;
using System.Text.RegularExpressions;
using Pusula.IntegrationTests.Support;
using Pusula.Startup;
using Shouldly;
using Xunit;

namespace Pusula.IntegrationTests.Startup;

/// <summary>
/// Real pusula programs started without <c>--urls</c>, with the default port held by the test: pusula listens on another port and says
/// so, and a second one finds the first by the address it recorded. Linux only, as in <see cref="AlreadyRunningTests"/>: the browser is the
/// <c>xdg-open</c> that is not on the <c>PATH</c> of the children and the state folder is a temporary one. If something on the machine
/// holds the port already, the tests hold nothing and still pass: the fallback happens either way. They never touch what they did not start.
/// </summary>
public sealed class DefaultPortFallbackTests
{
    private const int DefaultPort = 5190;

    // Holds the default port on the loopback address; null when something else has it already.
    private static TcpListener? HoldDefaultPort()
    {
        try
        {
            var listener = new TcpListener(IPAddress.Loopback, DefaultPort);
            listener.Start();
            return listener;
        }
        catch (SocketException)
        {
            return null;
        }
    }

    private static Process Start(TempDirectory root, TempDirectory home, TempDirectory emptyPath, params string[] more) =>
        StartWith(root, home, emptyPath, null, more);

    private static Process StartWith(TempDirectory root, TempDirectory home, TempDirectory emptyPath, IReadOnlyDictionary<string, string>? environment, params string[] more)
    {
        var start = new ProcessStartInfo(Environment.GetEnvironmentVariable("DOTNET_HOST_PATH") ?? "dotnet")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,

            // Like the download: no appsettings.json in the folder it is started in (the one next to the test program would take the place of the environment).
            WorkingDirectory = home.Path,
        };
        start.ArgumentList.Add(typeof(Program).Assembly.Location);
        // Not a folder at the start of the command line: a pusula started with folders neither records its address nor looks it up.
        start.ArgumentList.Add("--Pusula:Root");
        start.ArgumentList.Add(root.Path);
        start.ArgumentList.Add("--Pusula:CheckForUpdates");
        start.ArgumentList.Add("false");
        foreach (string argument in more)
        {
            start.ArgumentList.Add(argument);
        }

        start.Environment["HOME"] = home.Path;
        start.Environment["XDG_STATE_HOME"] = home.Resolve("state");
        start.Environment["DISPLAY"] = ":0";
        start.Environment["PATH"] = emptyPath.Path;
        start.Environment["ASPNETCORE_ENVIRONMENT"] = "Production";
        start.Environment.Remove("ASPNETCORE_URLS");
        start.Environment.Remove("DOTNET_URLS");
        start.Environment.Remove("URLS");
        foreach ((string name, string value) in environment ?? new Dictionary<string, string>())
        {
            start.Environment[name] = value;
        }

        return Process.Start(start)!;
    }

    // The first line of the standard output that matches, within the time given.
    private static async Task<string> ReadLineMatchingAsync(Process process, Regex pattern, CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(30));
        while (await process.StandardOutput.ReadLineAsync(timeout.Token) is { } line)
        {
            if (pattern.IsMatch(line))
            {
                return line;
            }
        }

        throw new InvalidOperationException("The program ended without the line.");
    }

    [Fact]
    public async Task Pusula_DefaultPortHeld_ListensOnAnotherPortAndSaysSo()
    {
        Assert.SkipUnless(OperatingSystem.IsLinux(), "Needs Linux and an xdg-open that is not there.");
        using var root = new TempDirectory();
        using var home = new TempDirectory();
        using var emptyPath = new TempDirectory();
        root.Write("A.md", "# A\n");
        TcpListener? held = HoldDefaultPort();
        using Process pusula = Start(root, home, emptyPath, "--no-browser");
        try
        {
            string line = await ReadLineMatchingAsync(pusula, new Regex(@"^pusula: port 5190 is in use; listening on http://127\.0\.0\.1:\d+/ instead$"), TestContext.Current.CancellationToken);

            int port = int.Parse(Regex.Match(line, @"127\.0\.0\.1:(\d+)/").Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture);
            port.ShouldNotBe(DefaultPort);
            using var client = new HttpClient();
            using HttpResponseMessage response = await client.GetAsync($"http://127.0.0.1:{port}/health", TestContext.Current.CancellationToken);
            response.IsSuccessStatusCode.ShouldBeTrue();
            Directory.Exists(home.Resolve("state")).ShouldBeFalse();
        }
        finally
        {
            pusula.Kill(entireProcessTree: true);
            await pusula.WaitForExitAsync(TestContext.Current.CancellationToken);
            held?.Stop();
        }
    }

    [Fact]
    public async Task SecondPusula_FindsTheFirstOneByItsRecordedAddress_WhileTheDefaultPortIsHeld()
    {
        Assert.SkipUnless(OperatingSystem.IsLinux(), "Needs Linux and an xdg-open that is not there.");
        using var root = new TempDirectory();
        using var home = new TempDirectory();
        using var emptyPath = new TempDirectory();
        root.Write("A.md", "# A\n");
        string addressFile = home.Resolve("state/pusula/instance-address");
        TcpListener? held = HoldDefaultPort();
        using Process first = Start(root, home, emptyPath, "--Pusula:OpenBrowser", "true");
        try
        {
            string line = await ReadLineMatchingAsync(first, new Regex(@"^pusula: port 5190 is in use; listening on "), TestContext.Current.CancellationToken);
            string address = Regex.Match(line, @"http://127\.0\.0\.1:\d+/").Value;
            for (int wait = 0; wait < 100 && !File.Exists(addressFile); wait++)
            {
                await Task.Delay(100, TestContext.Current.CancellationToken);
            }

            (await File.ReadAllTextAsync(addressFile, TestContext.Current.CancellationToken)).ShouldBe(address + "\n");

            using Process second = Start(root, home, emptyPath, "--Pusula:OpenBrowser", "true");
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(30));
            await second.WaitForExitAsync(timeout.Token);

            second.ExitCode.ShouldBe(0);
            (await second.StandardOutput.ReadToEndAsync(TestContext.Current.CancellationToken)).ShouldContain($"already running at {address}");
        }
        finally
        {
            first.Kill(entireProcessTree: true);
            await first.WaitForExitAsync(TestContext.Current.CancellationToken);
            held?.Stop();
        }
    }

    [Fact]
    public async Task Pusula_ExplicitUrls_NeverUseTheRecordedAddress_AndATakenPortStopsIt()
    {
        Assert.SkipUnless(OperatingSystem.IsLinux(), "Needs Linux and an xdg-open that is not there.");
        using var root = new TempDirectory();
        using var home = new TempDirectory();
        using var emptyPath = new TempDirectory();
        root.Write("A.md", "# A\n");
        string addressFile = home.Resolve("state/pusula/instance-address");
        TcpListener? held = HoldDefaultPort();
        TcpListener squatter = HoldFreePort();
        using Process first = Start(root, home, emptyPath, "--Pusula:OpenBrowser", "true");
        try
        {
            // A live pusula that has recorded its address (on a port other than the one the second one is told to use).
            await ReadLineMatchingAsync(first, new Regex(@"^pusula: port 5190 is in use; listening on "), TestContext.Current.CancellationToken);
            for (int wait = 0; wait < 100 && !File.Exists(addressFile); wait++)
            {
                await Task.Delay(100, TestContext.Current.CancellationToken);
            }

            File.Exists(addressFile).ShouldBeTrue();

            int taken = ((IPEndPoint)squatter.LocalEndpoint).Port;
            using Process second = Start(root, home, emptyPath, "--urls", $"http://127.0.0.1:{taken}", "--Pusula:OpenBrowser", "true");
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(30));
            await second.WaitForExitAsync(timeout.Token);

            second.ExitCode.ShouldBe(1);
            (await second.StandardError.ReadToEndAsync(TestContext.Current.CancellationToken)).ShouldContain("Another program");
            (await second.StandardOutput.ReadToEndAsync(TestContext.Current.CancellationToken)).ShouldNotContain("already running");
        }
        finally
        {
            first.Kill(entireProcessTree: true);
            await first.WaitForExitAsync(TestContext.Current.CancellationToken);
            squatter.Stop();
            held?.Stop();
        }
    }

    private static TcpListener HoldFreePort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        return listener;
    }

    [Theory]
    [InlineData("ASPNETCORE_URLS")]
    [InlineData("DOTNET_URLS")]
    [InlineData("URLS")]
    public async Task Pusula_UrlsFromTheEnvironment_AreNeverChangedAndATakenPortStopsIt(string variable)
    {
        Assert.SkipUnless(OperatingSystem.IsLinux(), "Needs Linux and an xdg-open that is not there.");
        using var root = new TempDirectory();
        using var home = new TempDirectory();
        using var emptyPath = new TempDirectory();
        root.Write("A.md", "# A\n");
        TcpListener held = HoldFreePort();
        try
        {
            int port = ((IPEndPoint)held.LocalEndpoint).Port;
            using Process pusula = StartWith(root, home, emptyPath, new Dictionary<string, string> { [variable] = $"http://127.0.0.1:{port}" }, "--no-browser");
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(30));
            Task<string> output = pusula.StandardOutput.ReadToEndAsync(timeout.Token);
            Task<string> error = pusula.StandardError.ReadToEndAsync(timeout.Token);
            try
            {
                await pusula.WaitForExitAsync(timeout.Token);
            }
            catch (OperationCanceledException)
            {
                pusula.Kill(entireProcessTree: true);
                throw new InvalidOperationException("pusula did not stop: " + await output + await error);
            }

            pusula.ExitCode.ShouldBe(1);
            (await error).ShouldContain("Another program");
            (await output).ShouldNotContain("is in use; listening on");
        }
        finally
        {
            held.Stop();
        }
    }

    [Fact]
    [UnsupportedOSPlatform("windows")]
    public async Task SecondPusula_RecordedAddressOfASquatter_IsNotOpenedAndItStartsItself()
    {
        Assert.SkipUnless(OperatingSystem.IsLinux(), "Needs Linux and an xdg-open that is not there.");
        using var root = new TempDirectory();
        using var home = new TempDirectory();
        using var emptyPath = new TempDirectory();
        root.Write("A.md", "# A\n");
        string state = home.Resolve("state");
        string addressFile = Path.Join(state, "pusula", "instance-address");

        // A key exists, a program that is not a pusula listens on the recorded address and answers nothing that proves it.
        InstanceKey.EnsureExists(state, [], Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance).ShouldBeTrue();
        TcpListener squatter = HoldFreePort();
        using var accepting = new CancellationTokenSource();
        Task acceptor = Task.Run(async () =>
        {
            try
            {
                while (true)
                {
                    using TcpClient client = await squatter.AcceptTcpClientAsync(accepting.Token);
                }
            }
            catch (Exception exception) when (exception is OperationCanceledException or ObjectDisposedException or SocketException)
            {
            }
        }, TestContext.Current.CancellationToken);
        string squatterAddress = $"http://127.0.0.1:{((IPEndPoint)squatter.LocalEndpoint).Port}/";
        File.WriteAllText(addressFile, squatterAddress + "\n");
        File.SetUnixFileMode(addressFile, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        InstanceAddress.Read(state).ShouldBe(squatterAddress);

        TcpListener? held = HoldDefaultPort();
        using Process pusula = Start(root, home, emptyPath, "--Pusula:OpenBrowser", "true");
        try
        {
            // It does not take the squatter for a pusula: it keeps running and replaces the record with its own address.
            string? recorded = null;
            for (int wait = 0; wait < 200 && recorded is null; wait++)
            {
                await Task.Delay(100, TestContext.Current.CancellationToken);
                pusula.HasExited.ShouldBeFalse();
                string? now = InstanceAddress.Read(state);
                recorded = now == squatterAddress ? null : now;
            }

            recorded.ShouldNotBeNull();
            recorded.ShouldNotBe(squatterAddress);
        }
        finally
        {
            pusula.Kill(entireProcessTree: true);
            await pusula.WaitForExitAsync(TestContext.Current.CancellationToken);
            held?.Stop();
            await accepting.CancelAsync();
            squatter.Stop();
            await acceptor;
        }
    }

    [Fact]
    public async Task Pusula_StoppedGracefully_TakesItsRecordedAddressAway()
    {
        Assert.SkipUnless(OperatingSystem.IsLinux(), "Needs Linux and an xdg-open that is not there.");
        using var root = new TempDirectory();
        using var home = new TempDirectory();
        using var emptyPath = new TempDirectory();
        root.Write("A.md", "# A\n");
        string addressFile = home.Resolve("state/pusula/instance-address");
        TcpListener? held = HoldDefaultPort();
        using Process pusula = Start(root, home, emptyPath, "--Pusula:OpenBrowser", "true");
        try
        {
            for (int wait = 0; wait < 100 && !File.Exists(addressFile); wait++)
            {
                await Task.Delay(100, TestContext.Current.CancellationToken);
            }

            File.Exists(addressFile).ShouldBeTrue();

            using Process signal = Process.Start(new ProcessStartInfo("kill", $"-TERM {pusula.Id}") { UseShellExecute = false })!;
            await signal.WaitForExitAsync(TestContext.Current.CancellationToken);
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(30));
            await pusula.WaitForExitAsync(timeout.Token);

            File.Exists(addressFile).ShouldBeFalse();
        }
        finally
        {
            if (!pusula.HasExited)
            {
                pusula.Kill(entireProcessTree: true);
                await pusula.WaitForExitAsync(TestContext.Current.CancellationToken);
            }

            held?.Stop();
        }
    }
}
