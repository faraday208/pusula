using System.Collections.Immutable;
using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Pusula.Links;

namespace Pusula.Indexing;

/// <summary>
/// Scans a folder (by default a Claude Code configuration folder such as <c>~/.claude</c>) and builds an immutable
/// <see cref="ConfigIndex"/>. The folder is only ever read. A <see cref="SourceProfile"/> decides what is scanned
/// and how links are resolved: the Claude profile knows load layers and memory names, the other profiles treat every
/// Markdown file as a note. The scan stops, with a <see cref="FolderTooLargeException"/>, at a folder that has more
/// Markdown files, more folders or more Markdown than <see cref="Limits"/> allow.
/// </summary>
internal sealed partial class IndexBuilder
{
    /// <summary>Markdown files larger than this many bytes (2 MiB) are not indexed.</summary>
    internal const long MaxFileBytes = 2L * 1024 * 1024;

    /// <summary>The one directory name that a folder of notes does not enter (besides the hidden ones).</summary>
    internal const string NodeModulesDirectory = "node_modules";

    private static readonly string[] SkippedTopLevelDirectories =
    [
        "plugins", "cache", "file-history", "sessions", "debug", "backups", "paste-cache", "chrome", "ide", "jobs",
        "daemon", "downloads", "session-env", "security", "usage-data", "todos", "shell-snapshots", "statsig",
        "telemetry", "state",
    ];

    private static readonly EnumerationOptions ListingOptions = new()
    {
        AttributesToSkip = 0,
        IgnoreInaccessible = true,
        RecurseSubdirectories = false,
        ReturnSpecialDirectories = false,
    };

    private readonly ILogger<IndexBuilder> _logger;
    private readonly StringComparison _comparison;
    private readonly StringComparer _comparer;

    /// <summary>Creates a builder that compares paths the way the current platform does.</summary>
    public IndexBuilder(ILogger<IndexBuilder> logger)
        : this(logger, PathComparison.Current)
    {
    }

    /// <summary>Creates a builder with an explicit path comparison (used by tests to exercise the Windows policy).</summary>
    internal IndexBuilder(ILogger<IndexBuilder> logger, StringComparison pathComparison)
    {
        _logger = logger;
        _comparison = pathComparison;
        _comparer = StringComparer.FromComparison(pathComparison);
    }

    /// <summary>
    /// The expressions that the links of every file are extracted with; null selects the application's own. Tests
    /// give a set with a short timeout to exercise the timeout without waiting for it.
    /// </summary>
    internal LinkExtractor.PatternSet? LinkPatterns { get; init; }

    /// <summary>
    /// How much of a folder is scanned before the scan gives up; <see cref="ScanLimits.Default"/> in the application.
    /// Tests give small limits, to exercise them without making a folder of 20,000 files.
    /// </summary>
    internal ScanLimits Limits { get; init; } = ScanLimits.Default;

    /// <summary>
    /// True for the runtime directories directly under the root that the scan never enters
    /// (<c>plugins</c>, <c>cache</c>, <c>sessions</c>, ...).
    /// </summary>
    /// <param name="name">A directory name directly under the root.</param>
    /// <param name="comparison">How names are compared.</param>
    internal static bool IsSkippedTopLevelDirectory(string name, StringComparison comparison) =>
        Array.Exists(SkippedTopLevelDirectories, skipped => string.Equals(skipped, name, comparison));

    /// <summary>Scans <paramref name="root"/> as a Claude Code configuration folder and builds the index (<see cref="ConfigIndex.Version"/> is 0).</summary>
    /// <param name="root">The configuration folder.</param>
    /// <exception cref="DirectoryNotFoundException">The folder does not exist.</exception>
    public ConfigIndex Build(string root) => Build(root, SourceProfile.Claude);

    /// <summary>Scans <paramref name="root"/> the way <paramref name="profile"/> says and builds the index (<see cref="ConfigIndex.Version"/> is 0).</summary>
    /// <param name="root">The folder.</param>
    /// <param name="profile">What kind of folder it is.</param>
    /// <exception cref="DirectoryNotFoundException">The folder does not exist.</exception>
    /// <exception cref="FolderTooLargeException">The folder is beyond <see cref="Limits"/>; nothing is built.</exception>
    public ConfigIndex Build(string root, SourceProfile profile)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(root);

        bool notes = profile != SourceProfile.Claude;
        long started = Stopwatch.GetTimestamp();
        string fullRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root));
        if (!Directory.Exists(fullRoot))
        {
            throw new DirectoryNotFoundException($"The configuration root '{fullRoot}' does not exist or is not a directory.");
        }

        string? outputStyle = notes ? null : ReadOutputStyle(fullRoot);

        var scan = new Scan(_comparer, fullRoot);
        string rootRealPath = new DirectoryInfo(fullRoot).ResolveLinkTarget(returnFinalTarget: true)?.FullName ?? fullRoot;
        scan.Visiting.Add(rootRealPath);
        Walk(new DirectoryInfo(fullRoot), string.Empty, rootRealPath, notes ? WalkMode.Notes : WalkMode.Root, scan);

        scan.Files.Sort((left, right) => _comparer.Compare(left.Path, right.Path));

        var parsed = new List<ParsedFile>(scan.Files.Count);
        foreach (ScannedFile file in scan.Files)
        {
            FrontmatterResult frontmatter = FrontmatterParser.Parse(file.Content);
            (Layer layer, LoadMode loadMode) = notes
                ? (Layer.Note, LoadMode.OnDemand)
                : ClaudeLayout.Classify(file.Path, frontmatter.Frontmatter, outputStyle, _comparison);
            parsed.Add(new ParsedFile(
                file.Path,
                file.Content,
                file.ModifiedAt,
                frontmatter,
                layer,
                loadMode,
                ExtractLinks(file.Path, frontmatter, notes),
                notes ? ExtractTags(file.Path, frontmatter) : []));
        }

        Func<RawLink, string, Link?> resolve = notes
            ? new NoteLinkResolver(new NoteLinkContext(fullRoot, [.. parsed.Select(file => file.Path)], scan.AllFiles)).Resolve
            : new LinkResolver(new LinkResolutionContext(
                fullRoot,
                _comparison,
                new HashSet<string>(parsed.Select(file => file.Path), _comparer),
                BuildMemoryNames(parsed),
                scan.TopLevelDirectories,
                relativePath => PathExists(fullRoot, relativePath))).Resolve;

        var backlinks = new Dictionary<string, List<Backlink>>(_comparer);
        var linkingOut = new HashSet<string>(_comparer);
        var resolvedLinks = new List<IReadOnlyList<Link>>(parsed.Count);
        foreach (ParsedFile file in parsed)
        {
            var links = new List<Link>(file.RawLinks.Count);
            foreach (RawLink raw in file.RawLinks)
            {
                Link? link = resolve(raw, file.Path);
                if (link is null)
                {
                    continue;
                }

                links.Add(link);
                if (link.Status == LinkStatus.Resolved && !string.Equals(link.Target, file.Path, _comparison))
                {
                    if (!backlinks.TryGetValue(link.Target!, out List<Backlink>? incoming))
                    {
                        backlinks[link.Target!] = incoming = [];
                    }

                    incoming.Add(new Backlink(file.Path, link.Kind, link.Line));
                    linkingOut.Add(file.Path);
                }
            }

            resolvedLinks.Add(links);
        }

        ImmutableSortedDictionary<string, ConfigFile>.Builder files = ImmutableSortedDictionary.CreateBuilder<string, ConfigFile>(_comparer);
        for (int i = 0; i < parsed.Count; i++)
        {
            ParsedFile file = parsed[i];
            files[file.Path] = new ConfigFile(
                file.Path,
                file.Path[(file.Path.LastIndexOf('/') + 1)..],
                file.Layer,
                file.LoadMode,
                TokenEstimator.Count(file.Path, file.Content, file.Layer, file.LoadMode, file.Frontmatter.Frontmatter),
                file.Frontmatter.Frontmatter,
                file.Frontmatter.Error,
                file.Frontmatter.ErrorLine,
                file.Content,
                file.Frontmatter.Body,
                file.Frontmatter.BodyStartLine,
                resolvedLinks[i],
                IsOrphan(file.Layer, linkedTo: backlinks.ContainsKey(file.Path), linksOut: notes && linkingOut.Contains(file.Path)),
                file.ModifiedAt)
            {
                Tags = file.Tags,
            };
        }

        var index = new ConfigIndex(
            fullRoot,
            DateTimeOffset.UtcNow,
            outputStyle,
            files.ToImmutable(),
            backlinks.ToImmutableSortedDictionary(
                entry => entry.Key,
                entry => (IReadOnlyList<Backlink>)entry.Value.ToArray(),
                _comparer),
            Profile: profile);

        TimeSpan elapsed = Stopwatch.GetElapsedTime(started);
        LogBuilt(parsed.Count, fullRoot, elapsed.TotalMilliseconds);
        return index;
    }

    private void Walk(DirectoryInfo directory, string relativeDirectory, string realPath, WalkMode mode, Scan scan)
    {
        FileSystemInfo[] entries;
        try
        {
            entries = directory.GetFileSystemInfos("*", ListingOptions);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            LogSkipped(relativeDirectory.Length == 0 ? "." : relativeDirectory, exception.Message);
            return;
        }

        foreach (FileSystemInfo entry in entries)
        {
            string name = entry.Name;
            if (mode == WalkMode.Root && entry is DirectoryInfo)
            {
                scan.TopLevelDirectories.Add(name);
            }

            if (name.StartsWith('.'))
            {
                continue;
            }

            string relativePath = relativeDirectory.Length == 0 ? name : $"{relativeDirectory}/{name}";
            if (entry is DirectoryInfo child)
            {
                WalkMode? childMode = ChildMode(mode, name);
                if (childMode is not null)
                {
                    WalkChild(child, relativePath, realPath, childMode.Value, scan);
                }
            }
            else if (mode == WalkMode.Notes)
            {
                // Every file counts as something a link can point at; only Markdown files are notes.
                scan.AllFiles.Add(relativePath);
                if (name.EndsWith(".md", StringComparison.OrdinalIgnoreCase) && entry is FileInfo note)
                {
                    ReadFile(note, relativePath, scan);
                }
            }
            else if (mode is WalkMode.Root or WalkMode.Normal
                && name.EndsWith(".md", StringComparison.OrdinalIgnoreCase)
                && entry is FileInfo file)
            {
                ReadFile(file, relativePath, scan);
            }
        }
    }

    private void WalkChild(DirectoryInfo child, string relativePath, string parentRealPath, WalkMode mode, Scan scan)
    {
        string childRealPath;
        try
        {
            // A symbolic link is followed, but its resolved target is remembered so that a link back to a
            // directory we are already inside does not loop.
            childRealPath = child.LinkTarget is null
                ? Path.Join(parentRealPath, child.Name)
                : child.ResolveLinkTarget(returnFinalTarget: true)?.FullName ?? child.FullName;
        }
        catch (IOException exception)
        {
            LogSkipped(relativePath, exception.Message);
            return;
        }

        if (!scan.Visiting.Add(childRealPath))
        {
            LogLoop(relativePath);
            return;
        }

        try
        {
            if (++scan.Directories > Limits.MaxDirectories)
            {
                throw FolderTooLargeException.ForDirectories(scan.Root, Limits.MaxDirectories);
            }

            Walk(child, relativePath, childRealPath, mode, scan);
        }
        finally
        {
            scan.Visiting.Remove(childRealPath);
        }
    }

    // Which directories the scan enters, and how, depending on where it currently is.
    private WalkMode? ChildMode(WalkMode mode, string name) => mode switch
    {
        WalkMode.Root when IsSkippedTopLevelDirectory(name, _comparison) => null,
        WalkMode.Root when string.Equals(name, "projects", _comparison) => WalkMode.Projects,
        WalkMode.Root or WalkMode.Normal => WalkMode.Normal,

        // A folder of notes is scanned in full, except for the dependencies of a project that lives inside it.
        WalkMode.Notes when string.Equals(name, NodeModulesDirectory, _comparison) => null,
        WalkMode.Notes => WalkMode.Notes,

        // projects/<project>/: only the memory directory holds configuration; the rest is transcripts.
        WalkMode.Projects => WalkMode.ProjectDirectory,
        WalkMode.ProjectDirectory when string.Equals(name, "memory", _comparison) => WalkMode.Normal,
        _ => null,
    };

    private void ReadFile(FileInfo file, string relativePath, Scan scan)
    {
        // Every Markdown file counts, the one that is too large to read too: what the limit is about is how many there are.
        if (++scan.MarkdownFiles > Limits.MaxFiles)
        {
            throw FolderTooLargeException.ForFiles(scan.Root, Limits.MaxFiles);
        }

        try
        {
            using var stream = new FileStream(file.FullName, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            if (stream.Length > MaxFileBytes)
            {
                LogSkipped(relativePath, $"{stream.Length} bytes exceeds the {MaxFileBytes} byte limit");
                return;
            }

            // What is read counts; and it is counted before it is read, so that no more than the limit is ever held.
            scan.Bytes += stream.Length;
            if (scan.Bytes > Limits.MaxBytes)
            {
                throw FolderTooLargeException.ForBytes(scan.Root, Limits.MaxBytes);
            }

            // The reader drops a byte order mark; invalid UTF-8 becomes U+FFFD instead of failing.
            using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
            scan.Files.Add(new ScannedFile(relativePath, reader.ReadToEnd(), new DateTimeOffset(file.LastWriteTimeUtc)));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // A dangling link, a permission problem or a file that vanished while the tree was being read.
            LogSkipped(relativePath, exception.Message);
        }
    }

    // A body that makes a link pattern run out of time (a hostile or machine-generated file) is indexed without links
    // and with a warning; the rest of the index is built as usual.
    private IReadOnlyList<RawLink> ExtractLinks(string path, FrontmatterResult frontmatter, bool notes)
    {
        if (!LinkExtractor.TryExtract(frontmatter.Body, frontmatter.BodyStartLine, out IReadOnlyList<RawLink> links, LinkPatterns))
        {
            LogLinksSkipped(path);
        }

        // Paths such as ~/.claude/x.md or `rules/x.md` are references of a Claude Code folder, not of a folder of notes.
        return notes ? [.. links.Where(link => link.Kind is LinkKind.MarkdownLink or LinkKind.WikiLink or LinkKind.Embed)] : links;
    }

    // The tags of a note. A body that makes an expression run out of time keeps the tags found so far, with a warning.
    private IReadOnlyList<string> ExtractTags(string path, FrontmatterResult frontmatter)
    {
        if (!NoteTags.TryExtract(frontmatter.Frontmatter, frontmatter.Body, out IReadOnlyList<string> tags, LinkPatterns))
        {
            LogTagsSkipped(path);
        }

        return tags;
    }

    // From settings.json only the selected output style is read; a missing or malformed file is ignored.
    private string? ReadOutputStyle(string root)
    {
        string path = Path.Combine(root, "settings.json");
        if (!File.Exists(path))
        {
            return null;
        }

        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using JsonDocument document = JsonDocument.Parse(stream);
            return document.RootElement.ValueKind == JsonValueKind.Object
                && document.RootElement.TryGetProperty("outputStyle", out JsonElement value)
                && value.ValueKind == JsonValueKind.String
                && value.GetString() is { Length: > 0 } style
                    ? style
                    : null;
        }
        catch (Exception exception) when (exception is JsonException or IOException or UnauthorizedAccessException)
        {
            LogSkipped("settings.json", exception.Message);
            return null;
        }
    }

    // Per memory directory: frontmatter "name" -> path. Files are sorted, so the first file wins a duplicate name.
    private Dictionary<string, IReadOnlyDictionary<string, string>> BuildMemoryNames(List<ParsedFile> files)
    {
        var byDirectory = new Dictionary<string, Dictionary<string, string>>(_comparer);
        foreach (ParsedFile file in files)
        {
            string? name = FrontmatterValues.GetString(file.Frontmatter.Frontmatter, "name")?.Trim();
            if (string.IsNullOrEmpty(name) || !MemoryDirectory.TryGetFor(file.Path, _comparison, out string? directory))
            {
                continue;
            }

            if (!byDirectory.TryGetValue(directory, out Dictionary<string, string>? names))
            {
                byDirectory[directory] = names = new Dictionary<string, string>(StringComparer.Ordinal);
            }

            names.TryAdd(name, file.Path);
        }

        return byDirectory.ToDictionary(entry => entry.Key, entry => (IReadOnlyDictionary<string, string>)entry.Value, _comparer);
    }

    /// <summary>
    /// Existence probe for link targets. Callers pass normalized root-relative paths that stay inside the root.
    /// Path.Join (not Path.Combine) is used so that a rooted or drive-qualified value can never replace the root.
    /// </summary>
    internal static bool PathExists(string root, string relativePath)
    {
        string fullPath = Path.Join(root, relativePath);
        return File.Exists(fullPath) || Directory.Exists(fullPath);
    }

    // A file of a kind that should be linked to is an orphan when nothing links to it. A note (a vault or a folder of
    // Markdown) has to have no links at all: a note that links to another one, such as the entry note of a vault that
    // nothing links back to, is not lonely.
    private static bool IsOrphan(Layer layer, bool linkedTo, bool linksOut) =>
        IsOrphanCandidate(layer) && !linkedTo && !linksOut;

    private static bool IsOrphanCandidate(Layer layer) =>
        layer is Layer.Memory or Layer.Reference or Layer.Shared or Layer.SkillResource or Layer.Other or Layer.Note;

    [LoggerMessage(Level = LogLevel.Information, Message = "Indexed {FileCount} files under {Root} in {ElapsedMs:F1} ms")]
    private partial void LogBuilt(int fileCount, string root, double elapsedMs);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Skipped {Path}: {Reason}")]
    private partial void LogSkipped(string path, string reason);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Links of {Path} were not extracted: a link pattern did not finish in time (an unusually long or oddly shaped line)")]
    private partial void LogLinksSkipped(string path);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Tags of {Path} were not all extracted: a tag pattern did not finish in time (an unusually long or oddly shaped line)")]
    private partial void LogTagsSkipped(string path);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Skipped {Path}: symbolic link back to a directory that is already being scanned")]
    private partial void LogLoop(string path);

    private enum WalkMode
    {
        /// <summary>The configuration root.</summary>
        Root,

        /// <summary>Any directory that is scanned in full.</summary>
        Normal,

        /// <summary>The <c>projects</c> directory: only its project directories are entered.</summary>
        Projects,

        /// <summary><c>projects/&lt;project&gt;</c>: only its <c>memory</c> directory is entered.</summary>
        ProjectDirectory,

        /// <summary>Any directory of a folder of notes: scanned in full, and every file is remembered.</summary>
        Notes,
    }

    private sealed class Scan(StringComparer comparer, string root)
    {
        // Full path of the folder that is scanned, for the message of a folder that turns out to be too large.
        public string Root { get; } = root;

        // What the scan has come across so far, against the limits: Markdown files, folders entered, bytes read.
        public int MarkdownFiles;

        public int Directories;

        public long Bytes;

        public List<ScannedFile> Files { get; } = [];

        // Every directory name directly under the root, including the ones that are not scanned.
        public HashSet<string> TopLevelDirectories { get; } = new(comparer);

        // Resolved real paths of the directories the scan is currently inside of (cycle detection).
        public HashSet<string> Visiting { get; } = new(comparer);

        // Folder of notes: the relative path of every file that is not hidden, Markdown or not.
        public HashSet<string> AllFiles { get; } = new(comparer);
    }

    private readonly record struct ScannedFile(string Path, string Content, DateTimeOffset ModifiedAt);

    private sealed record ParsedFile(
        string Path,
        string Content,
        DateTimeOffset ModifiedAt,
        FrontmatterResult Frontmatter,
        Layer Layer,
        LoadMode LoadMode,
        IReadOnlyList<RawLink> RawLinks,
        IReadOnlyList<string> Tags);
}
