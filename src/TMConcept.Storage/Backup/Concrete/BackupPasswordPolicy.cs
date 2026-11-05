namespace TMConcept.Storage.Backup;

/// <summary>Backup and master password rules: at least 12 characters; a simple strength score for the meter.</summary>
public static class BackupPasswordPolicy
{
    /// <summary>Shortest accepted password.</summary>
    public const int MinimumLength = 12;

    /// <summary>Whether the password is accepted.</summary>
    public static bool IsAcceptable(string? password) => (password ?? string.Empty).Length >= MinimumLength;

    /// <summary>Strength 0-4 (length and character classes), for the meter only.</summary>
    public static int Strength(string? password)
    {
        var p = password ?? string.Empty;
        if (p.Length < MinimumLength)
        {
            return p.Length == 0 ? 0 : 1;
        }

        var classes = new[] { p.Any(char.IsLower), p.Any(char.IsUpper), p.Any(char.IsDigit), p.Any(c => !char.IsLetterOrDigit(c)) }.Count(x => x);
        return Math.Clamp((p.Length >= 16 ? 2 : 1) + (classes >= 3 ? 2 : classes >= 2 ? 1 : 0), 1, 4);
    }
}
