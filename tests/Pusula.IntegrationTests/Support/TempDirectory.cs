using System.Text;

namespace Pusula.IntegrationTests.Support;

/// <summary>A uniquely named scratch directory under the system temp folder; deleted on dispose.</summary>
internal sealed class TempDirectory : IDisposable
{
    public TempDirectory()
    {
        Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "pusula-it-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path);
    }

    public string Path { get; }

    /// <summary>Full path of a <c>/</c>-separated path relative to this directory.</summary>
    public string Resolve(string relativePath) =>
        System.IO.Path.Combine(Path, relativePath.Replace('/', System.IO.Path.DirectorySeparatorChar));

    /// <summary>Writes a UTF-8 text file (without BOM), creating parent directories.</summary>
    public string Write(string relativePath, string content)
    {
        string fullPath = Resolve(relativePath);
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(fullPath)!);
        File.WriteAllText(fullPath, content, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        return fullPath;
    }

    /// <summary>Creates a directory (and the ones above it) and gives its full path.</summary>
    public string CreateDirectory(string relativePath)
    {
        string fullPath = Resolve(relativePath);
        Directory.CreateDirectory(fullPath);
        return fullPath;
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(Path, recursive: true);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // Best effort: a leftover scratch directory must not fail a test run.
        }
    }
}
