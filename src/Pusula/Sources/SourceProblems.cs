using Microsoft.AspNetCore.Http.HttpResults;

namespace Pusula.Sources;

/// <summary>
/// The problem details that the endpoints which change the sources answer with: one for every <see cref="EditError"/>.
/// The machine-readable <c>code</c> is the name of the error; <c>detail</c> is a short English sentence.
/// </summary>
internal static class SourceProblems
{
    /// <summary>The problem details for an error.</summary>
    /// <param name="error">What went wrong.</param>
    /// <param name="reason">Why the sources file cannot be used, for <see cref="EditError.FileInvalid"/>.</param>
    public static ProblemHttpResult For(EditError error, string? reason = null)
    {
        (int status, string title, string detail) = Describe(error, reason);
        return TypedResults.Problem(
            title: title,
            detail: detail,
            statusCode: status,
            extensions: new Dictionary<string, object?> { ["code"] = error.ToString() });
    }

    private static (int Status, string Title, string Detail) Describe(EditError error, string? reason) => error switch
    {
        EditError.PathRequired => (StatusCodes.Status400BadRequest, "Path required", "'path' is required."),
        EditError.PathNotAbsolute => (StatusCodes.Status400BadRequest, "Path not absolute", "'path' must be absolute, or start with '~/' (the home directory)."),
        EditError.FolderNotFound => (StatusCodes.Status400BadRequest, "Folder not found", "The folder does not exist or is not a directory."),
        EditError.TooBroad => (StatusCodes.Status400BadRequest, "Folder too broad", "The root of the file system and the home directory itself cannot be a source."),
        EditError.InvalidName => (
            StatusCodes.Status400BadRequest,
            "Invalid name",
            $"'name' must have at most {SourceRequestValidator.MaxNameLength} characters and no control characters."),
        EditError.InvalidProfile => (StatusCodes.Status400BadRequest, "Invalid profile", "'profile' must be auto, claude, vault or markdown."),
        EditError.Remote => (StatusCodes.Status403Forbidden, "Not allowed from another machine", "Sources can only be changed from the machine this server runs on."),
        EditError.CommandLine => (
            StatusCodes.Status403Forbidden,
            "Sources cannot be changed",
            "The folders were named on the command line or in Pusula:Root; only a sources file can be changed."),
        EditError.CrossOrigin => (StatusCodes.Status403Forbidden, "Cross-origin request refused", "The request does not come from a page of this server."),
        EditError.NotFound => (StatusCodes.Status404NotFound, "Source not found", "No source has this id."),
        EditError.AlreadyListed => (StatusCodes.Status409Conflict, "Folder already listed", "A source with this folder exists."),
        EditError.FileInvalid => (
            StatusCodes.Status409Conflict,
            "Sources file not usable",
            "The sources file cannot be used, so it was left as it is" + (reason is null ? "." : ": " + reason)),
        EditError.WriteFailed => (StatusCodes.Status500InternalServerError, "Sources file not written", "The sources file could not be written."),
        _ => throw new ArgumentOutOfRangeException(nameof(error), error, "Unknown error."),
    };
}
