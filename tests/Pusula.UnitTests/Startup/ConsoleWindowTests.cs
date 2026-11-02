using Pusula.Startup;
using Shouldly;
using Xunit;

namespace Pusula.UnitTests.Startup;

public sealed class ConsoleWindowTests
{
    [Theory]
    [InlineData(true, 1, false, true, true)]   // a double click on Windows: ours alone, and someone to type Enter
    [InlineData(true, 2, false, true, false)]  // started from a terminal: the shell is attached too
    [InlineData(true, 0, false, true, false)]  // the count could not be told
    [InlineData(true, 1, true, true, false)]   // input is redirected: nobody to press Enter
    [InlineData(false, 1, false, true, false)] // not Windows
    [InlineData(true, 1, false, false, false)] // a console nobody sees: hidden window, scheduled task, service wrapper
    public void ShouldPause_ByWhereItRuns(bool isWindows, int attached, bool inputRedirected, bool startedByExplorer, bool expected) =>
        ConsoleWindow.ShouldPause(isWindows, attached, inputRedirected, startedByExplorer).ShouldBe(expected);

    [Fact]
    public void ShouldPauseHere_InTests_IsFalse() => ConsoleWindow.ShouldPauseHere().ShouldBeFalse();

    [Fact]
    public void Fail_WritesTheMessageAndGivesExitCode1WithoutWaiting()
    {
        TextWriter original = Console.Error;
        using var captured = new StringWriter();
        Console.SetError(captured);
        try
        {
            ConsoleWindow.Fail("pusula: broken").ShouldBe(1);
        }
        finally
        {
            Console.SetError(original);
        }

        captured.ToString().ShouldBe($"pusula: broken{Environment.NewLine}");
    }
}
