using Microsoft.Extensions.Logging;

namespace Pusula.IntegrationTests.Support;

/// <summary>
/// A logger that passes everything on to another one and, while its <see cref="Latch"/> is armed, holds back whoever
/// logs a message that starts with a given text, after the message was passed on. Given to the index builder, it stops a
/// rebuild of the index in the middle of the scan, when the index host has the gate (the builder logs that it indexed the
/// folder before it hands the index back).
/// </summary>
internal sealed class BlockingLogger<T>(ILogger<T> inner, Latch latch, string messageStart) : ILogger<T>
{
    public IDisposable? BeginScope<TState>(TState state)
        where TState : notnull => inner.BeginScope(state);

    public bool IsEnabled(LogLevel logLevel) => inner.IsEnabled(logLevel);

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
    {
        inner.Log(logLevel, eventId, state, exception, formatter);
        if (formatter(state, exception).StartsWith(messageStart, StringComparison.Ordinal))
        {
            latch.Park();
        }
    }
}
