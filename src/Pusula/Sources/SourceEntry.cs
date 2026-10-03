using Pusula.Indexing;

namespace Pusula.Sources;

/// <summary>A source as the server runs it: the configured folder and, when it could be started, its index.</summary>
/// <param name="Definition">The folder as it is configured.</param>
/// <param name="Index">The index of the folder; null when the source is not available.</param>
/// <param name="Error">Why the source is not available (its folder does not exist, cannot be read, or is too large to show), in a sentence; null when it is.</param>
/// <param name="ErrorCode">Why the source is not available, as a code; null when it is. <see cref="SourceErrorCode.FolderMissing"/>: the source is started when the folder appears (the next time the list is read). Any other reason keeps it as it is until its folder or profile is changed, or the server is started again.</param>
internal sealed record SourceEntry(SourceDefinition Definition, IIndexProvider? Index, string? Error, SourceErrorCode? ErrorCode = null)
{
    /// <summary>True when the folder was read and its index is kept up to date.</summary>
    public bool IsAvailable => Index is not null;
}
