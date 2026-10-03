using Pusula.Indexing;
using Pusula.Links;

namespace Pusula.Tree;

/// <summary>Builds the <see cref="TreeResponse"/> for an index.</summary>
internal static class TreeMapper
{
    /// <summary>Maps the files of <paramref name="index"/> onto a tree of directories and files.</summary>
    /// <param name="index">The index snapshot.</param>
    public static TreeResponse ToResponse(ConfigIndex index)
    {
        var root = new DirectoryBuilder(string.Empty, string.Empty);
        foreach (ConfigFile file in index.Files.Values)
        {
            string[] parts = file.Path.Split('/');
            DirectoryBuilder directory = root;
            for (int i = 0; i < parts.Length - 1; i++)
            {
                directory = directory.Child(parts[i]);
            }

            directory.Files.Add(file);
        }

        return new TreeResponse(index.Root, index.Version, index.BuiltAt, root.ToNodes());
    }

    private static TreeFile ToFileNode(ConfigFile file) =>
        new(
            file.Name,
            file.Path,
            file.Tokens.Total,
            file.Tokens.EverySession,
            file.Layer,
            file.LoadMode,
            file.Links.Count(link => link.Status == LinkStatus.Broken),
            file.IsOrphan,
            file.Tags.Count > 0 ? file.Tags : null);

    private sealed class DirectoryBuilder(string name, string path)
    {
        private readonly Dictionary<string, DirectoryBuilder> _directories = new(StringComparer.FromComparison(PathComparison.Current));

        public string Name { get; } = name;

        public List<ConfigFile> Files { get; } = [];

        public DirectoryBuilder Child(string childName)
        {
            if (!_directories.TryGetValue(childName, out DirectoryBuilder? child))
            {
                _directories[childName] = child = new DirectoryBuilder(childName, path.Length == 0 ? childName : $"{path}/{childName}");
            }

            return child;
        }

        // Directories first, then files; each group by name, ignoring case (ordinal as the tie-breaker).
        public List<TreeNode> ToNodes() =>
        [
            .. _directories.Values
                .OrderBy(directory => directory.Name, StringComparer.OrdinalIgnoreCase)
                .ThenBy(directory => directory.Name, StringComparer.Ordinal)
                .Select(directory => directory.ToNode()),
            .. Files
                .OrderBy(file => file.Name, StringComparer.OrdinalIgnoreCase)
                .ThenBy(file => file.Name, StringComparer.Ordinal)
                .Select(ToFileNode),
        ];

        private TreeDirectory ToNode()
        {
            List<TreeNode> children = ToNodes();
            int fileCount = 0;
            int tokens = 0;
            int everySessionTokens = 0;
            foreach (TreeNode child in children)
            {
                fileCount += child is TreeDirectory directory ? directory.FileCount : 1;
                tokens += child.Tokens;
                everySessionTokens += child.EverySessionTokens;
            }

            return new TreeDirectory(Name, path, tokens, everySessionTokens, fileCount, children);
        }
    }
}
