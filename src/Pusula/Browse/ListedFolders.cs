using Pusula.Sources;

namespace Pusula.Browse;

/// <summary>The folders that the sources show at this moment: what tells a page which folders of a listing it has added already.</summary>
internal sealed class ListedFolders
{
    private readonly string[] _paths;

    /// <summary>Takes the folders of the sources as they are now.</summary>
    /// <param name="registry">The sources.</param>
    public ListedFolders(ISourceRegistry registry)
    {
        _paths = [.. registry.Sources.Select(source => source.Definition.Path)];
    }

    /// <summary>No source shows anything: nothing is listed.</summary>
    public static ListedFolders None { get; } = new(Array.Empty<string>());

    private ListedFolders(string[] paths)
    {
        _paths = paths;
    }

    /// <summary>Whether a source shows this very folder: the same full path, compared the way the platform compares paths.</summary>
    /// <param name="fullPath">Full path of the folder.</param>
    public bool Contains(string fullPath) => Array.Exists(_paths, path => SourcePaths.Same(path, fullPath));
}
