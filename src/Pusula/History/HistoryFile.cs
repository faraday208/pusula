using System.Diagnostics.CodeAnalysis;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using Pusula.Indexing;
using Pusula.Links;

namespace Pusula.History;

/// <summary>What reading a history file gave.</summary>
/// <param name="Days">The summaries in the file; empty when there is no file, or when it cannot be used.</param>
/// <param name="Problem">Why the file cannot be used, in a few words; null when it can (a file that is not there can be used: it holds nothing).</param>
internal readonly record struct HistoryRead(IReadOnlyList<DailySummary> Days, string? Problem)
{
    /// <summary>There is no file: nothing is kept yet.</summary>
    public static HistoryRead Nothing { get; } = new([], null);
}

/// <summary>
/// The file that the daily summaries of one source are kept in: a compact JSON file in the state folder of pusula, written whole
/// and replaced in one step. It carries the folder and the profile of the source it belongs to, for the person who finds it and to tell
/// a file that belongs to another source. The text of the files is not in it, only their token counts.
/// </summary>
/// <remarks>
/// <code>
/// { "version": 1, "root": "/home/me/.claude", "profile": "Claude",
///   "days": [ { "day": "2026-10-12", "takenAt": "2026-10-12T09:14:03+00:00",
///               "files": { "CLAUDE.md": [3495, 3495] },
///               "broken": [ ["guides/setup.md", "ClaudePath", "~/.claude/X.md"] ] } ] }
/// </code>
/// A file is the tokens in all and in every session of each file by index key, and each broken link as its file, kind and text.
/// </remarks>
internal static class HistoryFile
{
    /// <summary>The version of the format. A file of another version is not read, and is replaced by the next write.</summary>
    internal const int CurrentVersion = 1;

    // Only the person the files belong to may read or change them: the history says which files a person has, and how big.
    private const UnixFileMode PrivateFolder = UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute;
    private const UnixFileMode PrivateFile = UnixFileMode.UserRead | UnixFileMode.UserWrite;

    // The letters of a name stay what they are (no \u escapes for a ş), as in the sources file; the names of the properties are camelCase, the
    // names of the files (the keys of "files") stay as they are.
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        AllowDuplicateProperties = false,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    /// <summary>The folder of an index as the history spells it: the full path, without a separator at the end.</summary>
    /// <param name="index">The index snapshot.</param>
    public static string RootOf(ConfigIndex index) => Path.TrimEndingDirectorySeparator(Path.GetFullPath(index.Root));

    /// <summary>
    /// The name of the file of a source: the first 16 hexadecimal digits (lowercase) of the SHA-256 of the profile and the folder, and
    /// <c>.json</c>. Where paths are compared ignoring case (Windows) the folder is taken in upper case, so that one folder written in two
    /// ways has one file.
    /// </summary>
    /// <param name="profile">How the source is scanned.</param>
    /// <param name="root">The folder, as <see cref="RootOf"/> gives it.</param>
    public static string NameOf(SourceProfile profile, string root)
    {
        string folder = string.Equals("a", "A", PathComparison.Current) ? root.ToUpperInvariant() : root;
        byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes($"{profile}\n{folder}"));
        return Convert.ToHexStringLower(hash.AsSpan(0, 8)) + ".json";
    }

    /// <summary>
    /// Reads the summaries of a file. A file that is not there holds nothing. A file that cannot be read, is not valid, is of another
    /// version or belongs to another folder or profile cannot be used: it holds nothing either, and the problem says why.
    /// </summary>
    /// <param name="path">Full path of the file.</param>
    /// <param name="profile">The profile of the source the file is for.</param>
    /// <param name="root">The folder of the source the file is for, as <see cref="RootOf"/> gives it.</param>
    public static HistoryRead Read(string path, SourceProfile profile, string root)
    {
        Stored? stored;
        try
        {
            // Sharing as much as there is to share: a pusula that replaces the file at this moment is not held up by this one.
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            stored = JsonSerializer.Deserialize<Stored>(stream, Options);
        }
        catch (Exception exception) when (exception is FileNotFoundException or DirectoryNotFoundException)
        {
            return HistoryRead.Nothing;
        }
        catch (JsonException)
        {
            return Unusable("it is not a history file in JSON");
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return Unusable("it cannot be read: " + exception.Message);
        }

        if (stored is null)
        {
            return Unusable("it is not a history file in JSON");
        }

        if (stored.Version != CurrentVersion)
        {
            return Unusable($"it is of version {stored.Version}, and this pusula reads version {CurrentVersion}");
        }

        if (!string.Equals(stored.Root, root, PathComparison.Current) || !string.Equals(stored.Profile, profile.ToString(), StringComparison.Ordinal))
        {
            return Unusable($"it belongs to another source ({stored.Profile} {stored.Root})");
        }

        var days = new List<DailySummary>(stored.Days?.Count ?? 0);
        foreach (StoredDay entry in stored.Days ?? [])
        {
            if (!TryRead(entry, out DailySummary? day))
            {
                return Unusable("a day in it is not valid");
            }

            days.Add(day);
        }

        return new HistoryRead(days, null);
    }

    /// <summary>
    /// Writes the summaries so that nobody ever reads half of the file: the text goes to a new file in the same folder (its name starts with
    /// a dot and ends with <c>.tmp</c>), and that file then replaces the old one in one step. Nothing is left behind when writing fails. The
    /// folder is made when there is none. Where Unix permissions exist, the history folder and the folder above it (<c>pusula</c>) are made with
    /// mode 0700 and the file with mode 0600: only the person the files belong to can see them.
    /// </summary>
    /// <param name="folder">Full path of the history folder.</param>
    /// <param name="name">The name of the file, as <see cref="NameOf"/> gives it.</param>
    /// <param name="profile">The profile of the source the file is for.</param>
    /// <param name="root">The folder of the source the file is for, as <see cref="RootOf"/> gives it.</param>
    /// <param name="days">The summaries to keep, in the order they are written.</param>
    /// <exception cref="IOException">The file or its folder cannot be written.</exception>
    /// <exception cref="UnauthorizedAccessException">The file or its folder may not be written.</exception>
    public static void Write(string folder, string name, SourceProfile profile, string root, IEnumerable<DailySummary> days)
    {
        var stored = new Stored(CurrentVersion, root, profile.ToString(), [.. days.OrderBy(day => day.Day).Select(ToStored)]);
        byte[] bytes = JsonSerializer.SerializeToUtf8Bytes(stored, Options);

        CreateFolder(folder);
        string target = Path.Join(folder, name);
        string temporary = Path.Join(folder, TemporaryNameOf(name));
        try
        {
            using (var stream = new FileStream(temporary, NewFileOptions()))
            {
                stream.Write(bytes);
            }

            File.Move(temporary, target, overwrite: true);
        }
        catch
        {
            TryDelete(temporary);
            throw;
        }
    }

    /// <summary>The name of the file that a file is written to first: it starts with a dot and ends with <c>.tmp</c>, and is not the name of another temporary file.</summary>
    /// <param name="name">The name of the file that is written.</param>
    internal static string TemporaryNameOf(string name) => "." + name + "." + Guid.NewGuid().ToString("N") + ".tmp";

    private static HistoryRead Unusable(string problem) => new([], problem);

    private static void CreateFolder(string folder)
    {
        if (OperatingSystem.IsWindows())
        {
            Directory.CreateDirectory(folder);
            return;
        }

        // A mode is the mode of the last folder of a path only, so the folder above (pusula) is made on its own, before the history folder.
        if (Path.GetDirectoryName(folder) is { } parent)
        {
            Directory.CreateDirectory(parent, PrivateFolder);
        }

        Directory.CreateDirectory(folder, PrivateFolder);
    }

    private static FileStreamOptions NewFileOptions()
    {
        var options = new FileStreamOptions { Mode = FileMode.CreateNew, Access = FileAccess.Write, Share = FileShare.None };
        if (!OperatingSystem.IsWindows())
        {
            options.UnixCreateMode = PrivateFile;
        }

        return options;
    }

    private static void TryDelete(string file)
    {
        try
        {
            File.Delete(file);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // Best effort: the error that is being thrown is the one that matters.
        }
    }

    private static StoredDay ToStored(DailySummary day) => new(
        day.Day,
        day.TakenAt,
        day.Files.ToDictionary(file => file.Key, file => new[] { file.Value.Total, file.Value.EverySession }, StringComparer.Ordinal),
        [.. day.Broken.Select(link => new[] { link.Source, link.Kind.ToString(), link.Raw })]);

    // A day is valid when every part of it is: one wrong entry makes the whole file unusable, and the next write replaces it.
    private static bool TryRead(StoredDay entry, [NotNullWhen(true)] out DailySummary? day)
    {
        day = null;
        if (entry is not { Day: { } date, TakenAt: { } takenAt, Files: { } stored, Broken: { } brokenLinks })
        {
            return false;
        }

        var files = new Dictionary<string, FileTokens>(stored.Count, DailySummary.Keys);
        foreach ((string path, int[] counts) in stored)
        {
            if (counts is not [>= 0 and var total, >= 0 and var everySession])
            {
                return false;
            }

            files[path] = new FileTokens(total, everySession);
        }

        var broken = new List<BrokenLink>(brokenLinks.Count);
        foreach (string[] item in brokenLinks)
        {
            if (item is not [{ } source, { } kindName, { } raw] || !Enum.TryParse(kindName, ignoreCase: false, out LinkKind kind) || !Enum.IsDefined(kind))
            {
                return false;
            }

            broken.Add(new BrokenLink(source, kind, raw));
        }

        day = new DailySummary(date, takenAt, files, broken);
        return true;
    }

    // The file as JSON. In a file that someone changed, anything but the version may be missing.
    private sealed record Stored(int Version, string? Root, string? Profile, List<StoredDay>? Days);

    private sealed record StoredDay(DateOnly? Day, DateTimeOffset? TakenAt, Dictionary<string, int[]>? Files, List<string[]>? Broken);
}
