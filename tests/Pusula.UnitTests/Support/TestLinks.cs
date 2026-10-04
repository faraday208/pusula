using Xunit;

namespace Pusula.UnitTests.Support;

/// <summary>Symbolic links for the tests, made where they can be made: where they cannot (Windows without the right to make them), the test is skipped.</summary>
internal static class TestLinks
{
    // A folder or a file that is not there is a mistake of the test, and not a reason to skip it.
    private static bool CannotMakeLinks(Exception exception) =>
        exception is not (DirectoryNotFoundException or FileNotFoundException) && exception is IOException or UnauthorizedAccessException or PlatformNotSupportedException;

    /// <summary>Makes a link to a file (or to whatever the text names; it is not looked at).</summary>
    /// <param name="link">Full path of the link.</param>
    /// <param name="target">What the link says, as it is written.</param>
    public static void ToFile(string link, string target)
    {
        try
        {
            File.CreateSymbolicLink(link, target);
        }
        catch (Exception exception) when (CannotMakeLinks(exception))
        {
            Assert.Skip($"Symbolic links are not available here: {exception.Message}");
        }
    }

    /// <summary>Makes a link to a folder (or to whatever the text names; it is not looked at).</summary>
    /// <param name="link">Full path of the link.</param>
    /// <param name="target">What the link says, as it is written.</param>
    public static void ToFolder(string link, string target)
    {
        try
        {
            Directory.CreateSymbolicLink(link, target);
        }
        catch (Exception exception) when (CannotMakeLinks(exception))
        {
            Assert.Skip($"Symbolic links are not available here: {exception.Message}");
        }
    }
}
