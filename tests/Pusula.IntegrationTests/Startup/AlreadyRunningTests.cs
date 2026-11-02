using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using Pusula.IntegrationTests.Support;
using Pusula.Startup;
using Shouldly;
using Xunit;

namespace Pusula.IntegrationTests.Startup;

/// <summary>
/// Two real pusula programs, started the way the download is by a double click: the first makes the instance key and the second proves
/// itself against it. Linux only: the browser is the <c>xdg-open</c> that is not on the <c>PATH</c> of the children, so no browser opens
/// on the machine of whoever runs the tests (and the state folder is a temporary one).
/// </summary>
public sealed class AlreadyRunningTests
{
    private static int FreePort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        int port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }

    private static Process Start(TempDirectory root, TempDirectory home, TempDirectory emptyPath, int port)
    {
        var start = new ProcessStartInfo(Environment.GetEnvironmentVariable("DOTNET_HOST_PATH") ?? "dotnet")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        start.ArgumentList.Add(typeof(Program).Assembly.Location);
        start.ArgumentList.Add(root.Path);
        start.ArgumentList.Add("--urls");
        start.ArgumentList.Add($"http://127.0.0.1:{port}");
        start.ArgumentList.Add("--Pusula:OpenBrowser");
        start.ArgumentList.Add("true");
        start.Environment["HOME"] = home.Path;
        start.Environment["XDG_STATE_HOME"] = home.Resolve("state");
        start.Environment["DISPLAY"] = ":0";
        start.Environment["PATH"] = emptyPath.Path;
        start.Environment["ASPNETCORE_ENVIRONMENT"] = "Production";
        return Process.Start(start)!;
    }

    [Fact]
    public async Task SecondPusula_FindsTheFirstOneByItsKey_AndASquatterIsNotOpened()
    {
        Assert.SkipUnless(OperatingSystem.IsLinux(), "Needs Linux and an xdg-open that is not there.");
        using var root = new TempDirectory();
        using var home = new TempDirectory();
        using var emptyPath = new TempDirectory();
        root.Write("A.md", "# A\n");
        string keyFile = home.Resolve("state/pusula/instance-key");
        int port = FreePort();
        using Process first = Start(root, home, emptyPath, port);
        try
        {
            // The first writes the key once it listens, before it opens the browser.
            for (int wait = 0; wait < 100 && !File.Exists(keyFile); wait++)
            {
                await Task.Delay(100, TestContext.Current.CancellationToken);
            }

            File.Exists(keyFile).ShouldBeTrue();
            InstanceKey.Read(home.Resolve("state")).ShouldNotBeNull();

            using Process second = Start(root, home, emptyPath, port);
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(30));
            await second.WaitForExitAsync(timeout.Token);

            second.ExitCode.ShouldBe(0);
            (await second.StandardOutput.ReadToEndAsync(TestContext.Current.CancellationToken)).ShouldContain($"already running at http://127.0.0.1:{port}/");
        }
        finally
        {
            first.Kill(entireProcessTree: true);
            await first.WaitForExitAsync(TestContext.Current.CancellationToken);
        }
    }

    [Fact]
    public async Task SecondPusula_WithoutAKeyOfItsOwnUser_StopsWithThePortInUseLine()
    {
        Assert.SkipUnless(OperatingSystem.IsLinux(), "Needs Linux and an xdg-open that is not there.");
        using var root = new TempDirectory();
        using var home = new TempDirectory();
        using var emptyPath = new TempDirectory();
        root.Write("A.md", "# A\n");
        var squatter = new TcpListener(IPAddress.Loopback, 0);
        squatter.Start();
        try
        {
            int port = ((IPEndPoint)squatter.LocalEndpoint).Port;
            using Process second = Start(root, home, emptyPath, port);
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(30));
            await second.WaitForExitAsync(timeout.Token);

            second.ExitCode.ShouldBe(1);
            (await second.StandardError.ReadToEndAsync(TestContext.Current.CancellationToken)).ShouldContain("Another program");
            Directory.Exists(home.Resolve("state")).ShouldBeFalse();
        }
        finally
        {
            squatter.Stop();
        }
    }
}
