namespace TMConcept.Desktop.Updates;

/// <summary>
/// Which versions the "What's new" window shows at start (0.1.23): every version after the one that ran before (exclusive) up to the running
/// one (inclusive), newest first. The version that ran before is what the version check already kept (<c>LastVersion</c> of
/// <c>preferences.json</c>, recorded since 0.1.16), read before this start changes it, and the newest version a start since 0.1.23 recorded
/// (<c>WhatsNewSeenVersion</c>, written at every start that runs a newer version): the newer of the two is the start, so the notes show once.
/// Nothing when neither is known (a new install, or 0.1.15 and earlier), when nothing changed, or when a version is not a version.
/// <para>
/// A limit of the first start of 0.1.23: before it only <c>LastVersion</c> was kept, and the version check records it only when the
/// components were dealt with (updated, reinstalled, or none installed). Someone who always chose "Later" since an older version sees the
/// notes from that older version on, once. From 0.1.23 on, every start records its version, so this does not happen again.
/// </para>
/// </summary>
public static class WhatsNewRange
{
    /// <summary>
    /// The versions of <paramref name="available"/> after <paramref name="previous"/> (and after <paramref name="seen"/>, the newest version
    /// whose notes were shown) up to <paramref name="current"/>, newest first.
    /// </summary>
    public static IReadOnlyList<VersionNotes> Versions(string? previous, string? seen, string current, IEnumerable<VersionNotes> available)
    {
        ArgumentNullException.ThrowIfNull(available);
        if (!AppVersion.TryParse(current, out var to))
        {
            return [];
        }

        var knowsPrevious = AppVersion.TryParse(previous, out var from);
        if (AppVersion.TryParse(seen, out var shown) && (!knowsPrevious || shown > from))
        {
            from = shown;
        }
        else if (!knowsPrevious)
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
