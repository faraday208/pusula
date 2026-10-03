using System.Text;

namespace Pusula.Sources;

/// <summary>
/// Writes a sources file so that nobody ever reads half of it: the text goes to a new file in the same folder, and
/// that file then replaces the old one in one step.
/// </summary>
internal static class SourcesFileWriter
{
    private static readonly UTF8Encoding Utf8 = new(encoderShouldEmitUTF8Identifier: false);

    /// <summary>
    /// Replaces the file with <paramref name="text"/> (UTF-8, no byte order mark), creating the folder when it does
    /// not exist. A file that is a link is written through, so that the link stays a link. Nothing is left behind when
    /// writing fails.
    /// </summary>
    /// <param name="file">Full path of the sources file.</param>
    /// <param name="text">What the file says from now on.</param>
    /// <exception cref="IOException">The file or its folder cannot be written.</exception>
    /// <exception cref="UnauthorizedAccessException">The file or its folder may not be written.</exception>
    public static void Write(string file, string text)
    {
        var info = new FileInfo(file);
        string target = info.LinkTarget is null ? file : info.ResolveLinkTarget(returnFinalTarget: true)?.FullName ?? file;
        string directory = Path.GetDirectoryName(target) ?? throw new IOException($"'{target}' is not a file in a folder.");
        Directory.CreateDirectory(directory);

        string temporary = Path.Join(directory, "." + Path.GetFileName(target) + "." + Guid.NewGuid().ToString("N") + ".tmp");
        try
        {
            File.WriteAllText(temporary, text, Utf8);
            File.Move(temporary, target, overwrite: true);
        }
        catch
        {
            TryDelete(temporary);
            throw;
        }
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
}
