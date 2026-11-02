using System.Diagnostics.CodeAnalysis;
using System.Text.RegularExpressions;
using Pusula.Startup;

namespace Pusula.Updates;

/// <summary>
/// A version as Semantic Versioning 2.0 writes it (<c>1.2.3</c>, <c>1.2.3-rc.1</c>), with the precedence the specification gives it. A version
/// with build metadata (<c>1.2.3+abc</c>) is not accepted: <see cref="AppVersion"/> leaves it off before this reads the version of pusula, and
/// our own tags have none. Zeros in front of a number (<c>01.2.3</c>) are accepted, which the specification forbids: they are harmless for our tags
/// and do not count in the comparison.
/// </summary>
internal sealed partial class SemanticVersion : IComparable<SemanticVersion>
{
    [GeneratedRegex(@"^([0-9]+)\.([0-9]+)\.([0-9]+)(?:-([0-9A-Za-z-]+(?:\.[0-9A-Za-z-]+)*))?\z")]
    private static partial Regex Shape { get; }

    private readonly string[] _numbers;
    private readonly string[] _preRelease;

    private SemanticVersion(string text, string[] numbers, string[] preRelease)
    {
        Text = text;
        _numbers = numbers;
        _preRelease = preRelease;
    }

    /// <summary>The version as it was written.</summary>
    public string Text { get; }

    /// <summary>Reads a version: three numbers, and after a <c>-</c> the identifiers of a pre-release, separated by dots.</summary>
    /// <param name="text">The text, such as <c>0.4.0-rc.1</c>.</param>
    /// <param name="version">The version; null when the text is not one.</param>
    public static bool TryParse(string? text, [NotNullWhen(true)] out SemanticVersion? version)
    {
        Match match = text is null ? Match.Empty : Shape.Match(text);
        if (!match.Success)
        {
            version = null;
            return false;
        }

        string[] numbers = [match.Groups[1].Value, match.Groups[2].Value, match.Groups[3].Value];
        string[] preRelease = match.Groups[4].Success ? match.Groups[4].Value.Split('.') : [];
        version = new SemanticVersion(text!, numbers, preRelease);
        return true;
    }

    /// <summary>
    /// Compares by precedence: the three numbers as numbers; a pre-release is lower than the release; then identifier by identifier, numbers as
    /// numbers, words in ASCII order, a number lower than a word; when all are equal, the version with more identifiers is higher.
    /// </summary>
    /// <param name="other">The version to compare with.</param>
    public int CompareTo(SemanticVersion? other)
    {
        if (other is null)
        {
            return 1;
        }

        for (int i = 0; i < _numbers.Length; i++)
        {
            int byNumber = CompareNumbers(_numbers[i], other._numbers[i]);
            if (byNumber != 0)
            {
                return byNumber;
            }
        }

        if (_preRelease.Length == 0 || other._preRelease.Length == 0)
        {
            return other._preRelease.Length.CompareTo(_preRelease.Length);
        }

        for (int i = 0; i < Math.Min(_preRelease.Length, other._preRelease.Length); i++)
        {
            int byIdentifier = CompareIdentifiers(_preRelease[i], other._preRelease[i]);
            if (byIdentifier != 0)
            {
                return byIdentifier;
            }
        }

        return _preRelease.Length.CompareTo(other._preRelease.Length);
    }

    /// <inheritdoc />
    public override string ToString() => Text;

    private static int CompareIdentifiers(string a, string b)
    {
        bool numberA = IsNumber(a);
        bool numberB = IsNumber(b);
        if (numberA && numberB)
        {
            return CompareNumbers(a, b);
        }

        if (numberA != numberB)
        {
            return numberA ? -1 : 1;
        }

        return Math.Sign(string.CompareOrdinal(a, b));
    }

    // Digits of any length: compared as text without the zeros in front (a number can be longer than any integer type).
    private static int CompareNumbers(string a, string b)
    {
        string x = a.TrimStart('0');
        string y = b.TrimStart('0');
        return x.Length != y.Length ? x.Length.CompareTo(y.Length) : Math.Sign(string.CompareOrdinal(x, y));
    }

    private static bool IsNumber(string identifier) => identifier.All(static c => c is >= '0' and <= '9');
}
