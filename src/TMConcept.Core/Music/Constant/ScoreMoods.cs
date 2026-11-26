namespace TMConcept.Core.Music;

/// <summary>
/// How each <see cref="ScoreMood"/> is composed (0.1.55, "Program composes"): common progressions of film and library music (I–vi–IV–V
/// for calm, I–V–vi–IV for uplifting, i–VI–III–VII for epic …), an energy and a tempo. The app's own choices, written for this composer.
/// </summary>
public static class ScoreMoods
{
    /// <summary>The mood of a prompt without a mood word.</summary>
    public const ScoreMood Default = ScoreMood.Calm;

    /// <summary>The shape of each mood.</summary>
    public static readonly IReadOnlyDictionary<ScoreMood, ScoreMoodShape> All = new Dictionary<ScoreMood, ScoreMoodShape>
    {
        [ScoreMood.Calm] = new(Minor: false, [0, 5, 3, 4], 0.3, 80),
        [ScoreMood.Uplifting] = new(Minor: false, [0, 4, 5, 3], 0.6, 112),
        [ScoreMood.Epic] = new(Minor: true, [0, 5, 2, 6], 0.8, 96),
        [ScoreMood.Dark] = new(Minor: true, [0, 5, 6, 4], 0.5, 84),
        [ScoreMood.Mysterious] = new(Minor: true, [0, 5, 3, 4], 0.4, 88),
        [ScoreMood.Playful] = new(Minor: false, [0, 3, 4, 0], 0.6, 120),
        [ScoreMood.Tense] = new(Minor: true, [0, 0, 5, 6], 0.7, 100),
        [ScoreMood.Sad] = new(Minor: true, [0, 3, 5, 4], 0.3, 72),
        [ScoreMood.Action] = new(Minor: true, [0, 6, 5, 6], 0.9, 140),
    };
}
