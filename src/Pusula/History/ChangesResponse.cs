using Pusula.Indexing;
using Pusula.Links;

namespace Pusula.History;

/// <summary>What changed in a source since the daily summaries that pusula keeps of it were taken, and whether today's summary is kept.</summary>
/// <param name="History">Whether today's summary is kept, and if it is not, why.</param>
/// <param name="FirstDay">The day of the oldest summary pusula has of the source (today's included once it is kept): the day the history starts. Left out when there is no summary.</param>
/// <param name="PreviousDay">The source as it is now against the newest summary of an earlier day; a day on which pusula was not opened is skipped. Left out when there is no summary of an earlier day.</param>
/// <param name="PreviousWeek">The source as it is now against the newest summary that is seven days old or older. Left out when there is no such summary.</param>
/// <param name="Error">Why today's summary could not be written (the message of the error); there only when the history is WriteFailed.</param>
public sealed record ChangesResponse(
    HistoryState History,
    DateOnly? FirstDay = null,
    ChangesSince? PreviousDay = null,
    ChangesSince? PreviousWeek = null,
    string? Error = null);

/// <summary>Whether the summary of today is kept, and if it is not, why.</summary>
public enum HistoryState
{
    /// <summary>Today's summary is kept: it was taken by this request or earlier today.</summary>
    Kept,

    /// <summary>pusula has no state folder to keep summaries in, because the home directory is not known. Nothing is written, and there is nothing to compare with.</summary>
    Off,

    /// <summary>The state folder is inside a folder that is shown, and pusula never writes into those: today's summary was not written. Older summaries are still compared.</summary>
    InsideSource,

    /// <summary>Writing today's summary failed; the error says why. Older summaries are still compared, and the next request tries again.</summary>
    WriteFailed,
}

/// <summary>How a source differs now from one earlier daily summary of it.</summary>
/// <param name="Day">The day of the summary that is compared with: the local calendar day of the machine pusula runs on.</param>
/// <param name="TakenAt">When that summary was taken.</param>
/// <param name="FilesBefore">The number of indexed files in the summary.</param>
/// <param name="FilesAfter">The number of indexed files now.</param>
/// <param name="EverySessionTokensBefore">Estimated tokens that were loaded at the start of every session, over all files of the summary.</param>
/// <param name="EverySessionTokensAfter">Estimated tokens that are loaded at the start of every session now, over all files. For a vault or a plain Markdown folder both numbers are 0.</param>
/// <param name="Added">Files that are in the source now and not in the summary, with their current values; the ones that add the most to every session first (then the largest, then by path).</param>
/// <param name="Removed">Files that are in the summary and not in the source now, with the values of the summary and no load mode, for the file is gone; the ones that added the most to every session first (then the largest, then by path).</param>
/// <param name="Resized">Files that are in both and whose tokens or tokens in every session differ; the biggest change in every-session tokens first (then in tokens, then by path). A file whose text changed but whose token counts did not is not listed.</param>
/// <param name="NewlyBroken">Links that are broken now and were not in the summary, each with the line of its first occurrence now; by file, then line.</param>
/// <param name="NoLongerBroken">Links that were broken in the summary and are not broken now: fixed, removed, or their file is gone. They have no line; by file, then kind, then text.</param>
public sealed record ChangesSince(
    DateOnly Day,
    DateTimeOffset TakenAt,
    int FilesBefore,
    int FilesAfter,
    int EverySessionTokensBefore,
    int EverySessionTokensAfter,
    IReadOnlyList<ChangedFile> Added,
    IReadOnlyList<ChangedFile> Removed,
    IReadOnlyList<ResizedFile> Resized,
    IReadOnlyList<ChangedLink> NewlyBroken,
    IReadOnlyList<ChangedLink> NoLongerBroken);

/// <summary>A file that was added or removed.</summary>
/// <param name="Path">Index key of the file.</param>
/// <param name="Tokens">Estimated tokens of the file: now for an added file, in the summary for a removed one.</param>
/// <param name="EverySessionTokens">Estimated tokens the file adds to every session: now for an added file, in the summary for a removed one.</param>
/// <param name="LoadMode">When Claude Code loads the file. Left out for a removed file, which is gone.</param>
public sealed record ChangedFile(string Path, int Tokens, int EverySessionTokens, LoadMode? LoadMode = null);

/// <summary>A file that is in the summary and in the source, with different token counts.</summary>
/// <param name="Path">Index key of the file.</param>
/// <param name="LoadMode">When Claude Code loads the file now.</param>
/// <param name="TokensBefore">Estimated tokens of the file in the summary.</param>
/// <param name="TokensAfter">Estimated tokens of the file now.</param>
/// <param name="EverySessionTokensBefore">Estimated tokens the file added to every session in the summary.</param>
/// <param name="EverySessionTokensAfter">Estimated tokens the file adds to every session now.</param>
public sealed record ResizedFile(string Path, LoadMode LoadMode, int TokensBefore, int TokensAfter, int EverySessionTokensBefore, int EverySessionTokensAfter);

/// <summary>A link that became broken or stopped being broken. A broken link is told apart by its file, its kind and its text, not by its line: lines move when text is added above.</summary>
/// <param name="Source">Index key of the file that contains the link.</param>
/// <param name="Kind">The syntax the link was written in.</param>
/// <param name="Raw">The link text as written.</param>
/// <param name="Line">1-based line of the first occurrence of the link in the source file now; left out for a link that is no longer broken.</param>
public sealed record ChangedLink(string Source, LinkKind Kind, string Raw, int? Line = null);
