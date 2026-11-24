namespace TMConcept.Core.Subtitles;

/// <summary>
/// The numbers of the subtitles timed by the voice (0.1.48, F5 + F6). The grouping rules and their numbers follow ReelMimic's
/// caption-grouping notes (numbers and rules only, our own code); the match threshold follows its align_lyrics.py (MIT, "the text is right,
/// the recognition is only a ruler").
/// </summary>
public static class CaptionTiming
{
    /// <summary>A sentence whose share of heard letters is below this is marked "check by ear".</summary>
    public const double MatchThreshold = 0.6;

    /// <summary>A pause this long between two words starts a new group (a breath).</summary>
    public const long PauseMs = 500;

    /// <summary>A word ending in a comma starts a new group after it when this much silence follows.</summary>
    public const long CommaPauseMs = 250;

    /// <summary>A group holds at most this many words.</summary>
    public const int MaxWords = 6;

    /// <summary>A group lasts at most this long (first word's start to last word's end).</summary>
    public const long MaxMs = 2500;

    /// <summary>A soft break (comma, conjunction, size) waits until the group has this many words.</summary>
    public const int MinWords = 2;

    /// <summary>A soft break waits until the group lasts this long.</summary>
    public const long MinMs = 500;

    /// <summary>A group appears this much before its first word.</summary>
    public const long LeadMs = 80;

    /// <summary>A group ends at least this much before the next one appears.</summary>
    public const long GapMs = 50;

    /// <summary>A group stays at most this long after its last word.</summary>
    public const long TailMs = 600;
}
