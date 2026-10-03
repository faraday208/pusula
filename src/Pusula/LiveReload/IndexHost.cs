using System.Runtime.CompilerServices;
using Pusula.Indexing;

namespace Pusula.LiveReload;

/// <summary>
/// Owns the current <see cref="ConfigIndex"/> of one source. It builds the index when it is started, watches the
/// folder, rebuilds the index shortly after something relevant changed, and announces every new version to the
/// subscribers of <see cref="WatchAsync"/>. Every source has a host of its own, so that one that breaks leaves the
/// others alone.
/// </summary>
/// <remarks>
/// Announcements form a linked list of tasks: every <see cref="IndexChange"/> carries the task of the next one, so a
/// subscriber that follows the chain from the point where it subscribed cannot miss a change, however slowly it
/// reads, and the host keeps no per-subscriber state.
/// </remarks>
internal sealed partial class IndexHost : IIndexProvider, IDisposable
{
    // How long the folder has to stay quiet before the index is rebuilt.
    private static readonly TimeSpan DebounceDelay = TimeSpan.FromMilliseconds(250);

    private readonly IndexBuilder _builder;
    private readonly string _root;
    private readonly SourceProfile _profile;
    private readonly ILogger<IndexHost> _logger;
    private readonly StringComparison _comparison = PathComparison.Current;

    // One rebuild at a time: timer callbacks may overlap when a rebuild takes longer than the debounce delay.
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly CancellationTokenSource _stopping = new();

    private ITimer? _timer;
    private FileSystemWatcher? _watcher;
    private bool _watcherNeedsReset;
    private ConfigIndex? _current;
    private Node? _tail;
    private int _disposed;

    /// <summary>Creates the host; nothing is read until <see cref="StartAsync"/>.</summary>
    /// <param name="builder">Builds the index.</param>
    /// <param name="root">Full path of the folder.</param>
    /// <param name="profile">What kind of folder it is.</param>
    /// <param name="logger">The log.</param>
    public IndexHost(IndexBuilder builder, string root, SourceProfile profile, ILogger<IndexHost> logger)
    {
        _builder = builder;
        _root = root;
        _profile = profile;
        _logger = logger;
    }

    /// <summary>How long to wait before trying to start the file watcher again after it failed to start.</summary>
    internal TimeSpan WatcherRetryDelay { get; init; } = TimeSpan.FromSeconds(5);

    /// <inheritdoc />
    /// <exception cref="InvalidOperationException">The host has not been started.</exception>
    public ConfigIndex Current =>
        Volatile.Read(ref _current) ?? throw new InvalidOperationException("The index has not been built: the index host is not started.");

    /// <summary>
    /// Whether a change at <paramref name="relativePath"/> can alter the index. Filters the file system events.
    /// Claude profile: hidden names, the runtime directories that are never scanned and the transcripts under
    /// <c>projects/</c> are ignored; Markdown files, directories (names without an extension), the root
    /// <c>settings.json</c> and the memory directories are not. Other profiles: every path counts (an attachment
    /// that appears or goes changes what a link points at), except hidden ones and those below <c>node_modules</c>.
    /// </summary>
    /// <param name="relativePath">Path relative to the root, separated by <c>/</c>.</param>
    /// <param name="comparison">How directory and file names are compared.</param>
    /// <param name="profile">What kind of folder the path is in.</param>
    internal static bool IsRelevant(string relativePath, StringComparison comparison, SourceProfile profile = SourceProfile.Claude)
    {
        string[] parts = relativePath.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0 || Array.Exists(parts, part => part[0] == '.'))
        {
            return false;
        }

        if (profile != SourceProfile.Claude)
        {
            return !Array.Exists(parts, part => string.Equals(part, IndexBuilder.NodeModulesDirectory, comparison));
        }

        if (IndexBuilder.IsSkippedTopLevelDirectory(parts[0], comparison))
        {
            return false;
        }

        if (string.Equals(parts[0], "projects", comparison))
        {
            // projects itself (it may be created after the application started, with projects below it that no event
            // was seen for), projects/<project> (a project appeared, disappeared or moved), or anything inside its
            // memory directory.
            return parts.Length <= 2 || string.Equals(parts[2], "memory", comparison);
        }

        if (parts.Length == 1 && string.Equals(parts[0], "settings.json", comparison))
        {
            return true;
        }

        // Markdown files, and names without an extension, which may be directories.
        string extension = Path.GetExtension(parts[^1]);
        return extension.Length == 0 || string.Equals(extension, ".md", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Builds the first index (version 1) and starts watching the folder. A root that does not exist gives a
    /// <see cref="DirectoryNotFoundException"/> that names the folder.
    /// </summary>
    /// <param name="cancellationToken">Not used: the scan is quick and cannot be interrupted.</param>
    public Task StartAsync(CancellationToken cancellationToken)
    {
        ConfigIndex first = _builder.Build(_root, _profile) with { Version = 1 };
        Volatile.Write(ref _current, first);
        Volatile.Write(ref _tail, new Node(change: null));

        _timer = TimeProvider.System.CreateTimer(OnTimerElapsed, state: null, Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
        StartWatcher();

        // Whatever changed between the scan and the moment the watcher went live is picked up by one more rebuild,
        // which announces nothing when there is nothing new.
        Schedule(DebounceDelay);
        return Task.CompletedTask;
    }

    /// <summary>Stops watching and ends every <see cref="WatchAsync"/> sequence.</summary>
    /// <param name="cancellationToken">Not used.</param>
    public Task StopAsync(CancellationToken cancellationToken)
    {
        Stop();
        return Task.CompletedTask;
    }

    /// <summary>
    /// Subscribes to the changes of the index. The subscription starts when this method is called: every change
    /// published after that is yielded, in order, even if it is published before the first element is requested.
    /// The sequence ends without an exception when <paramref name="cancellationToken"/> is cancelled or the host stops
    /// (and so does one that begins after the host was disposed).
    /// </summary>
    /// <param name="cancellationToken">Ends the sequence.</param>
    /// <exception cref="InvalidOperationException">The host has not been started.</exception>
    public IAsyncEnumerable<IndexChange> WatchAsync(CancellationToken cancellationToken)
    {
        Node start = Volatile.Read(ref _tail) ?? throw new InvalidOperationException("The index has not been built: the index host is not started.");
        return Follow(start, cancellationToken);
    }

    /// <summary>
    /// Rebuilds the index now. A rebuild that finds no difference keeps the version and announces nothing;
    /// otherwise the version is incremented and the change is published. Rebuilds run one at a time.
    /// </summary>
    /// <param name="cancellationToken">Cancels the wait for a running rebuild.</param>
    internal async Task RefreshAsync(CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            RestartWatcherIfNeeded();

            ConfigIndex previous = Current;
            ConfigIndex fresh = _builder.Build(_root, _profile);
            IndexDiff diff = IndexDiff.Compute(previous, fresh);
            if (diff.IsEmpty)
            {
                // Nothing a subscriber could see changed, but the snapshot is still served: a link may point at
                // something that appeared on disk without being indexed (a directory, a script).
                Volatile.Write(ref _current, fresh with { Version = previous.Version });
                return;
            }

            long version = previous.Version + 1;
            Publish(fresh with { Version = version }, new IndexChange(version, diff.Added, diff.Removed, diff.Changed));
            LogPublished(version, diff.Added.Count, diff.Removed.Count, diff.Changed.Count, _root);
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 1)
        {
            return;
        }

        Stop();
        _stopping.Dispose();
        _gate.Dispose();
    }

    // Walks the chain from 'node' on, waiting for each next link.
    private async IAsyncEnumerable<IndexChange> Follow(Node node, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        CancellationTokenSource linked;
        try
        {
            linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _stopping.Token);
        }
        catch (ObjectDisposedException)
        {
            // The host was disposed (its source was removed) before this sequence began: there is nothing to follow.
            yield break;
        }

        using (linked)
        {
            while (true)
            {
                try
                {
                    node = await node.Next.Task.WaitAsync(linked.Token).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    yield break;
                }

                yield return node.Change!;
            }
        }
    }

    private void Publish(ConfigIndex index, IndexChange change)
    {
        var next = new Node(change);
        Node previous = Volatile.Read(ref _tail)!;

        // The snapshot and the new end of the chain are in place before the subscribers are woken up.
        Volatile.Write(ref _current, index);
        Volatile.Write(ref _tail, next);
        previous.Next.SetResult(next);
    }

    private void Stop()
    {
        _stopping.Cancel();
        _timer?.Dispose();
        _watcher?.Dispose();
    }

    private void StartWatcher()
    {
        FileSystemWatcher? watcher = null;
        try
        {
            watcher = new FileSystemWatcher(_root)
            {
                IncludeSubdirectories = true,
                NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName | NotifyFilters.LastWrite | NotifyFilters.Size,
                InternalBufferSize = 64 * 1024,
            };
            watcher.Created += OnFileSystemEvent;
            watcher.Changed += OnFileSystemEvent;
            watcher.Deleted += OnFileSystemEvent;
            watcher.Renamed += OnRenamed;
            watcher.Error += OnWatcherError;
            watcher.EnableRaisingEvents = true;

            _watcher = watcher;
            LogWatching(_root);
        }
        catch (Exception exception) when (exception is IOException or ArgumentException or UnauthorizedAccessException or PlatformNotSupportedException)
        {
            // For example the limit on inotify watches. The pages keep working; they just stop updating by themselves.
            watcher?.Dispose();
            LogWatcherFailed(exception.Message);
            Schedule(WatcherRetryDelay);
        }
    }

    private void RestartWatcherIfNeeded()
    {
        if (_watcher is not null && !Volatile.Read(ref _watcherNeedsReset))
        {
            return;
        }

        // Events were lost (or the watcher never started): start over. The rebuild that follows sees everything.
        _watcher?.Dispose();
        _watcher = null;
        Volatile.Write(ref _watcherNeedsReset, false);
        StartWatcher();
    }

    private void OnFileSystemEvent(object sender, FileSystemEventArgs e)
    {
        if (IsRelevantFullPath(e.FullPath))
        {
            Schedule(DebounceDelay);
        }
    }

    private void OnRenamed(object sender, RenamedEventArgs e)
    {
        if (IsRelevantFullPath(e.FullPath) || IsRelevantFullPath(e.OldFullPath))
        {
            Schedule(DebounceDelay);
        }
    }

    // The watcher lost events (or broke): restart it and rebuild, which sees everything that was missed.
    // Internal so that tests can simulate a broken watcher.
    internal void OnWatcherError(object? sender, ErrorEventArgs e)
    {
        LogWatcherError(e.GetException().Message);
        Volatile.Write(ref _watcherNeedsReset, true);
        Schedule(DebounceDelay);
    }

    private bool IsRelevantFullPath(string fullPath) =>
        IsRelevant(Path.GetRelativePath(_root, fullPath).Replace(Path.DirectorySeparatorChar, '/'), _comparison, _profile);

    // Every call postpones the rebuild: it runs once the folder has been quiet for the whole delay.
    private void Schedule(TimeSpan delay) => _timer?.Change(delay, Timeout.InfiniteTimeSpan);

    private void OnTimerElapsed(object? state) => _ = RefreshSafelyAsync();

    // The timer callback: a rebuild that never throws. Internal so that tests can run it directly.
    internal async Task RefreshSafelyAsync()
    {
        try
        {
            await RefreshAsync(_stopping.Token).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is OperationCanceledException or ObjectDisposedException && _stopping.IsCancellationRequested)
        {
            // The application is shutting down.
        }
        catch (Exception exception)
        {
            // The previous index stays in place; the next change tries again. Nothing may escape a timer callback.
            LogRefreshFailed(exception.Message, _root);
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Watching {Root} for changes")]
    private partial void LogWatching(string root);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Cannot watch the folder, pages will not update by themselves (retrying): {Reason}")]
    private partial void LogWatcherFailed(string reason);

    [LoggerMessage(Level = LogLevel.Warning, Message = "The file watcher reported an error and is restarted: {Reason}")]
    private partial void LogWatcherError(string reason);

    [LoggerMessage(Level = LogLevel.Information, Message = "Index is now version {Version}: {Added} added, {Removed} removed, {Changed} changed ({Root})")]
    private partial void LogPublished(long version, int added, int removed, int changed, string root);

    [LoggerMessage(Level = LogLevel.Error, Message = "Rebuilding the index of {Root} failed, the previous index stays: {Reason}")]
    private partial void LogRefreshFailed(string reason, string root);

    // One link of the announcement chain: a change and the task of the next link.
    private sealed class Node(IndexChange? change)
    {
        public IndexChange? Change { get; } = change;

        public TaskCompletionSource<Node> Next { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    }
}
