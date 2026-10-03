using System.Diagnostics.CodeAnalysis;
using Microsoft.Extensions.Options;
using Pusula.Indexing;
using Pusula.LiveReload;
using Pusula.Startup;

namespace Pusula.Sources;

/// <summary>
/// Reads the list of sources, starts an <see cref="IndexHost"/> for each of them when the application starts and
/// stops them when it stops. Every source has a host of its own: a source that cannot be started (its folder does not
/// exist, cannot be read, or is too large to show) is listed as not available and the others work. When the list comes
/// from a sources file, that file is the truth and the registry
/// follows it: it watches the file, and a change to it, made by hand or by <see cref="AddAsync"/> and
/// <see cref="RemoveAsync"/>, brings the running sources in line with it without a restart (see
/// <see cref="ReconcileAsync"/>). A file that was changed into something that cannot be read leaves the last valid
/// list in place and says why in <see cref="SourcesFileError"/>.
/// </summary>
internal sealed partial class SourceRegistry : ISourceRegistry, ISourceEditor, IHostedService, IDisposable
{
    private readonly IndexBuilder _builder;
    private readonly IOptions<PusulaOptions> _options;
    private readonly UserDirectories _directories;
    private readonly ILoggerFactory _loggerFactory;
    private readonly ILogger<SourceRegistry> _logger;

    // One change of the list at a time: reading the sources file, changing it and bringing the sources in line with it.
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly List<IndexHost> _hosts = [];

    private SourceList? _list;
    private SourceEntry[] _entries = [];
    private string? _fileError;
    private SourcesFileWatcher? _watcher;
    private int _stopped;
    private int _disposed;

    /// <summary>Creates the registry; nothing is read until <see cref="Load"/> or <see cref="StartAsync"/>.</summary>
    public SourceRegistry(
        IndexBuilder builder,
        IOptions<PusulaOptions> options,
        UserDirectories directories,
        ILoggerFactory loggerFactory,
        ILogger<SourceRegistry> logger)
    {
        _builder = builder;
        _options = options;
        _directories = directories;
        _loggerFactory = loggerFactory;
        _logger = logger;
    }

    /// <inheritdoc />
    public string? SourcesFile => Volatile.Read(ref _list)?.SourcesFile;

    /// <inheritdoc />
    public string? SourcesFileError => Volatile.Read(ref _fileError);

    /// <inheritdoc />
    public IReadOnlyList<SourceEntry> Sources => Volatile.Read(ref _entries);

    /// <inheritdoc />
    public bool TryGet(string id, [NotNullWhen(true)] out SourceEntry? source)
    {
        source = Array.Find(Volatile.Read(ref _entries), entry => string.Equals(entry.Definition.Id, id, StringComparison.Ordinal));
        return source is not null;
    }

    /// <summary>
    /// Decides what to show (see <see cref="SourceList.TryResolve"/>); only the first call does, and the settings are
    /// read now and not earlier, so that those supplied late, such as those of a test host, are honored. Called
    /// before the server starts so that a sources file that cannot be used ends with one line and not a stack trace.
    /// </summary>
    /// <returns>The line to print when the sources file cannot be used; null otherwise.</returns>
    public string? Load()
    {
        if (Volatile.Read(ref _list) is not null)
        {
            return null;
        }

        if (!SourceList.TryResolve(_options.Value, _directories.Home, _directories.ApplicationData, out SourceList? list, out string? error))
        {
            return error;
        }

        Volatile.Write(ref _list, list);
        return null;
    }

    /// <summary>
    /// Starts every source, and watches the sources file when the list comes from one. A folder that was named on the
    /// command line or in <c>Pusula:Root</c> that cannot be read stops the application (a
    /// <see cref="DirectoryNotFoundException"/> that names the folder, for one that does not exist, or a
    /// <see cref="FolderTooLargeException"/>, for one that is too large to show); a folder of a sources file only makes
    /// its source not available.
    /// </summary>
    /// <param name="cancellationToken">Cancels the wait for another change of the list; the scan of a folder cannot be interrupted.</param>
    /// <exception cref="InvalidOperationException">The sources file cannot be used (and <see cref="Load"/> was not called before to say so).</exception>
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        string? error = Load();
        if (error is not null)
        {
            throw new InvalidOperationException(error);
        }

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await ReconcileAsync(_list!, cancellationToken).ConfigureAwait(false);
            StartWatching();
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>Stops watching the sources file and every folder, and ends every event stream.</summary>
    /// <param name="cancellationToken">Cancels the wait for a change of the list that is under way.</param>
    public async Task StopAsync(CancellationToken cancellationToken)
    {
        Volatile.Write(ref _stopped, 1);

        try
        {
            await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (ObjectDisposedException)
        {
            // Disposed already (an application whose start failed is disposed, and may be stopped after that): nothing is left to stop.
            return;
        }

        try
        {
            _watcher?.Dispose();
            foreach (IndexHost host in _hosts)
            {
                await host.StopAsync(cancellationToken).ConfigureAwait(false);
            }
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

        _watcher?.Dispose();
        foreach (IndexHost host in _hosts)
        {
            host.Dispose();
        }

        _gate.Dispose();
    }

    /// <inheritdoc />
    public async Task<SourceEditResult> AddAsync(NewSource source, CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_list?.SourcesFile is not { } file)
            {
                return SourceEditResult.Failed(EditError.CommandLine);
            }

            if (!TryReadFile(file, out FileState? state, out string? reason))
            {
                return SourceEditResult.Failed(EditError.FileInvalid, reason);
            }

            if (state.Definitions.Any(definition => SourcePaths.Same(definition.Path, source.FullPath)))
            {
                return SourceEditResult.Failed(EditError.AlreadyListed);
            }

            // The id is taken from the name; it must not be one of the ids the file gives out, written or derived.
            var taken = new HashSet<string>(state.Definitions.Select(definition => definition.Id), StringComparer.Ordinal);
            string id = SourceIds.MakeUnique(SourceIds.Derive(source.Name), taken);

            if (!SourcesFileEditor.TryAdd(state.Text, state.Definitions, _directories.Home, source, id, out string? json, out reason))
            {
                return SourceEditResult.Failed(EditError.FileInvalid, reason);
            }

            if (await CommitAsync(file, json).ConfigureAwait(false) is { } failure)
            {
                return failure;
            }

            if (!TryGet(id, out SourceEntry? added))
            {
                throw new InvalidOperationException($"The source '{id}' was written to the sources file but is not running.");
            }

            LogAdded(id, added.Definition.Path);
            return SourceEditResult.Added(added);
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <inheritdoc />
    public async Task<SourceEditResult> RemoveAsync(string id, CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_list?.SourcesFile is not { } file)
            {
                return SourceEditResult.Failed(EditError.CommandLine);
            }

            if (!TryReadFile(file, out FileState? state, out string? reason))
            {
                return SourceEditResult.Failed(EditError.FileInvalid, reason);
            }

            int index = IndexOf(state.Definitions, id);
            if (index < 0)
            {
                return SourceEditResult.Failed(EditError.NotFound);
            }

            SourceDefinition removed = state.Definitions[index];
            if (!SourcesFileEditor.TryRemove(state.Text, state.Definitions, _directories.Home, index, out string? json, out reason))
            {
                return SourceEditResult.Failed(EditError.FileInvalid, reason);
            }

            if (await CommitAsync(file, json).ConfigureAwait(false) is { } failure)
            {
                return failure;
            }

            LogRemoved(removed.Id, removed.Path);
            return SourceEditResult.Removed();
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>
    /// Reads the sources file again and brings the sources in line with it: the work of the file watcher after a
    /// change. A file that was deleted leaves the list as it is. A file that cannot be read does too, and
    /// <see cref="SourcesFileError"/> says why until it can be read again.
    /// </summary>
    internal async Task ReloadAsync()
    {
        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            if (Volatile.Read(ref _stopped) == 1 || _list?.SourcesFile is not { } file)
            {
                return;
            }

            // A watcher that reported an error is started again here; the read below sees what it missed.
            _watcher?.TryStart();

            if (TryReadFile(file, out FileState? state, out _) && state.Text is not null)
            {
                await ReconcileAsync(new SourceList(state.Definitions, file), CancellationToken.None).ConfigureAwait(false);
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    // The callback of the file watcher: a reload that never throws. Internal so that tests can run it directly.
    internal async Task ReloadSafelyAsync()
    {
        try
        {
            await ReloadAsync().ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is OperationCanceledException or ObjectDisposedException && Volatile.Read(ref _disposed) == 1)
        {
            // The application is shutting down.
        }
        catch (Exception exception)
        {
            // The sources stay as they are; the next change of the file tries again. Nothing may escape a timer callback.
            LogReloadFailed(exception, SourcesFile ?? string.Empty);
        }
    }

    private void OnFileChanged() => _ = ReloadSafelyAsync();

    // The gate is held. Starts watching the folder of the sources file: now, or after the first write when the folder
    // is created by it. Does nothing when the list does not come from a sources file.
    private void StartWatching()
    {
        if (_list?.SourcesFile is not { } file)
        {
            return;
        }

        _watcher ??= new SourcesFileWatcher(file, OnFileChanged, _loggerFactory.CreateLogger<SourcesFileWatcher>());
        _watcher.TryStart();
    }

    // The gate is held. The list as the sources file has it, with the text it was read from; as it is shown now, with no
    // text, when there is no file (yet, or any more). A file that cannot be used is remembered in SourcesFileError.
    private bool TryReadFile(string file, [NotNullWhen(true)] out FileState? state, [NotNullWhen(false)] out string? reason)
    {
        state = null;
        if (!File.Exists(file))
        {
            SetFileError(null);
            state = new FileState(Text: null, _list!.Sources);
            reason = null;
            return true;
        }

        if (!SourcesFileReader.TryReadText(file, out string? text, out reason)
            || !SourcesFileReader.TryParse(text, Path.GetDirectoryName(file) ?? string.Empty, _directories.Home, out IReadOnlyList<SourceDefinition>? definitions, out reason))
        {
            SetFileError(reason);
            return false;
        }

        if (SetFileError(null) is not null)
        {
            LogFileValid(file);
        }

        state = new FileState(text, definitions);
        return true;
    }

    // The previous error is returned; a new one is logged once.
    private string? SetFileError(string? reason)
    {
        string? previous = Interlocked.Exchange(ref _fileError, reason);
        if (reason is not null && !string.Equals(previous, reason, StringComparison.Ordinal))
        {
            LogFileInvalid(SourcesFile ?? string.Empty, reason);
        }

        return previous;
    }

    // The gate is held. Writes the new text of the sources file and brings the sources in line with it. Not
    // interrupted once it has begun: the file and the sources must not end up saying different things.
    private async Task<SourceEditResult?> CommitAsync(string file, string json)
    {
        try
        {
            SourcesFileWriter.Write(file, json);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            LogWriteFailed(exception, file);
            return SourceEditResult.Failed(EditError.WriteFailed);
        }

        // The sources are those of the text that was written, read the way the next start (and a reload) reads it.
        if (!SourcesFileReader.TryParse(json, Path.GetDirectoryName(file) ?? string.Empty, _directories.Home, out IReadOnlyList<SourceDefinition>? definitions, out string? reason))
        {
            throw new InvalidOperationException($"The sources file that was just written cannot be read: {reason}");
        }

        await ReconcileAsync(new SourceList(definitions, file), CancellationToken.None).ConfigureAwait(false);
        SetFileError(null);

        // The first write may have created the folder of the file, which could not be watched before.
        StartWatching();
        return null;
    }

    // The gate is held. Brings the running sources in line with the list. A source whose id, folder and profile are
    // what they were keeps its host (the very same object, so nothing is scanned again and its event streams go on);
    // a new one is started; one that is not listed any more is stopped, which ends its event streams. The order is the
    // order of the list, and the sources are replaced in one step.
    private async Task ReconcileAsync(SourceList list, CancellationToken cancellationToken)
    {
        SourceEntry[] previous = Volatile.Read(ref _entries);
        var entries = new List<SourceEntry>(list.Sources.Count);

        foreach (SourceDefinition definition in list.Sources)
        {
            SourceEntry? running = Array.Find(previous, entry => string.Equals(entry.Definition.Id, definition.Id, StringComparison.Ordinal));
            if (running is not null && IsSameFolder(running.Definition, definition) && !CanBeStartedNow(running))
            {
                // Only its name may be different.
                entries.Add(running.Definition == definition ? running : running with { Definition = definition });
                continue;
            }

            entries.Add(await StartSourceAsync(definition, cancellationToken).ConfigureAwait(false));
        }

        // The new list is in place before a host stops, so that no request is handed a source that is going away.
        Volatile.Write(ref _entries, [.. entries]);
        Volatile.Write(ref _list, list);

        var kept = new HashSet<IIndexProvider>(entries.Where(entry => entry.Index is not null).Select(entry => entry.Index!), ReferenceEqualityComparer.Instance);
        foreach (SourceEntry old in previous)
        {
            if (old.Index is IndexHost host && !kept.Contains(host))
            {
                _hosts.Remove(host);
                await host.StopAsync(cancellationToken).ConfigureAwait(false);
                host.Dispose();
                LogStopped(old.Definition.Id, old.Definition.Path);
            }
        }
    }

    private async Task<SourceEntry> StartSourceAsync(SourceDefinition definition, CancellationToken cancellationToken)
    {
        // A folder the user named is not looked at here: the host throws, and the application does not start.
        if (!definition.IsRequired && !Directory.Exists(definition.Path))
        {
            return NotAvailable(definition, "The folder does not exist or is not a directory.", SourceErrorCode.FolderMissing);
        }

        var host = new IndexHost(_builder, definition.Path, definition.Profile, _loggerFactory.CreateLogger<IndexHost>());
        _hosts.Add(host);
        try
        {
            await host.StartAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (FolderTooLargeException exception) when (!definition.IsRequired)
        {
            // Nothing is wrong with the server or the folder; the folder is just more than is shown. No stack trace.
            _hosts.Remove(host);
            host.Dispose();
            return NotAvailable(definition, exception.Message, SourceErrorCode.TooLarge);
        }
        catch (Exception exception) when (!definition.IsRequired && exception is not OperationCanceledException)
        {
            _hosts.Remove(host);
            host.Dispose();
            LogStartFailed(exception, definition.Id, definition.Path);
            return NotAvailable(
                definition,
                exception is IOException or UnauthorizedAccessException ? exception.Message : "The folder could not be read.",
                ErrorCodeOf(exception));
        }

        LogStarted(definition.Id, definition.Profile, definition.Path);
        return new SourceEntry(definition, host, Error: null);
    }

    private SourceEntry NotAvailable(SourceDefinition definition, string reason, SourceErrorCode code)
    {
        LogNotAvailable(definition.Id, reason, definition.Path);
        return new SourceEntry(definition, Index: null, reason, code);
    }

    /// <summary>
    /// The code of a source whose scan failed with an exception that is not a folder that is too large: a folder that
    /// vanished between the check and the scan is <see cref="SourceErrorCode.FolderMissing"/>, anything else (a
    /// permission, an I/O error, something unexpected) is <see cref="SourceErrorCode.NotReadable"/>.
    /// </summary>
    /// <param name="exception">What the scan threw.</param>
    internal static SourceErrorCode ErrorCodeOf(Exception exception) =>
        exception is DirectoryNotFoundException ? SourceErrorCode.FolderMissing : SourceErrorCode.NotReadable;

    // A source that was not available because its folder was missing is tried again once the folder is there. Any
    // other source stays as it is: one that is too large, for one, would be scanned again, up to the limit, at every
    // change of the list.
    private static bool CanBeStartedNow(SourceEntry entry) =>
        !entry.IsAvailable && entry.ErrorCode == SourceErrorCode.FolderMissing && Directory.Exists(entry.Definition.Path);

    private static bool IsSameFolder(SourceDefinition left, SourceDefinition right) =>
        left.Profile == right.Profile && SourcePaths.Same(left.Path, right.Path);

    private static int IndexOf(IReadOnlyList<SourceDefinition> definitions, string id)
    {
        for (int i = 0; i < definitions.Count; i++)
        {
            if (string.Equals(definitions[i].Id, id, StringComparison.Ordinal))
            {
                return i;
            }
        }

        return -1;
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Source {Id} ({Profile}): {Path}")]
    private partial void LogStarted(string id, SourceProfile profile, string path);

    [LoggerMessage(Level = LogLevel.Information, Message = "Source {Id} stopped: {Path}")]
    private partial void LogStopped(string id, string path);

    [LoggerMessage(Level = LogLevel.Information, Message = "Source {Id} added: {Path}")]
    private partial void LogAdded(string id, string path);

    [LoggerMessage(Level = LogLevel.Information, Message = "Source {Id} removed: {Path}")]
    private partial void LogRemoved(string id, string path);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Source {Id} is not available: {Reason} ({Path})")]
    private partial void LogNotAvailable(string id, string reason, string path);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Source {Id} could not be started ({Path})")]
    private partial void LogStartFailed(Exception exception, string id, string path);

    [LoggerMessage(Level = LogLevel.Warning, Message = "The sources file {File} cannot be used, the last valid list stays: {Reason}")]
    private partial void LogFileInvalid(string file, string reason);

    [LoggerMessage(Level = LogLevel.Information, Message = "The sources file {File} can be used again")]
    private partial void LogFileValid(string file);

    [LoggerMessage(Level = LogLevel.Error, Message = "The sources file {File} could not be written")]
    private partial void LogWriteFailed(Exception exception, string file);

    [LoggerMessage(Level = LogLevel.Error, Message = "Reading the sources file {File} again failed, the sources stay as they are")]
    private partial void LogReloadFailed(Exception exception, string file);

    // The sources file as it is now: its text, and the sources that the reader made of it in the same order. The text
    // is null when there is no file, and the sources are those that are shown.
    private sealed record FileState(string? Text, IReadOnlyList<SourceDefinition> Definitions);
}
