using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Pusula.IntegrationTests.Support;

/// <summary>
/// A logger factory that logs nothing and, while it is <see cref="Armed"/>, fails when it is asked for a logger: a way to
/// make something unexpected go wrong at the one place that needs a logger (starting a source), without touching a folder.
/// </summary>
internal sealed class FailingLoggerFactory : ILoggerFactory
{
    private volatile bool _armed;

    public bool Armed
    {
        get => _armed;
        set => _armed = value;
    }

    public void AddProvider(ILoggerProvider provider)
    {
    }

    public ILogger CreateLogger(string categoryName) =>
        _armed ? throw new InvalidOperationException("The logger factory is out of order.") : NullLogger.Instance;

    public void Dispose()
    {
    }
}
