using System.Diagnostics.CodeAnalysis;

namespace Pusula.Links;

/// <summary>Turns <see cref="RawLink"/>s into <see cref="Link"/>s by resolving them against the index and the file system.</summary>
internal sealed class LinkResolver
{
    private const string ClaudeRootPrefix = "~/.claude/";

    private readonly LinkResolutionContext _context;
    private readonly string _normalizedRoot;

    /// <summary>Creates a resolver over the given tree description.</summary>
    public LinkResolver(LinkResolutionContext context)
    {
        _context = context;
        _normalizedRoot = NormalizeRoot(context.RootFullPath);
    }

    /// <summary>
    /// Resolves one reference found in <paramref name="sourcePath"/>. Returns null for a relative-path code span
    /// that does not look like a path into this tree (for example <c>bin/obj</c> or an example path in a code sample).
    /// </summary>
    /// <param name="raw">The reference.</param>
    /// <param name="sourcePath">Index key of the file that contains the reference.</param>
    public Link? Resolve(RawLink raw, string sourcePath) => raw.Kind switch
    {
        LinkKind.MarkdownLink => ResolveMarkdownLink(raw, sourcePath),
        LinkKind.WikiLink or LinkKind.Embed => ResolveWikiLink(raw, sourcePath),
        LinkKind.ClaudePath => ResolveClaudePath(raw),
        LinkKind.RelativePath => ResolveRelativePath(raw, sourcePath),
        _ => throw new ArgumentOutOfRangeException(nameof(raw), raw.Kind, "Unknown link kind."),
    };

    /// <summary>
    /// Security-critical path normalization. Resolves <paramref name="relative"/> against
    /// <paramref name="baseDirectory"/> lexically: empty and <c>.</c> segments are dropped and <c>..</c> removes the
    /// previous segment. Returns false when the path would leave the root or contains <c>\</c> or NUL.
    /// </summary>
    /// <param name="baseDirectory">Normalized root-relative directory (empty for the root).</param>
    /// <param name="relative">The path to resolve; separators are <c>/</c> only.</param>
    /// <param name="normalized">Root-relative path without a leading or trailing <c>/</c>; empty for the root itself.</param>
    internal static bool TryNormalize(string baseDirectory, string relative, [NotNullWhen(true)] out string? normalized)
    {
        normalized = null;
        if (relative.Contains('\\') || relative.Contains('\0'))
        {
            return false;
        }

        var segments = new List<string>();
        if (!Apply(segments, baseDirectory) || !Apply(segments, relative))
        {
            return false;
        }

        normalized = string.Join('/', segments);
        return true;

        static bool Apply(List<string> segments, string path)
        {
            foreach (string segment in path.Split('/'))
            {
                if (segment.Length == 0 || segment == ".")
                {
                    continue;
                }

                if (segment != "..")
                {
                    segments.Add(segment);
                }
                else if (segments.Count > 0)
                {
                    segments.RemoveAt(segments.Count - 1);
                }
                else
                {
                    return false;
                }
            }

            return true;
        }
    }

    /// <summary>The full path of a root as a normalized, <c>/</c>-separated path without a leading or trailing <c>/</c>.</summary>
    /// <param name="rootFullPath">Full path of the root folder.</param>
    internal static string NormalizeRoot(string rootFullPath)
    {
        string root = rootFullPath.Replace('\\', '/');
        return TryNormalize(string.Empty, root, out string? normalized) ? normalized : root.Trim('/');
    }

    /// <summary>Splits the inner text of a wiki link or embed, <c>target#heading|alias</c>, into the target and the heading.</summary>
    /// <param name="inner">The text between the brackets.</param>
    internal static (string Target, string? Heading) SplitWikiLink(string inner)
    {
        int pipe = inner.IndexOf('|');
        if (pipe >= 0)
        {
            inner = inner[..pipe];
        }

        string? heading = null;
        int hash = inner.IndexOf('#');
        if (hash >= 0)
        {
            heading = inner[(hash + 1)..].Trim();
            if (heading.Length == 0)
            {
                heading = null;
            }

            inner = inner[..hash];
        }

        return (inner.Trim(), heading);
    }

    /// <summary>
    /// An absolute path as a root-relative one, when it lies under the root (or is the root): false when it lies
    /// elsewhere or cannot be normalized.
    /// </summary>
    /// <param name="normalizedRoot">The root, as <see cref="NormalizeRoot"/> returns it.</param>
    /// <param name="absolutePath">The absolute path, separated by <c>/</c>.</param>
    /// <param name="comparison">How the root and the path are compared.</param>
    /// <param name="relative">Root-relative path; empty for the root itself.</param>
    internal static bool TryGetRootRelative(string normalizedRoot, string absolutePath, StringComparison comparison, [NotNullWhen(true)] out string? relative)
    {
        relative = null;
        if (!TryNormalize(string.Empty, absolutePath, out string? normalized))
        {
            return false;
        }

        if (normalizedRoot.Length == 0)
        {
            relative = normalized;
            return true;
        }

        if (string.Equals(normalized, normalizedRoot, comparison))
        {
            relative = string.Empty;
            return true;
        }

        if (normalized.StartsWith(normalizedRoot + "/", comparison))
        {
            relative = normalized[(normalizedRoot.Length + 1)..];
            return true;
        }

        return false;
    }

    private Link ResolveMarkdownLink(RawLink raw, string sourcePath)
    {
        string destination = raw.Raw;
        if (destination.Length >= 2 && destination[0] == '<' && destination[^1] == '>')
        {
            destination = destination[1..^1];
        }

        destination = StripFileScheme(destination);

        string? heading = null;
        int hash = destination.IndexOf('#');
        if (hash >= 0)
        {
            heading = Decode(destination[(hash + 1)..]);
            destination = destination[..hash];
        }

        destination = Decode(destination);
        if (heading is { Length: 0 })
        {
            heading = null;
        }

        if (destination.Length == 0)
        {
            return Make(raw, sourcePath, heading, LinkStatus.Resolved);
        }

        string? normalized;
        if (destination.StartsWith(ClaudeRootPrefix, StringComparison.Ordinal))
        {
            if (!TryNormalize(string.Empty, destination[ClaudeRootPrefix.Length..], out normalized))
            {
                return External(raw);
            }
        }
        else if (destination.StartsWith("~/", StringComparison.Ordinal))
        {
            return External(raw);
        }
        else if (TryGetAbsolutePath(destination, out string? absolute))
        {
            if (!TryGetRootRelative(absolute, out normalized))
            {
                return External(raw);
            }
        }
        else if (!TryNormalize(DirectoryOf(sourcePath), destination, out normalized))
        {
            return External(raw);
        }

        return FromNormalized(raw, normalized, heading);
    }

    private Link ResolveWikiLink(RawLink raw, string sourcePath)
    {
        (string target, string? heading) = SplitWikiLink(raw.Raw);
        if (target.Length == 0)
        {
            return Make(raw, sourcePath, heading, LinkStatus.Resolved);
        }

        bool inMemory = MemoryDirectory.TryGetFor(sourcePath, _context.PathComparison, out string? memoryDirectory);
        if (inMemory
            && _context.MemoryNames.TryGetValue(memoryDirectory!, out IReadOnlyDictionary<string, string>? names)
            && names.TryGetValue(target, out string? namedPath))
        {
            return Make(raw, namedPath, heading, LinkStatus.Resolved);
        }

        if (!TryNormalize(DirectoryOf(sourcePath), target, out string? stem))
        {
            return External(raw);
        }

        string[] candidates = [stem, stem + ".md"];
        foreach (string candidate in candidates)
        {
            if (_context.IndexedPaths.TryGetValue(candidate, out string? indexed))
            {
                return Make(raw, indexed, heading, LinkStatus.Resolved);
            }
        }

        foreach (string candidate in candidates)
        {
            if (candidate.Length > 0 && _context.PathExists(candidate))
            {
                return Make(raw, candidate, heading, LinkStatus.NonMarkdown);
            }
        }

        return Make(raw, null, heading, inMemory ? LinkStatus.Pending : LinkStatus.Broken);
    }

    private Link ResolveClaudePath(RawLink raw)
    {
        if (!raw.Raw.StartsWith(ClaudeRootPrefix, StringComparison.Ordinal)
            || !TryNormalize(string.Empty, raw.Raw[ClaudeRootPrefix.Length..], out string? normalized))
        {
            return External(raw);
        }

        return FromNormalized(raw, normalized, heading: null);
    }

    private Link? ResolveRelativePath(RawLink raw, string sourcePath)
    {
        // A name without an extension that does not end in '/' is neither a file nor a written-out directory
        // ("bin/obj", "src/app"): most likely a code example, so it is not a reference and is never probed.
        if (!HasExtensionOrTrailingSlash(raw.Raw))
        {
            return null;
        }

        string path = raw.Raw.StartsWith("./", StringComparison.Ordinal) ? raw.Raw[2..] : raw.Raw;
        string directory = DirectoryOf(sourcePath);

        foreach (string baseDirectory in SelfAndAncestors(directory))
        {
            if (!TryNormalize(baseDirectory, path, out string? candidate))
            {
                continue;
            }

            if (_context.IndexedPaths.TryGetValue(candidate, out string? indexed))
            {
                return Make(raw, indexed, heading: null, LinkStatus.Resolved);
            }

            if (candidate.Length > 0 && _context.PathExists(candidate))
            {
                return Make(raw, candidate, heading: null, LinkStatus.NonMarkdown);
            }
        }

        // Nothing exists. Only paths that look like they were meant for this tree count as broken links;
        // anything else is most likely an example path in prose or a code sample.
        if (!TryNormalize(string.Empty, path, out string? rootRelative))
        {
            return null;
        }

        int slash = path.IndexOf('/');
        string first = slash < 0 ? path : path[..slash];
        return IsKnownDirectory(first, directory) ? Make(raw, rootRelative, heading: null, LinkStatus.Broken) : null;
    }

    // The last segment has an extension when it contains a '.'; a path that ends in '/' names a directory outright.
    private static bool HasExtensionOrTrailingSlash(string relativePath)
    {
        if (relativePath.EndsWith('/'))
        {
            return true;
        }

        return relativePath[(relativePath.LastIndexOf('/') + 1)..].Contains('.');
    }

    private bool IsKnownDirectory(string name, string sourceDirectory)
    {
        if (name.Length == 0)
        {
            return false;
        }

        if (_context.TopLevelDirectories.Contains(name))
        {
            return true;
        }

        foreach (string baseDirectory in SelfAndAncestors(sourceDirectory))
        {
            if (TryNormalize(baseDirectory, name, out string? candidate) && candidate.Length > 0 && _context.PathExists(candidate))
            {
                return true;
            }
        }

        return false;
    }

    private Link FromNormalized(RawLink raw, string normalized, string? heading)
    {
        if (normalized.Length == 0)
        {
            // The root directory itself: it exists but is not a file.
            return Make(raw, null, heading, LinkStatus.NonMarkdown);
        }

        if (_context.IndexedPaths.TryGetValue(normalized, out string? indexed))
        {
            return Make(raw, indexed, heading, LinkStatus.Resolved);
        }

        return Make(raw, normalized, heading, _context.PathExists(normalized) ? LinkStatus.NonMarkdown : LinkStatus.Broken);
    }

    // An absolute path is a link into the tree only when it lies under the root.
    private bool TryGetRootRelative(string absolutePath, [NotNullWhen(true)] out string? relative) =>
        TryGetRootRelative(_normalizedRoot, absolutePath, _context.PathComparison, out relative);

    internal static bool TryGetAbsolutePath(string path, [NotNullWhen(true)] out string? absolute)
    {
        // "/C:/dir" is what remains of "file:///C:/dir"; it is a Windows drive path.
        if (path.Length >= 4 && path[0] == '/' && char.IsAsciiLetter(path[1]) && path[2] == ':' && path[3] == '/')
        {
            absolute = path[1..];
            return true;
        }

        if (path[0] == '/' || (path.Length >= 3 && char.IsAsciiLetter(path[0]) && path[1] == ':' && path[2] == '/'))
        {
            absolute = path;
            return true;
        }

        absolute = null;
        return false;
    }

    internal static string StripFileScheme(string destination)
    {
        if (!destination.StartsWith("file:", StringComparison.OrdinalIgnoreCase))
        {
            return destination;
        }

        string rest = destination["file:".Length..];
        return rest.StartsWith("//", StringComparison.Ordinal) ? rest[2..] : rest;
    }

    // Uri.UnescapeDataString never throws; malformed escapes are left as they are, which is the "raw on failure" rule.
    internal static string Decode(string value) => Uri.UnescapeDataString(value);

    internal static string DirectoryOf(string path)
    {
        int slash = path.LastIndexOf('/');
        return slash < 0 ? string.Empty : path[..slash];
    }

    private static IEnumerable<string> SelfAndAncestors(string directory)
    {
        string current = directory;
        while (true)
        {
            yield return current;
            if (current.Length == 0)
            {
                yield break;
            }

            int slash = current.LastIndexOf('/');
            current = slash < 0 ? string.Empty : current[..slash];
        }
    }

    internal static Link Make(RawLink raw, string? target, string? heading, LinkStatus status) =>
        new(raw.Kind, raw.Raw, raw.Line, target, heading, status);

    internal static Link External(RawLink raw) => Make(raw, target: null, heading: null, LinkStatus.External);
}
