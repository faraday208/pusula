using Microsoft.AspNetCore.Http.HttpResults;

namespace Pusula.Browse;

/// <summary>
/// The problem details that listing a folder answers with: one for every <see cref="BrowseError"/>. The machine-readable
/// <c>code</c> is the name of the error; <c>detail</c> is a short English sentence. The refusals (403) are those of
/// <see cref="Pusula.Sources.SourceProblems"/>.
/// </summary>
internal static class BrowseProblems
{
    /// <summary>The problem details for an error.</summary>
    /// <param name="error">What went wrong.</param>
    public static ProblemHttpResult For(BrowseError error)
    {
        (int status, string title, string detail) = Describe(error);
        return TypedResults.Problem(
            title: title,
            detail: detail,
            statusCode: status,
            extensions: new Dictionary<string, object?> { ["code"] = error.ToString() });
    }

    private static (int Status, string Title, string Detail) Describe(BrowseError error) => error switch
    {
        BrowseError.PathNotAbsolute => (
            StatusCodes.Status400BadRequest,
            "Path not absolute",
            "'path' must be absolute, or start with '~/' (the home directory), and have no character that no path has."),
        BrowseError.FolderNotFound => (StatusCodes.Status404NotFound, "Folder not found", "The folder does not exist or is not a directory."),
        _ => throw new ArgumentOutOfRangeException(nameof(error), error, "Unknown error."),
    };
}
