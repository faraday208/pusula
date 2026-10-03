namespace Pusula.Sources;

/// <summary>Adds sources to the sources file and takes them out. The registry does it; the endpoints see this much of it.</summary>
internal interface ISourceEditor
{
    /// <summary>
    /// Adds the source to the end of the sources file, starts showing it and answers with it (its first index is built).
    /// The file is changed as a whole or not at all. Fails with <see cref="EditError.CommandLine"/> when the list does not
    /// come from a sources file, <see cref="EditError.FileInvalid"/> when the file cannot be read (it is left alone),
    /// <see cref="EditError.AlreadyListed"/> when a source has the folder, and <see cref="EditError.WriteFailed"/>.
    /// </summary>
    /// <param name="source">The source, as <see cref="SourceRequestValidator"/> checked it.</param>
    /// <param name="cancellationToken">Cancels the wait for another change of the list that is under way; the change itself is not interrupted.</param>
    Task<SourceEditResult> AddAsync(NewSource source, CancellationToken cancellationToken);

    /// <summary>
    /// Takes the source out of the sources file and stops showing it (its event streams end). The folder itself is not
    /// touched. Fails like <see cref="AddAsync"/>, and with <see cref="EditError.NotFound"/> when the file lists no such source.
    /// </summary>
    /// <param name="id">The id of the source.</param>
    /// <param name="cancellationToken">Cancels the wait for another change of the list that is under way; the change itself is not interrupted.</param>
    Task<SourceEditResult> RemoveAsync(string id, CancellationToken cancellationToken);
}
