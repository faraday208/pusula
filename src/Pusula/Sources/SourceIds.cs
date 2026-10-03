using System.Text;
using System.Text.RegularExpressions;

namespace Pusula.Sources;

/// <summary>The ids and names of sources.</summary>
internal static partial class SourceIds
{
    /// <summary>The most characters an id that is derived from a name has.</summary>
    internal const int MaxLength = 40;

    /// <summary>The id of a name that has no letter or digit.</summary>
    internal const string FallbackId = "source";

    // Lowercase letters and digits, joined by single hyphens.
    [GeneratedRegex("""^[a-z0-9]+(-[a-z0-9]+)*$""", RegexOptions.None, matchTimeoutMilliseconds: 1000)]
    private static partial Regex ValidId();

    /// <summary>Whether <paramref name="id"/> has the form of an id.</summary>
    /// <param name="id">The id.</param>
    public static bool IsValid(string id) => ValidId().IsMatch(id);

    /// <summary>The name of a folder: its last segment, or the whole path for a folder that has none (a root directory).</summary>
    /// <param name="fullPath">Full path of the folder.</param>
    public static string NameOf(string fullPath)
    {
        string name = Path.GetFileName(Path.TrimEndingDirectorySeparator(fullPath));
        return name.Length > 0 ? name : fullPath;
    }

    /// <summary>
    /// An id from a name: lowercase, the Turkish letters as ASCII (ı→i ş→s ğ→g ü→u ö→o ç→c), everything that is not
    /// a letter or digit as a hyphen, runs of hyphens as one, no hyphen at either end, at most <see cref="MaxLength"/>
    /// characters; <see cref="FallbackId"/> when nothing is left.
    /// </summary>
    /// <param name="name">The name.</param>
    public static string Derive(string name)
    {
        var id = new StringBuilder(name.Length);
        foreach (char letter in name)
        {
            char c = AsAscii(letter);
            if (c is (>= 'a' and <= 'z') or (>= '0' and <= '9'))
            {
                id.Append(c);
            }
            else if (id.Length > 0 && id[^1] != '-')
            {
                id.Append('-');
            }
        }

        string derived = id.ToString().TrimEnd('-');
        if (derived.Length > MaxLength)
        {
            derived = derived[..MaxLength].TrimEnd('-');
        }

        return derived.Length == 0 ? FallbackId : derived;
    }

    /// <summary>
    /// <paramref name="id"/> if it is not taken yet, otherwise <c>id-2</c>, <c>id-3</c>, ... (cut so that the whole stays
    /// within <see cref="MaxLength"/>); the id that is returned is added to <paramref name="taken"/>.
    /// </summary>
    /// <param name="id">The id that is wanted.</param>
    /// <param name="taken">The ids of the other sources.</param>
    public static string MakeUnique(string id, ISet<string> taken)
    {
        if (taken.Add(id))
        {
            return id;
        }

        for (int number = 2; ; number++)
        {
            string suffix = "-" + number.ToString(System.Globalization.CultureInfo.InvariantCulture);
            string stem = id.Length + suffix.Length > MaxLength ? id[..(MaxLength - suffix.Length)].TrimEnd('-') : id;
            if (taken.Add(stem + suffix))
            {
                return stem + suffix;
            }
        }
    }

    private static char AsAscii(char letter) => letter switch
    {
        'ı' or 'İ' or 'I' => 'i',
        'ş' or 'Ş' => 's',
        'ğ' or 'Ğ' => 'g',
        'ü' or 'Ü' => 'u',
        'ö' or 'Ö' => 'o',
        'ç' or 'Ç' => 'c',
        _ => char.ToLowerInvariant(letter),
    };
}
