using Pusula.Startup;

namespace Pusula.Updates;

/// <summary>
/// What the checks for a newer release have found so far, in memory only: nothing is written to a file, and a new start begins again at
/// <see cref="UpdateState.NotChecked"/>. The service of the checks writes it, the endpoint reads it.
/// </summary>
internal sealed class UpdateNotice
{
    private readonly Lock _lock = new();
    private readonly string _current;
    private readonly SemanticVersion? _currentVersion;
    private readonly TimeProvider _time;
    private UpdateResponse _answer;

    /// <summary>Starts with nothing known.</summary>
    /// <param name="enabled">Whether the check is on; when it is not, the state stays <see cref="UpdateState.Off"/>.</param>
    /// <param name="current">The version of this pusula (<see cref="AppVersion.Current"/>).</param>
    /// <param name="time">The clock that dates an answer.</param>
    public UpdateNotice(bool enabled, string current, TimeProvider time)
    {
        ArgumentNullException.ThrowIfNull(current);
        ArgumentNullException.ThrowIfNull(time);

        Enabled = enabled;
        _current = current;
        _time = time;
        _currentVersion = SemanticVersion.TryParse(current, out SemanticVersion? parsed) ? parsed : null;
        _answer = new UpdateResponse(enabled ? UpdateState.NotChecked : UpdateState.Off, current);
    }

    /// <summary>Whether the check is on.</summary>
    public bool Enabled { get; }

    /// <summary>What is known now.</summary>
    public UpdateResponse Snapshot()
    {
        lock (_lock)
        {
            return _answer;
        }
    }

    /// <summary>A check found the latest release. It is newer when its version is higher than this one; a version of this pusula that cannot be read is never older.</summary>
    /// <param name="tag">The tag of the release, already checked (<see cref="ReleaseTag"/>).</param>
    public void RecordLatest(ReleaseTag tag)
    {
        ArgumentNullException.ThrowIfNull(tag);

        bool newer = _currentVersion is not null && tag.Version.CompareTo(_currentVersion) > 0;
        var answer = new UpdateResponse(newer ? UpdateState.Newer : UpdateState.UpToDate, _current, tag.Version.Text, tag.Url, _time.GetUtcNow());
        lock (_lock)
        {
            _answer = answer;
        }
    }

    /// <summary>
    /// A check failed. An answer that an earlier check found stays (a release does not go away because GitHub did not answer once); with none, the state is
    /// <see cref="UpdateState.Failed"/>.
    /// </summary>
    public void RecordFailure()
    {
        lock (_lock)
        {
            if (_answer.State is UpdateState.UpToDate or UpdateState.Newer)
            {
                return;
            }

            _answer = new UpdateResponse(UpdateState.Failed, _current, CheckedAt: _time.GetUtcNow());
        }
    }
}
