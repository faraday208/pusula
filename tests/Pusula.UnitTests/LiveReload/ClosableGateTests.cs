using Pusula.LiveReload;
using Shouldly;
using Xunit;

namespace Pusula.UnitTests.LiveReload;

public sealed class ClosableGateTests
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);

    // Long enough for something that should not have happened to have happened.
    private static readonly TimeSpan Short = TimeSpan.FromMilliseconds(200);

    private static async Task<bool> EnterAsync(ClosableGate gate) =>
        await gate.TryEnterAsync(TestContext.Current.CancellationToken);

    [Fact]
    public async Task TryEnterAsync_OpenGate_LetsOneCallerInAndTheNextOneWaitsForExit()
    {
        var gate = new ClosableGate();
        (await EnterAsync(gate)).ShouldBeTrue();

        Task<bool> second = gate.TryEnterAsync(TestContext.Current.CancellationToken).AsTask();
        await Task.Delay(Short, TestContext.Current.CancellationToken);
        second.IsCompleted.ShouldBeFalse();

        gate.Exit();
        (await second.WaitAsync(Timeout, TestContext.Current.CancellationToken)).ShouldBeTrue();
        gate.Exit();
    }

    [Fact]
    public async Task TryEnterAsync_ManyCallersAtOnce_NeverHaveTwoInside()
    {
        var gate = new ClosableGate();
        int inside = 0;
        int overlaps = 0;

        await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => Task.Run(
            async () =>
            {
                for (int i = 0; i < 100; i++)
                {
                    (await EnterAsync(gate)).ShouldBeTrue();
                    if (Interlocked.Increment(ref inside) > 1)
                    {
                        Interlocked.Increment(ref overlaps);
                    }

                    await Task.Yield();
                    Interlocked.Decrement(ref inside);
                    gate.Exit();
                }
            },
            TestContext.Current.CancellationToken)));

        overlaps.ShouldBe(0);
    }

    [Fact]
    public async Task TryEnterAsync_CancelledWhileWaiting_ThrowsAndLeavesTheGateToItsOwner()
    {
        var gate = new ClosableGate();
        (await EnterAsync(gate)).ShouldBeTrue();
        using var cancellation = new CancellationTokenSource();
        Task<bool> waiting = gate.TryEnterAsync(cancellation.Token).AsTask();

        await cancellation.CancelAsync();
        await Should.ThrowAsync<OperationCanceledException>(() => waiting);

        // The caller that gave up is not counted any more: the cleanup waits for the one inside only.
        int cleanups = 0;
        gate.Close(() => cleanups++, Short).ShouldBeFalse();
        cleanups.ShouldBe(0);
        gate.Exit();
        cleanups.ShouldBe(1);
    }

    [Fact]
    public async Task Close_NobodyInside_RunsTheCleanupAtOnceAndRefusesEveryCallerAfterwards()
    {
        var gate = new ClosableGate();
        int cleanups = 0;

        gate.Close(() => cleanups++, Timeout).ShouldBeTrue();

        cleanups.ShouldBe(1);
        (await EnterAsync(gate)).ShouldBeFalse();
    }

    [Fact]
    public async Task Close_CallerInside_RunsTheCleanupWhenHeLeavesAndNotBefore()
    {
        var gate = new ClosableGate();
        (await EnterAsync(gate)).ShouldBeTrue();
        int cleanups = 0;

        // Closing does not wait longer than it is told to, nor does it interrupt the one inside.
        gate.Close(() => cleanups++, Short).ShouldBeFalse();
        cleanups.ShouldBe(0);

        gate.Exit();

        cleanups.ShouldBe(1);
        gate.Close(() => cleanups++, Timeout).ShouldBeTrue();
        cleanups.ShouldBe(1);
    }

    // A caller that is turned away is out as soon as it is: the cleanup does not wait for it to find that out.
    [Fact]
    public async Task Close_CallerInsideAndCallerWaiting_TurnsTheSecondAwayAndRunsTheCleanupAfterTheFirstLeft()
    {
        var gate = new ClosableGate();
        var events = new List<string>();
        (await EnterAsync(gate)).ShouldBeTrue();
        Task second = Task.Run(
            async () =>
            {
                bool entered = await gate.TryEnterAsync(CancellationToken.None);
                lock (events)
                {
                    events.Add(entered ? "second entered" : "second refused");
                }
            },
            TestContext.Current.CancellationToken);
        await Task.Delay(Short, TestContext.Current.CancellationToken);

        gate.Close(
            () =>
            {
                lock (events)
                {
                    events.Add("cleanup");
                }
            },
            Short).ShouldBeFalse();
        lock (events)
        {
            events.Add("first leaves");
        }

        gate.Exit();
        await second.WaitAsync(Timeout, TestContext.Current.CancellationToken);
        gate.Close(static () => { }, Timeout).ShouldBeTrue();

        events.ShouldContain("second refused");
        events.ShouldNotContain("second entered");
        events.IndexOf("cleanup").ShouldBeGreaterThan(events.IndexOf("first leaves"));
    }

    [Fact]
    public void Close_CalledAgain_RunsTheFirstCleanupOnly()
    {
        var gate = new ClosableGate();
        var cleanups = new List<string>();

        gate.Close(() => cleanups.Add("first"), Timeout).ShouldBeTrue();
        gate.Close(() => cleanups.Add("second"), Timeout).ShouldBeTrue();

        cleanups.ShouldBe(["first"]);
    }

    [Fact]
    public async Task Dispose_ClosesTheGateWithNothingToCleanUp()
    {
        var gate = new ClosableGate();

        gate.Dispose();
        gate.Dispose();

        (await EnterAsync(gate)).ShouldBeFalse();
    }

    // Callers come and go while the gate is closed by several threads. Nothing may throw, the cleanup runs exactly once,
    // and never while a caller is inside (it takes down what the callers use).
    [Fact]
    public async Task Close_WhileCallersComeAndGo_RunsTheCleanupOnceAndNeverUnderACaller()
    {
        for (int round = 0; round < 100; round++)
        {
            var gate = new ClosableGate();
            int inside = 0;
            int cleanups = 0;
            int cleanedUpWithSomeoneInside = 0;
            int turnedAway = 0;

            Task[] callers =
            [
                .. Enumerable.Range(0, 6).Select(_ => Task.Run(
                    async () =>
                    {
                        while (await gate.TryEnterAsync(CancellationToken.None))
                        {
                            Interlocked.Increment(ref inside);
                            await Task.Yield();
                            Interlocked.Decrement(ref inside);
                            gate.Exit();
                        }

                        Interlocked.Increment(ref turnedAway);
                    },
                    TestContext.Current.CancellationToken)),
            ];

            await Task.Delay(round % 5, TestContext.Current.CancellationToken);
            Task[] closers =
            [
                .. Enumerable.Range(0, 3).Select(_ => Task.Run(
                    () => gate.Close(
                        () =>
                        {
                            Interlocked.Increment(ref cleanups);
                            if (Volatile.Read(ref inside) != 0)
                            {
                                Interlocked.Increment(ref cleanedUpWithSomeoneInside);
                            }
                        },
                        Timeout),
                    TestContext.Current.CancellationToken)),
            ];

            await Task.WhenAll([.. callers, .. closers]).WaitAsync(Timeout, TestContext.Current.CancellationToken);

            cleanups.ShouldBe(1);
            cleanedUpWithSomeoneInside.ShouldBe(0);
            turnedAway.ShouldBe(callers.Length);
        }
    }
}
