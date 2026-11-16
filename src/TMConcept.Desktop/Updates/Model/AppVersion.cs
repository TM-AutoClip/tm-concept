using System.Globalization;

namespace TMConcept.Desktop.Updates;

/// <summary>
/// A semantic version (<c>0.1.18</c>, <c>v0.2.0</c>, <c>0.2.0-beta.1</c>; a build part after <c>+</c> is ignored). A pre-release sorts before
/// its release; pre-release parts compare numerically when both are numbers (a number before a word), otherwise as text.
/// </summary>
public readonly record struct AppVersion(int Major, int Minor, int Patch, string Prerelease) : IComparable<AppVersion>
{
    /// <summary>Reads <paramref name="text"/>; false when it is not a version.</summary>
    public static bool TryParse(string? text, out AppVersion version)
    {
        version = default;
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        var value = text.Trim();
        if (value.StartsWith('v') || value.StartsWith('V'))
        {
            value = value[1..];
        }

        var plus = value.IndexOf('+', StringComparison.Ordinal);
        if (plus >= 0)
        {
            value = value[..plus];
        }

        var dash = value.IndexOf('-', StringComparison.Ordinal);
        var pre = dash >= 0 ? value[(dash + 1)..] : string.Empty;
        var parts = (dash >= 0 ? value[..dash] : value).Split('.');
        if (parts.Length is < 2 or > 3 || (dash >= 0 && pre.Length == 0))
        {
            return false;
        }

        var numbers = new int[3];
        for (var i = 0; i < parts.Length; i++)
        {
            if (!int.TryParse(parts[i], NumberStyles.None, CultureInfo.InvariantCulture, out numbers[i]))
            {
                return false;
            }
        }

        version = new AppVersion(numbers[0], numbers[1], numbers[2], pre);
        return true;
    }

    /// <inheritdoc />
    public int CompareTo(AppVersion other)
    {
        var order = Major.CompareTo(other.Major);
        if (order == 0)
        {
            order = Minor.CompareTo(other.Minor);
        }

        if (order == 0)
        {
            order = Patch.CompareTo(other.Patch);
        }

        if (order != 0)
        {
            return order;
        }

        var mine = Prerelease ?? string.Empty;
        var theirs = other.Prerelease ?? string.Empty;
        if (mine.Length == 0 || theirs.Length == 0)
        {
            // A release is newer than its pre-releases.
            return mine.Length == theirs.Length ? 0 : mine.Length == 0 ? 1 : -1;
        }

        var left = mine.Split('.');
        var right = theirs.Split('.');
        for (var i = 0; i < Math.Min(left.Length, right.Length); i++)
        {
            var a = int.TryParse(left[i], NumberStyles.None, CultureInfo.InvariantCulture, out var x);
            var b = int.TryParse(right[i], NumberStyles.None, CultureInfo.InvariantCulture, out var y);
            var step = a && b ? x.CompareTo(y) : a ? -1 : b ? 1 : string.CompareOrdinal(left[i], right[i]);
            if (step != 0)
            {
                return step;
            }
        }

        return left.Length.CompareTo(right.Length);
    }

    /// <summary>Whether this version is newer than <paramref name="other"/>.</summary>
    public bool IsNewerThan(AppVersion other) => CompareTo(other) > 0;

    /// <inheritdoc />
    public override string ToString() =>
        string.Create(CultureInfo.InvariantCulture, $"{Major}.{Minor}.{Patch}") + (string.IsNullOrEmpty(Prerelease) ? string.Empty : "-" + Prerelease);

    /// <summary>Less than.</summary>
    public static bool operator <(AppVersion left, AppVersion right) => left.CompareTo(right) < 0;

    /// <summary>Greater than.</summary>
    public static bool operator >(AppVersion left, AppVersion right) => left.CompareTo(right) > 0;

    /// <summary>Less than or equal.</summary>
    public static bool operator <=(AppVersion left, AppVersion right) => left.CompareTo(right) <= 0;

    /// <summary>Greater than or equal.</summary>
    public static bool operator >=(AppVersion left, AppVersion right) => left.CompareTo(right) >= 0;
}
