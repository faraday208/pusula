namespace Pusula.Updates;

/// <summary>What pusula knows about a newer release: the answer of <c>GET /api/update</c>.</summary>
/// <param name="State">How the check stands.</param>
/// <param name="Current">The version of this pusula.</param>
/// <param name="Latest">The version of the latest release (<c>0.4.0</c>, without the <c>v</c> of its tag); left out until a check has found it.</param>
/// <param name="Url">The page of that release on GitHub, made from its tag; left out when <paramref name="Latest"/> is.</param>
/// <param name="CheckedAt">When the answer was found (when the check failed, when it was tried); left out until a check has run.</param>
public sealed record UpdateResponse(UpdateState State, string Current, string? Latest = null, string? Url = null, DateTimeOffset? CheckedAt = null);

/// <summary>How the check for a newer release stands.</summary>
public enum UpdateState
{
    /// <summary>The check is turned off (<c>Pusula:CheckForUpdates</c>, or this is a build run with <c>dotnet run</c>): nothing is asked and nothing is known.</summary>
    Off,

    /// <summary>The check is on and has not found an answer yet: the first one runs right after the start.</summary>
    NotChecked,

    /// <summary>The latest release is this version or an older one (or this version cannot be compared).</summary>
    UpToDate,

    /// <summary>A newer release is out: <c>latest</c> and <c>url</c> say which and where.</summary>
    Newer,

    /// <summary>The check failed and there is no earlier answer. The next one runs a day later.</summary>
    Failed,
}
