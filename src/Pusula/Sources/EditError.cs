namespace Pusula.Sources;

/// <summary>
/// Why adding or removing a source failed. The name is the <c>code</c> of the problem details that the endpoint
/// answers with, so the names are part of the API.
/// </summary>
internal enum EditError
{
    /// <summary>400: the request has no <c>path</c>, or a blank one.</summary>
    PathRequired,

    /// <summary>400: the path is neither absolute nor starts with <c>~</c>.</summary>
    PathNotAbsolute,

    /// <summary>400: the folder does not exist or is not a directory.</summary>
    FolderNotFound,

    /// <summary>400: the folder is the root of the file system or the home directory itself.</summary>
    TooBroad,

    /// <summary>400: the name is longer than <see cref="SourceRequestValidator.MaxNameLength"/> characters or has a control character.</summary>
    InvalidName,

    /// <summary>400: the profile is none of <c>auto</c>, <c>claude</c>, <c>vault</c> and <c>markdown</c>.</summary>
    InvalidProfile,

    /// <summary>403: the request does not come from the machine the server runs on, or a reverse proxy forwarded it (it has a forwarding header).</summary>
    Remote,

    /// <summary>403: the folders were named on the command line or in <c>Pusula:Root</c>; there is no sources file to change.</summary>
    CommandLine,

    /// <summary>403: the request was not made by a page of this server (its <c>Origin</c> or <c>Sec-Fetch-Site</c> says so).</summary>
    CrossOrigin,

    /// <summary>404: no source has the id.</summary>
    NotFound,

    /// <summary>409: a source with this folder exists.</summary>
    AlreadyListed,

    /// <summary>409: the sources file cannot be read, so it was left as it is.</summary>
    FileInvalid,

    /// <summary>500: the sources file could not be written.</summary>
    WriteFailed,
}
