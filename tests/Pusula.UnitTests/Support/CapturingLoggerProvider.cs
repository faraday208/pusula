using Microsoft.Extensions.Logging;

namespace Pusula.UnitTests.Support;

/// <summary>A logger provider that records every entry of every logger it makes, with the category of the logger: for what the filters of the host let through.</summary>
internal sealed class CapturingLoggerProvider : ILoggerProvider
{
    private readonly List<(string Category, LogLevel Level, string Message)> _entries = [];

    public IReadOnlyList<(string Category, LogLevel Level, string Message)> Entries => _entries;

    public ILogger CreateLogger(string categoryName) => new CategoryLogger(categoryName, _entries);

    public void Dispose()
    {
    }

    private sealed class CategoryLogger(string category, List<(string Category, LogLevel Level, string Message)> entries) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => logLevel != LogLevel.None;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
            entries.Add((category, logLevel, formatter(state, exception)));
    }
}
