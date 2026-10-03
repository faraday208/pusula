using System.Diagnostics.CodeAnalysis;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using Pusula.Indexing;

namespace Pusula.Sources;

/// <summary>
/// Changes the text of a sources file: adds a source to the list or takes one out. The other items stay as they
/// were written (<c>"path": "~/.claude"</c> stays, <c>"profile": "auto"</c> stays, an item without an id stays
/// without one, and so does any property this program does not know). Only the comments are lost, because the
/// text is written out again.
/// </summary>
internal static class SourcesFileEditor
{
    // Easy to read in an editor: the letters of a name stay what they are (no ı for a dotless i).
    private static readonly JsonSerializerOptions WriteOptions = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    /// <summary>Adds the source at the end of the list.</summary>
    /// <param name="text">The text of the sources file; null when there is no file yet, which then starts with <paramref name="current"/>.</param>
    /// <param name="current">The sources as the file lists them now, in order (what the reader made of <paramref name="text"/>).</param>
    /// <param name="homeDirectory">The user's home directory.</param>
    /// <param name="source">The source to add.</param>
    /// <param name="id">The id of the source.</param>
    /// <param name="json">The new text of the file.</param>
    /// <param name="reason">Why the text cannot be changed, in one line.</param>
    public static bool TryAdd(
        string? text,
        IReadOnlyList<SourceDefinition> current,
        string homeDirectory,
        NewSource source,
        string id,
        [NotNullWhen(true)] out string? json,
        [NotNullWhen(false)] out string? reason)
    {
        json = null;
        if (!TryOpen(text, current, homeDirectory, out JsonObject? root, out JsonArray? sources, out reason))
        {
            return false;
        }

        sources.Add(new JsonObject
        {
            ["id"] = id,
            ["name"] = source.Name,
            ["path"] = source.Path,
            ["profile"] = source.Profile,
        });
        json = Serialize(root);
        return true;
    }

    /// <summary>Takes one source out of the list.</summary>
    /// <param name="text">The text of the sources file; null when there is no file yet, which then starts with <paramref name="current"/>.</param>
    /// <param name="current">The sources as the file lists them now, in order (what the reader made of <paramref name="text"/>).</param>
    /// <param name="homeDirectory">The user's home directory.</param>
    /// <param name="index">The position of the source in <paramref name="current"/>; the item of the file at the same position is taken out.</param>
    /// <param name="json">The new text of the file.</param>
    /// <param name="reason">Why the text cannot be changed, in one line.</param>
    public static bool TryRemove(
        string? text,
        IReadOnlyList<SourceDefinition> current,
        string homeDirectory,
        int index,
        [NotNullWhen(true)] out string? json,
        [NotNullWhen(false)] out string? reason)
    {
        json = null;
        if (!TryOpen(text, current, homeDirectory, out JsonObject? root, out JsonArray? sources, out reason))
        {
            return false;
        }

        sources.RemoveAt(index);
        json = Serialize(root);
        return true;
    }

    // The items of the file as they are written, ready to be changed. The reader makes one source of every item and
    // keeps their order, so that the position of a source is the position of its item.
    private static bool TryOpen(
        string? text,
        IReadOnlyList<SourceDefinition> current,
        string homeDirectory,
        [NotNullWhen(true)] out JsonObject? root,
        [NotNullWhen(true)] out JsonArray? sources,
        [NotNullWhen(false)] out string? reason)
    {
        root = null;
        sources = null;

        if (text is null)
        {
            // No file yet: the list that is shown now is what it starts with.
            var items = new JsonArray();
            foreach (SourceDefinition definition in current)
            {
                items.Add(Baseline(definition, homeDirectory));
            }

            sources = items;
            root = new JsonObject { ["sources"] = items };
            reason = null;
            return true;
        }

        JsonNode? node;
        try
        {
            node = JsonNode.Parse(text, nodeOptions: null, documentOptions: SourcesFileReader.ReadOptions);
        }
        catch (JsonException exception)
        {
            reason = "invalid JSON: " + SourcesFileReader.OneLine(exception.Message);
            return false;
        }

        JsonObject? parsed = node as JsonObject;
        JsonArray? array;
        try
        {
            array = parsed?["sources"] as JsonArray;
        }
        catch (ArgumentException)
        {
            // A property that is in the file twice: the reader takes the last one, but a JsonObject refuses it, and
            // writing the file out again would drop the other one without anyone having said so.
            reason = "a property of the file is there more than once";
            return false;
        }

        if (parsed is null || array is null)
        {
            reason = "expected an object with a \"sources\" array";
            return false;
        }

        if (array.Count != current.Count)
        {
            reason = "the \"sources\" array does not hold the sources that were read from it";
            return false;
        }

        root = parsed;
        sources = array;
        reason = null;
        return true;
    }

    // The item of a source of the list as it is shown now: "auto" for a profile that "auto" gives anyway, and a path
    // below the home directory as "~/...", which is what the user would have written.
    private static JsonObject Baseline(SourceDefinition definition, string homeDirectory) => new()
    {
        ["id"] = definition.Id,
        ["name"] = definition.Name,
        ["path"] = Portable(definition.Path, homeDirectory),
        ["profile"] = SourceProfiles.Detect(definition.Path) == definition.Profile ? "auto" : SourceProfiles.Name(definition.Profile),
    };

    private static string Portable(string fullPath, string homeDirectory)
    {
        if (homeDirectory.Length == 0)
        {
            return fullPath;
        }

        string prefix = Path.TrimEndingDirectorySeparator(Path.GetFullPath(homeDirectory)) + Path.DirectorySeparatorChar;
        return fullPath.StartsWith(prefix, PathComparison.Current)
            ? "~/" + fullPath[prefix.Length..].Replace(Path.DirectorySeparatorChar, '/')
            : fullPath;
    }

    private static string Serialize(JsonObject root) => root.ToJsonString(WriteOptions) + "\n";
}
