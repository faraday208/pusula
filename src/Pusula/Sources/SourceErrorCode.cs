namespace Pusula.Sources;

/// <summary>
/// Why a source is not available, as a code that a page can act on. The name is the <c>errorCode</c> of the source in
/// <c>GET /api/sources</c> and the <c>code</c> of the problem details (503) that the endpoints of such a source answer
/// with, so the names are part of the API (like those of <see cref="EditError"/>, they travel as text). The sentence in
/// <c>error</c> says the same in English, for the log and for people.
/// </summary>
internal enum SourceErrorCode
{
    /// <summary>The folder does not exist (or is not a directory). The source is started when the folder appears.</summary>
    FolderMissing,

    /// <summary>The folder exists but its scan failed: with an I/O or permission error, or with anything else unexpected. It stays as it is until its folder or profile is changed, or the server is started again.</summary>
    NotReadable,

    /// <summary>The folder is more than is shown: it has more Markdown files, folders or Markdown than the scan limits allow. It stays as it is until its folder or profile is changed, or the server is started again.</summary>
    TooLarge,
}
