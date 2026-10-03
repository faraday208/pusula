using System.Diagnostics.CodeAnalysis;

namespace Pusula.Links;

/// <summary>
/// Turns <see cref="RawLink"/>s of a folder of notes (a vault or a plain folder of Markdown files) into
/// <see cref="Link"/>s. Unlike <see cref="LinkResolver"/> it never looks at the file system: it only knows the notes
/// and the other files it was given. Every comparison ignores case, as Obsidian does.
/// </summary>
internal sealed class NoteLinkResolver
{
    private const string MarkdownExtension = ".md";
    private const StringComparison IgnoreCase = StringComparison.OrdinalIgnoreCase;

    private readonly string _normalizedRoot;

    // Notes: by exact path, by path ignoring case, and by every tail of the path without ".md" ("a/b/c" is found as
    // "a/b/c", "b/c" and "c"). Other files: the same, with the extension kept.
    private readonly HashSet<string> _notes;
    private readonly Dictionary<string, string> _notesIgnoringCase = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _otherFiles;
    private readonly Dictionary<string, string> _otherFilesIgnoringCase = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, List<string>> _notesByName;
    private readonly Dictionary<string, List<string>> _otherFilesByName;

    /// <summary>Creates a resolver over the given folder description.</summary>
    public NoteLinkResolver(NoteLinkContext context)
    {
        _normalizedRoot = LinkResolver.NormalizeRoot(context.RootFullPath);

        string[] notes = [.. context.NotePaths.Order(StringComparer.Ordinal)];
        _notes = new HashSet<string>(notes, StringComparer.Ordinal);
        _otherFiles = new HashSet<string>(context.FilePaths.Where(path => !_notes.Contains(path)), StringComparer.Ordinal);

        // The paths are added in order, so that of two paths that differ only in case the first one is found.
        foreach (string note in notes)
        {
            _notesIgnoringCase.TryAdd(note, note);
        }

        string[] others = [.. _otherFiles.Order(StringComparer.Ordinal)];
        foreach (string other in others)
        {
            _otherFilesIgnoringCase.TryAdd(other, other);
        }

        _notesByName = ByName(notes, stripMarkdownExtension: true);
        _otherFilesByName = ByName(others, stripMarkdownExtension: false);
    }

    /// <summary>
    /// Resolves one reference found in <paramref name="sourcePath"/>. Only Markdown links, wiki links and embeds are
    /// references of a note; any other kind gives null.
    /// </summary>
    /// <param name="raw">The reference.</param>
    /// <param name="sourcePath">Index key of the note that contains the reference.</param>
    public Link? Resolve(RawLink raw, string sourcePath) => raw.Kind switch
    {
        LinkKind.MarkdownLink => ResolveMarkdownLink(raw, sourcePath),
        LinkKind.WikiLink or LinkKind.Embed => ResolveWikiLink(raw, sourcePath),
        _ => null,
    };

    // Wiki links and embeds, in the order Obsidian looks: a path that starts with "./" or "../" is relative to the
    // note and nothing else; any other target is a path from the root of the folder, then a path from the folder of the
    // note, then a name (the end of a path). What is found nowhere is a note that is not written yet.
    private Link ResolveWikiLink(RawLink raw, string sourcePath)
    {
        (string target, string? heading) = LinkResolver.SplitWikiLink(raw.Raw);
        if (target.Length == 0)
        {
            return LinkResolver.Make(raw, sourcePath, heading, LinkStatus.Resolved);
        }

        string sourceDirectory = LinkResolver.DirectoryOf(sourcePath);
        if (target.StartsWith("./", StringComparison.Ordinal) || target.StartsWith("../", StringComparison.Ordinal))
        {
            return !LinkResolver.TryNormalize(sourceDirectory, target, out string? relative)
                ? LinkResolver.External(raw)
                : FindPath(raw, heading, relative, addMarkdownExtensionAlways: true) ?? Unwritten(raw, heading);
        }

        if (!LinkResolver.TryNormalize(string.Empty, target, out string? fromRoot))
        {
            return LinkResolver.External(raw);
        }

        if (fromRoot.Length == 0)
        {
            return Unwritten(raw, heading);
        }

        Link? found = FindPath(raw, heading, fromRoot, addMarkdownExtensionAlways: true);
        if (found is null && sourceDirectory.Length > 0 && LinkResolver.TryNormalize(sourceDirectory, target, out string? fromSource))
        {
            found = FindPath(raw, heading, fromSource, addMarkdownExtensionAlways: true);
        }

        return found ?? FindName(raw, heading, fromRoot, sourceDirectory) ?? Unwritten(raw, heading);
    }

    // Markdown links: the same decoding as in the Claude profile, then the note's folder, the root of the folder and
    // the name, in that order. What is found nowhere is broken: a Markdown link names a file that should exist.
    private Link ResolveMarkdownLink(RawLink raw, string sourcePath)
    {
        string destination = raw.Raw;
        if (destination.Length >= 2 && destination[0] == '<' && destination[^1] == '>')
        {
            destination = destination[1..^1];
        }

        destination = LinkResolver.StripFileScheme(destination);

        string? heading = null;
        int hash = destination.IndexOf('#');
        if (hash >= 0)
        {
            heading = LinkResolver.Decode(destination[(hash + 1)..]);
            destination = destination[..hash];
        }

        destination = LinkResolver.Decode(destination);
        if (heading is { Length: 0 })
        {
            heading = null;
        }

        if (destination.Length == 0)
        {
            return LinkResolver.Make(raw, sourcePath, heading, LinkStatus.Resolved);
        }

        // "~/..." is the user's home directory, never part of a folder of notes.
        if (destination == "~" || destination.StartsWith("~/", StringComparison.Ordinal))
        {
            return LinkResolver.External(raw);
        }

        if (LinkResolver.TryGetAbsolutePath(destination, out string? absolute))
        {
            return !LinkResolver.TryGetRootRelative(_normalizedRoot, absolute, IgnoreCase, out string? underRoot)
                ? LinkResolver.External(raw)
                : FindPath(raw, heading, underRoot, addMarkdownExtensionAlways: false) ?? Missing(raw, underRoot, heading);
        }

        string sourceDirectory = LinkResolver.DirectoryOf(sourcePath);
        if (!LinkResolver.TryNormalize(sourceDirectory, destination, out string? fromSource))
        {
            return LinkResolver.External(raw);
        }

        Link? found = FindPath(raw, heading, fromSource, addMarkdownExtensionAlways: false);
        if (found is null && LinkResolver.TryNormalize(string.Empty, destination, out string? fromRoot))
        {
            found = FindPath(raw, heading, fromRoot, addMarkdownExtensionAlways: false)
                ?? (fromRoot.Length > 0 ? FindName(raw, heading, fromRoot, sourceDirectory) : null);
        }

        return found ?? Missing(raw, fromSource, heading);
    }

    // The file at this exact path (ignoring case): the path itself, then the path with ".md" added. A Markdown link
    // adds ".md" only to a name without an extension; a wiki link always tries both.
    private Link? FindPath(RawLink raw, string? heading, string path, bool addMarkdownExtensionAlways)
    {
        if (path.Length == 0)
        {
            return null;
        }

        if (TryFindFile(path, out string? target, out LinkStatus status))
        {
            return LinkResolver.Make(raw, target, heading, status);
        }

        if ((addMarkdownExtensionAlways || Path.GetExtension(path).Length == 0)
            && TryFindFile(path + MarkdownExtension, out target, out status))
        {
            return LinkResolver.Make(raw, target, heading, status);
        }

        return null;
    }

    private bool TryFindFile(string path, [NotNullWhen(true)] out string? target, out LinkStatus status)
    {
        status = LinkStatus.Resolved;
        if (_notes.Contains(path))
        {
            target = path;
            return true;
        }

        if (_notesIgnoringCase.TryGetValue(path, out target))
        {
            return true;
        }

        status = LinkStatus.NonMarkdown;
        if (_otherFiles.Contains(path))
        {
            target = path;
            return true;
        }

        return _otherFilesIgnoringCase.TryGetValue(path, out target);
    }

    // The end of a path: a note whose path without ".md" is the name or ends with "/" and the name; otherwise a file
    // that is not a note, whose whole path is the name or ends with "/" and the name. Of several the one in the folder
    // of the source wins, then the shortest path, then the first in ordinal order.
    private Link? FindName(RawLink raw, string? heading, string name, string sourceDirectory)
    {
        string noteName = name.EndsWith(MarkdownExtension, IgnoreCase) ? name[..^MarkdownExtension.Length] : name;
        if (noteName.Length > 0 && _notesByName.TryGetValue(noteName, out List<string>? notes))
        {
            return LinkResolver.Make(raw, Best(notes, sourceDirectory), heading, LinkStatus.Resolved);
        }

        return _otherFilesByName.TryGetValue(name, out List<string>? others)
            ? LinkResolver.Make(raw, Best(others, sourceDirectory), heading, LinkStatus.NonMarkdown)
            : null;
    }

    // The candidates are in ordinal order, and only a strictly better one replaces the best so far.
    private static string Best(List<string> candidates, string sourceDirectory)
    {
        string best = candidates[0];
        bool bestIsLocal = IsIn(best, sourceDirectory);
        for (int i = 1; i < candidates.Count; i++)
        {
            string candidate = candidates[i];
            bool isLocal = IsIn(candidate, sourceDirectory);
            if ((isLocal && !bestIsLocal) || (isLocal == bestIsLocal && candidate.Length < best.Length))
            {
                best = candidate;
                bestIsLocal = isLocal;
            }
        }

        return best;
    }

    private static bool IsIn(string path, string directory) =>
        string.Equals(LinkResolver.DirectoryOf(path), directory, IgnoreCase);

    // Every path and every tail of it that starts after a "/": the keys a name can be written as.
    private static Dictionary<string, List<string>> ByName(string[] paths, bool stripMarkdownExtension)
    {
        var byName = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        foreach (string path in paths)
        {
            string name = stripMarkdownExtension && path.EndsWith(MarkdownExtension, IgnoreCase)
                ? path[..^MarkdownExtension.Length]
                : path;

            for (int start = 0; start < name.Length;)
            {
                string key = name[start..];
                if (!byName.TryGetValue(key, out List<string>? list))
                {
                    byName[key] = list = [];
                }

                list.Add(path);
                int slash = name.IndexOf('/', start);
                start = slash < 0 ? name.Length : slash + 1;
            }
        }

        return byName;
    }

    private static Link Unwritten(RawLink raw, string? heading) => LinkResolver.Make(raw, target: null, heading, LinkStatus.Pending);

    // Nothing is at the path: the link is broken, and it points where it would have to point. The folder itself is not a file.
    private static Link Missing(RawLink raw, string path, string? heading) =>
        path.Length == 0
            ? LinkResolver.Make(raw, target: null, heading, LinkStatus.NonMarkdown)
            : LinkResolver.Make(raw, path, heading, LinkStatus.Broken);
}
