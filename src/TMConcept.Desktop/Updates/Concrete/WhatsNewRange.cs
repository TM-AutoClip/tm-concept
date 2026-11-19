namespace TMConcept.Desktop.Updates;

/// <summary>
/// Which versions the "What's new" window shows at start: every version after the newest one a start recorded (<c>WhatsNewSeenVersion</c>
/// of <c>preferences.json</c>, written at every start that runs a newer version; exclusive) up to the running one (inclusive), newest first,
/// so the notes show once. Nothing when no version was recorded (a new install), when nothing changed, or when a version is not a version.
/// </summary>
public static class WhatsNewRange
{
    /// <summary>
    /// The versions of <paramref name="available"/> after <paramref name="seen"/> (the newest version a start recorded) up to
    /// <paramref name="current"/>, newest first.
    /// </summary>
    public static IReadOnlyList<VersionNotes> Versions(string? seen, string current, IEnumerable<VersionNotes> available)
    {
        ArgumentNullException.ThrowIfNull(available);
        if (!AppVersion.TryParse(current, out var to) || !AppVersion.TryParse(seen, out var from))
        {
            return [];
        }

        return from >= to ? [] : available.Where(n => n.Version > from && n.Version <= to).OrderByDescending(n => n.Version).ToList();
    }

    /// <summary>Every version of <paramref name="available"/> up to <paramref name="current"/>, newest first (the window opened by hand).</summary>
    public static IReadOnlyList<VersionNotes> UpTo(string current, IEnumerable<VersionNotes> available)
    {
        ArgumentNullException.ThrowIfNull(available);
        return AppVersion.TryParse(current, out var to)
            ? available.Where(n => n.Version <= to).OrderByDescending(n => n.Version).ToList()
            : available.OrderByDescending(n => n.Version).ToList();
    }
}
