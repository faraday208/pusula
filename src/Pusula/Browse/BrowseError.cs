namespace Pusula.Browse;

/// <summary>
/// Why a folder cannot be listed. The name is the <c>code</c> of the problem details that the endpoint answers with, so
/// the names are part of the API (like those of <see cref="Pusula.Sources.EditError"/>, they travel as text).
/// </summary>
internal enum BrowseError
{
    /// <summary>400: the path is neither absolute nor starts with <c>~</c>, or it has a character that no path has.</summary>
    PathNotAbsolute,

    /// <summary>404: the folder does not exist or is not a directory.</summary>
    FolderNotFound,
}
