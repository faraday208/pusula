using Pusula.Indexing;

namespace Pusula.Sources;

/// <summary>
/// Watches one sources file: when it is written, replaced, created or deleted, the <c>changed</c> callback is called
/// once the folder has been quiet for <see cref="Delay"/>. The folder is watched and not the file itself, so that an
/// editor that saves by replacing the file is seen, and so is a file that does not exist yet. A folder that does not
/// exist cannot be watched; <see cref="TryStart"/> tries again. <see cref="TryStart"/> and <see cref="Dispose"/> can be
/// called at the same time and any number of times: a start that comes after the dispose, or that the dispose did not
/// wait for, does not leave a watcher running.
/// </summary>
internal sealed partial class SourcesFileWatcher : IDisposable
{
    /// <summary>How long the folder has to stay quiet before a change is reported.</summary>
    internal static readonly TimeSpan Delay = TimeSpan.FromMilliseconds(300);

    private readonly string _file;
    private readonly string _directory;
    private readonly string _name;
    private readonly Action _changed;
    private readonly ILogger<SourcesFileWatcher> _logger;
    private readonly ITimer _timer;

    // Makes starting and ending the watcher one thing at a time.
    private readonly Lock _sync = new();

    private FileSystemWatcher? _watcher;
    private bool _needsRestart;
    private bool _disposed;

    /// <summary>Creates the watcher; nothing is watched until <see cref="TryStart"/>.</summary>
    /// <param name="file">Full path of the sources file.</param>
    /// <param name="changed">Called, on a thread of the pool, when the file may have changed. Must not throw.</param>
    /// <param name="logger">The log.</param>
    public SourcesFileWatcher(string file, Action changed, ILogger<SourcesFileWatcher> logger)
    {
        _file = file;
        _directory = Path.GetDirectoryName(file) ?? throw new ArgumentException("The sources file is not a file in a folder.", nameof(file));
        _name = Path.GetFileName(file);
        _changed = changed;
        _logger = logger;
        _timer = TimeProvider.System.CreateTimer(static state => ((SourcesFileWatcher)state!).OnTimerElapsed(), this, Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
    }

    /// <summary>
    /// Starts watching the folder of the file, unless it is watched already; starts again when the watcher reported an
    /// error. A start reports <c>changed</c> once, so that whatever happened before the watcher went live is seen.
    /// </summary>
    /// <returns>True when the folder is watched; false when it does not exist yet or cannot be watched.</returns>
    public bool TryStart()
    {
        lock (_sync)
        {
            return !_disposed && StartWatcher();
        }
    }

    // Called with _sync.
    private bool StartWatcher()
    {
        if (_watcher is not null && !Volatile.Read(ref _needsRestart))
        {
            return true;
        }

        _watcher?.Dispose();
        _watcher = null;
        Volatile.Write(ref _needsRestart, false);

        if (!Directory.Exists(_directory))
        {
            LogFolderMissing(_directory);
            return false;
        }

        FileSystemWatcher? watcher = null;
        try
        {
            watcher = new FileSystemWatcher(_directory)
            {
                IncludeSubdirectories = false,
                NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.Size,
            };
            watcher.Created += OnChanged;
            watcher.Changed += OnChanged;
            watcher.Deleted += OnChanged;
            watcher.Renamed += OnRenamed;
            watcher.Error += OnError;
            watcher.EnableRaisingEvents = true;
        }
        catch (Exception exception) when (exception is IOException or ArgumentException or UnauthorizedAccessException or PlatformNotSupportedException)
        {
            // For example the limit on inotify watches: the file still works, it is just not noticed when it changes.
            watcher?.Dispose();
            LogFailed(exception.Message);
            return false;
        }

        _watcher = watcher;
        LogWatching(_file);
        Schedule();
        return true;
    }

    /// <inheritdoc />
    public void Dispose()
    {
        lock (_sync)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _timer.Dispose();
            _watcher?.Dispose();
            _watcher = null;
        }
    }

    private void OnChanged(object sender, FileSystemEventArgs e)
    {
        if (IsTheFile(e.Name))
        {
            Schedule();
        }
    }

    private void OnRenamed(object sender, RenamedEventArgs e)
    {
        if (IsTheFile(e.Name) || IsTheFile(e.OldName))
        {
            Schedule();
        }
    }

    // Events were lost or the watcher broke: it is started again by the next TryStart, which also reports a change.
    // Internal so that tests can simulate a broken watcher.
    internal void OnError(object? sender, ErrorEventArgs e)
    {
        LogError(e.GetException().Message);
        Volatile.Write(ref _needsRestart, true);
        Schedule();
    }

    private bool IsTheFile(string? name) => string.Equals(name, _name, PathComparison.Current);

    // Every call postpones the report: it comes once the folder has been quiet for the whole delay.
    private void Schedule() => _timer.Change(Delay, Timeout.InfiniteTimeSpan);

    private void OnTimerElapsed()
    {
        if (!Volatile.Read(ref _disposed))
        {
            _changed();
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Watching the sources file {File} for changes")]
    private partial void LogWatching(string file);

    [LoggerMessage(Level = LogLevel.Debug, Message = "The folder of the sources file does not exist yet, so it is not watched until the file is written: {Directory}")]
    private partial void LogFolderMissing(string directory);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Cannot watch the sources file, a change to it is not noticed until it is written by this program: {Reason}")]
    private partial void LogFailed(string reason);

    [LoggerMessage(Level = LogLevel.Warning, Message = "The watcher of the sources file reported an error and is started again: {Reason}")]
    private partial void LogError(string reason);
}
