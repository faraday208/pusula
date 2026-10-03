using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using Pusula.Indexing;
using Pusula.Startup;

namespace Pusula.Sources;

/// <summary>
/// Reads a sources file: <c>{ "sources": [ { "id", "name", "path", "profile" } ] }</c>. Comments and a comma after the
/// last item are allowed. Only <c>path</c> is required; <c>~</c> in it is the home directory, and a relative path is
/// relative to the folder of the file. The list may be empty: that is what the file says after its last source was
/// removed. A file that cannot be used is never an exception: it is <c>false</c> and a reason on one line.
/// </summary>
internal static class SourcesFileReader
{
    /// <summary>What a sources file may contain besides the JSON itself: comments and a comma after the last item.</summary>
    internal static readonly JsonDocumentOptions ReadOptions = new()
    {
        CommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    /// <summary>Reads and parses the file.</summary>
    /// <param name="path">Full path of the file.</param>
    /// <param name="homeDirectory">The user's home directory, which a leading <c>~</c> expands to.</param>
    /// <param name="sources">The sources, in the order of the file.</param>
    /// <param name="reason">Why the file cannot be used, in one line.</param>
    public static bool TryRead(
        string path,
        string homeDirectory,
        [NotNullWhen(true)] out IReadOnlyList<SourceDefinition>? sources,
        [NotNullWhen(false)] out string? reason)
    {
        if (!TryReadText(path, out string? json, out reason))
        {
            sources = null;
            return false;
        }

        return TryParse(json, Path.GetDirectoryName(path) ?? string.Empty, homeDirectory, out sources, out reason);
    }

    /// <summary>Reads the text of the file.</summary>
    /// <param name="path">Full path of the file.</param>
    /// <param name="text">The text.</param>
    /// <param name="reason">Why the file cannot be read, in one line.</param>
    public static bool TryReadText(string path, [NotNullWhen(true)] out string? text, [NotNullWhen(false)] out string? reason)
    {
        try
        {
            text = File.ReadAllText(path);
            reason = null;
            return true;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            text = null;
            reason = OneLine(exception.Message);
            return false;
        }
    }

    /// <summary>Parses the text of a sources file.</summary>
    /// <param name="json">The text.</param>
    /// <param name="baseDirectory">The folder that a relative <c>path</c> is relative to: the folder of the file.</param>
    /// <param name="homeDirectory">The user's home directory, which a leading <c>~</c> expands to.</param>
    /// <param name="sources">The sources, in the order of the file; a folder that does not exist is not an error here.</param>
    /// <param name="reason">Why the text cannot be used, in one line.</param>
    public static bool TryParse(
        string json,
        string baseDirectory,
        string homeDirectory,
        [NotNullWhen(true)] out IReadOnlyList<SourceDefinition>? sources,
        [NotNullWhen(false)] out string? reason)
    {
        sources = null;

        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(json, ReadOptions);
        }
        catch (JsonException exception)
        {
            reason = "invalid JSON: " + OneLine(exception.Message);
            return false;
        }

        using (document)
        {
            if (document.RootElement.ValueKind != JsonValueKind.Object
                || !document.RootElement.TryGetProperty("sources", out JsonElement list)
                || list.ValueKind != JsonValueKind.Array)
            {
                reason = "expected an object with a \"sources\" array";
                return false;
            }

            return TryReadItems(list, baseDirectory, homeDirectory, out sources, out reason);
        }
    }

    private static bool TryReadItems(
        JsonElement list,
        string baseDirectory,
        string homeDirectory,
        [NotNullWhen(true)] out IReadOnlyList<SourceDefinition>? sources,
        [NotNullWhen(false)] out string? reason)
    {
        sources = null;
        var items = new List<Item>();
        var explicitIds = new HashSet<string>(StringComparer.Ordinal);

        int number = 0;
        foreach (JsonElement element in list.EnumerateArray())
        {
            number++;
            if (!TryReadItem(element, number, baseDirectory, homeDirectory, out Item? item, out reason))
            {
                return false;
            }

            if (item.Id is not null && !explicitIds.Add(item.Id))
            {
                reason = $"source {number}: the id \"{item.Id}\" is used more than once";
                return false;
            }

            items.Add(item);
        }

        // Derived ids are given out after all written ones, so that no written id is taken away by a derived one.
        var taken = new HashSet<string>(explicitIds, StringComparer.Ordinal);
        var result = new List<SourceDefinition>(items.Count);
        foreach (Item item in items)
        {
            string name = item.Name ?? SourceIds.NameOf(item.FullPath);
            string id = item.Id ?? SourceIds.MakeUnique(SourceIds.Derive(name), taken);
            result.Add(new SourceDefinition(id, name, item.FullPath, item.Profile ?? SourceProfiles.Detect(item.FullPath)));
        }

        sources = result;
        reason = null;
        return true;
    }

    private static bool TryReadItem(
        JsonElement element,
        int number,
        string baseDirectory,
        string homeDirectory,
        [NotNullWhen(true)] out Item? item,
        [NotNullWhen(false)] out string? reason)
    {
        item = null;
        if (element.ValueKind != JsonValueKind.Object)
        {
            reason = $"source {number}: expected an object";
            return false;
        }

        if (!TryGetString(element, "id", number, out string? id, out reason)
            || !TryGetString(element, "name", number, out string? name, out reason)
            || !TryGetString(element, "path", number, out string? path, out reason)
            || !TryGetString(element, "profile", number, out string? profileText, out reason))
        {
            return false;
        }

        if (id is not null && !SourceIds.IsValid(id))
        {
            reason = $"source {number}: the id \"{id}\" must be lowercase letters and digits, joined by single hyphens";
            return false;
        }

        if (string.IsNullOrWhiteSpace(path))
        {
            reason = $"source {number}: \"path\" is required";
            return false;
        }

        SourceProfile? profile = null;
        if (profileText is not null && !SourceProfiles.TryParse(profileText, out profile))
        {
            reason = $"source {number}: unknown profile \"{profileText}\" (use auto, claude, vault or markdown)";
            return false;
        }

        if (SourcePaths.HasInvalidCharacter(path))
        {
            reason = $"source {number}: \"path\" has a character that no path has";
            return false;
        }

        string fullPath;
        try
        {
            fullPath = Path.GetFullPath(PusulaOptions.ExpandHome(path.Trim(), homeDirectory), baseDirectory);
        }
        catch (Exception exception) when (exception is InvalidOperationException or ArgumentException or NotSupportedException or IOException)
        {
            // No home directory for a "~", or a path the framework refuses to resolve (too long, for one).
            reason = $"source {number}: {OneLine(exception.Message)}";
            return false;
        }

        item = new Item(id, string.IsNullOrWhiteSpace(name) ? null : name.Trim(), fullPath, profile);
        reason = null;
        return true;
    }

    // An absent value and null are the same; anything but a string is an error.
    private static bool TryGetString(JsonElement element, string property, int number, out string? value, [NotNullWhen(false)] out string? reason)
    {
        value = null;
        reason = null;
        if (!element.TryGetProperty(property, out JsonElement found) || found.ValueKind == JsonValueKind.Null)
        {
            return true;
        }

        if (found.ValueKind != JsonValueKind.String)
        {
            reason = $"source {number}: \"{property}\" must be a string";
            return false;
        }

        try
        {
            value = found.GetString();
        }
        catch (InvalidOperationException)
        {
            // Text that is no text: an escape that is half of a character (a lone "\ud800").
            reason = $"source {number}: \"{property}\" is not valid text";
            return false;
        }

        return true;
    }

    /// <summary>The text on one line: the lines of a message, trimmed, joined by a space.</summary>
    /// <param name="text">The text.</param>
    internal static string OneLine(string text) =>
        string.Join(' ', text.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));

    private sealed record Item(string? Id, string? Name, string FullPath, SourceProfile? Profile);
}
