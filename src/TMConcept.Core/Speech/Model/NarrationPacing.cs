namespace TMConcept.Core.Speech;

/// <summary>
/// The silences the program adds to the narration (<see cref="NarrationTempos.Of"/>), on top of the short margins every clip keeps after its
/// dead air is trimmed (<see cref="NarrationGaps"/>): <paramref name="SentenceGapMs"/> after every sentence inside a scene,
/// <paramref name="SceneGapMs"/> after a scene's last word (the scene lasts that much longer, so the picture changes after the pause).
/// <see cref="NarrationTempos.Heard"/> uses the same pair for the totals a listener hears, those margins included (0.1.62).
/// </summary>
public sealed record NarrationPacing(int SentenceGapMs, int SceneGapMs)
{
    /// <summary>No added silence (a silent video, or a test that times clips exactly).</summary>
    public static NarrationPacing None { get; } = new(0, 0);
}
