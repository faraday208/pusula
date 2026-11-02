using System.Diagnostics.CodeAnalysis;

namespace Pusula.Updates;

/// <summary>
/// The tag of a release of pusula, <c>v0.4.0</c> or <c>v0.4.0-rc.1</c>: the only thing that is taken from the answer of GitHub. The page of the
/// release is made from the tag, never taken from the answer.
/// </summary>
internal sealed class ReleaseTag
{
    /// <summary>Where the page of a release is: this and the tag.</summary>
    internal const string PageAddress = "https://github.com/faraday208/pusula/releases/tag/";

    private const int MaxLength = 64;

    private ReleaseTag(string text, SemanticVersion version)
    {
        Text = text;
        Version = version;
    }

    /// <summary>The tag as written, with its <c>v</c>.</summary>
    public string Text { get; }

    /// <summary>The version of the tag, without the <c>v</c>.</summary>
    public SemanticVersion Version { get; }

    /// <summary>The page of the release on GitHub.</summary>
    public string Url => PageAddress + Text;

    /// <summary>Reads a tag: a <c>v</c>, then a version (<c>^v(\d+)\.(\d+)\.(\d+)(-[0-9A-Za-z-]+(\.[0-9A-Za-z-]+)*)?$</c>, ASCII digits only), at most 64 characters.</summary>
    /// <param name="text">The text of <c>tag_name</c>.</param>
    /// <param name="tag">The tag; null when the text is not one.</param>
    public static bool TryParse(string? text, [NotNullWhen(true)] out ReleaseTag? tag)
    {
        if (text is not null && text.Length <= MaxLength && text.StartsWith('v') && SemanticVersion.TryParse(text[1..], out SemanticVersion? version))
        {
            tag = new ReleaseTag(text, version);
            return true;
        }

        tag = null;
        return false;
    }
}
