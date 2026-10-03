using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Pusula.IntegrationTests.Support;

/// <summary>
/// A logger factory that logs nothing and, while its <see cref="Latch"/> is armed, holds back whoever asks it for a
/// logger: a way to stop the registry in the middle of a change of the list. It has the gate then, and has not made the
/// host of the source it is starting yet (a new host gets its logger from here).
/// </summary>
internal sealed class BlockingLoggerFactory(Latch latch) : ILoggerFactory
{
    public void AddProvider(ILoggerProvider provider)
    {
    }

    public ILogger CreateLogger(string categoryName)
    {
        latch.Park();
        return NullLogger.Instance;
    }

    public void Dispose()
    {
    }
}
