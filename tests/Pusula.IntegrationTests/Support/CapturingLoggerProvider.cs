using Microsoft.Extensions.Logging;

namespace Pusula.IntegrationTests.Support;

/// <summary>A logger provider that records every entry of every logger it makes, with the category of the logger: for what the filters of the host let through.</summary>
internal sealed class CapturingLoggerProvider : ILoggerProvider
{
    private readonly Lock _gate = new();
    private readonly List<(string Category, LogLevel Level, string Message)> _entries = [];

    public IReadOnlyList<(string Category, LogLevel Level, string Message)> Entries
    {
        get
        {
            lock (_gate)
            {
                return [.. _entries];
            }
        }
    }

    public ILogger CreateLogger(string categoryName) => new CategoryLogger(categoryName, this);

    public void Dispose()
    {
    }

    private void Add(string category, LogLevel level, string message)
    {
        lock (_gate)
        {
            _entries.Add((category, level, message));
        }
    }

    private sealed class CategoryLogger(string category, CapturingLoggerProvider provider) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => logLevel != LogLevel.None;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
            provider.Add(category, logLevel, formatter(state, exception));
    }
}
