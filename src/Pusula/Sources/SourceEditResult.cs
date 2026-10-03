namespace Pusula.Sources;

/// <summary>What came of adding or removing a source: it worked, or <see cref="Error"/> says what went wrong.</summary>
/// <param name="Entry">The source that was added; null for a removal and for a failure.</param>
/// <param name="Error">What went wrong; null when it worked.</param>
/// <param name="Reason">Why the sources file cannot be used, for <see cref="EditError.FileInvalid"/>.</param>
internal sealed record SourceEditResult(SourceEntry? Entry, EditError? Error, string? Reason)
{
    /// <summary>A source was added.</summary>
    /// <param name="entry">The source, started.</param>
    public static SourceEditResult Added(SourceEntry entry) => new(entry, Error: null, Reason: null);

    /// <summary>A source was removed.</summary>
    public static SourceEditResult Removed() => new(Entry: null, Error: null, Reason: null);

    /// <summary>Nothing was changed.</summary>
    /// <param name="error">What went wrong.</param>
    /// <param name="reason">Why the sources file cannot be used, for <see cref="EditError.FileInvalid"/>.</param>
    public static SourceEditResult Failed(EditError error, string? reason = null) => new(Entry: null, error, reason);
}
