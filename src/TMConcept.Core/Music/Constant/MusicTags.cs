namespace TMConcept.Core.Music;

/// <summary>
/// The words of the music tags (0.1.45, C4): the library's tracks are tagged with them (<c>catalog/music.json</c>) and the scene plan's music
/// hint uses them (<see cref="MusicHint"/>). Each style has its nearest styles, taken when no track has the asked one
/// (<see cref="MusicMatcher"/>).
/// </summary>
public static class MusicTags
{
    /// <summary>The moods.</summary>
    public static readonly IReadOnlyList<string> Moods = ["calm", "uplifting", "epic", "dark", "mysterious", "playful", "tense", "sad", "action"];

    /// <summary>The styles.</summary>
    public static readonly IReadOnlyList<string> Styles = ["orchestral", "cinematic", "electronic", "ambient", "acoustic", "world", "medieval", "pop"];

    /// <summary>The tempos: slow below <see cref="SlowBelowBpm"/>, fast above <see cref="FastAboveBpm"/>.</summary>
    public static readonly IReadOnlyList<string> Tempos = [Slow, Medium, Fast];

    /// <summary>How the music comes in or goes out: at once, or faded.</summary>
    public static readonly IReadOnlyList<string> Transitions = [Cut, Fade];

    /// <summary>A slow tempo.</summary>
    public const string Slow = "slow";

    /// <summary>A medium tempo.</summary>
    public const string Medium = "medium";

    /// <summary>A fast tempo.</summary>
    public const string Fast = "fast";

    /// <summary>The music starts or stops at once.</summary>
    public const string Cut = "cut";

    /// <summary>The music fades in or out.</summary>
    public const string Fade = "fade";

    /// <summary>A track below this tempo is slow.</summary>
    public const int SlowBelowBpm = 90;

    /// <summary>A track above this tempo is fast.</summary>
    public const int FastAboveBpm = 125;

    /// <summary>The nearest styles of each style, nearest first (a style without a track takes these).</summary>
    public static readonly IReadOnlyDictionary<string, IReadOnlyList<string>> NearStyles = new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal)
    {
        ["orchestral"] = ["cinematic", "medieval", "ambient"],
        ["cinematic"] = ["orchestral", "ambient", "electronic"],
        ["electronic"] = ["ambient", "pop", "cinematic"],
        ["ambient"] = ["electronic", "cinematic", "acoustic"],
        ["acoustic"] = ["pop", "world", "medieval"],
        ["world"] = ["medieval", "acoustic", "cinematic"],
        ["medieval"] = ["world", "acoustic", "orchestral"],
        ["pop"] = ["electronic", "acoustic", "ambient"],
    };

    /// <summary>The nearest moods of each mood, nearest first.</summary>
    public static readonly IReadOnlyDictionary<string, IReadOnlyList<string>> NearMoods = new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal)
    {
        ["calm"] = ["sad", "uplifting", "mysterious"],
        ["uplifting"] = ["playful", "epic", "calm"],
        ["epic"] = ["action", "uplifting", "tense"],
        ["dark"] = ["tense", "sad", "mysterious"],
        ["mysterious"] = ["tense", "dark", "calm"],
        ["playful"] = ["uplifting", "calm", "mysterious"],
        ["tense"] = ["dark", "action", "mysterious"],
        ["sad"] = ["calm", "dark", "mysterious"],
        ["action"] = ["epic", "tense", "dark"],
    };
}
