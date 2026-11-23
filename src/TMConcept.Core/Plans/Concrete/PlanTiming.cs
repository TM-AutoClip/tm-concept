using TMConcept.Core.Render;
using TMConcept.Core.Speech;

namespace TMConcept.Core.Plans;

/// <summary>
/// Times a scene's beats from its narration (0.1.33): each beat starts when its sentence is heard and lasts until the next sentence (the last
/// one until the scene ends). The voice speaks sentence by sentence (<see cref="Videos.SceneNarrator"/>), so a clip's spans are its sentences;
/// without them (a silent video, a kept clip) the scene is shared by the sentences' lengths in letters.
/// </summary>
public static class PlanTiming
{
    /// <summary>The sentences of a scene for the plan: the spoken spans' texts when there are any, otherwise the caption split into sentences.</summary>
    public static IReadOnlyList<string> Sentences(string caption, IReadOnlyList<SpeechSpan>? spans)
    {
        if (spans is { Count: > 0 })
        {
            return spans.Select(s => s.Text).ToList();
        }

        var split = SentenceSplitter.Split(caption ?? string.Empty);
        return split.Count > 0 ? split : [caption ?? string.Empty];
    }

    /// <summary>
    /// The start of every sentence, seconds from the scene's start: from <paramref name="spans"/> when there is one per sentence, otherwise in
    /// proportion to <paramref name="sentences"/>' lengths over <paramref name="seconds"/>.
    /// </summary>
    public static IReadOnlyList<double> Starts(IReadOnlyList<string> sentences, IReadOnlyList<SpeechSpan>? spans, double seconds)
    {
        ArgumentNullException.ThrowIfNull(sentences);
        if (sentences.Count == 0)
        {
            return [0];
        }

        if (spans is { Count: > 0 } heard && heard.Count == sentences.Count)
        {
            return heard.Select(s => Math.Min(seconds, s.StartMs / 1000.0)).ToList();
        }

        var total = Math.Max(1, sentences.Sum(s => Math.Max(1, s.Length)));
        var starts = new List<double>();
        var at = 0.0;
        foreach (var sentence in sentences)
        {
            starts.Add(at);
            at += seconds * Math.Max(1, sentence.Length) / total;
        }

        return starts;
    }

    /// <summary>
    /// Fills the beats' <see cref="PlanBeat.Start"/> and <see cref="PlanBeat.Duration"/> (seconds in the video) of <paramref name="plan"/> for a
    /// scene from <paramref name="sceneStart"/> lasting <paramref name="seconds"/>.
    /// </summary>
    public static void Apply(RenderPlan plan, double sceneStart, double seconds, IReadOnlyList<double> starts)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(starts);
        if (starts.Count == 0)
        {
            starts = [0];
        }

        foreach (var beat in plan.Beats)
        {
            var sentence = Math.Clamp(beat.Sentence, 1, starts.Count);
            var from = starts[sentence - 1];
            var to = sentence < starts.Count ? starts[sentence] : seconds;
            beat.Start = Math.Round(sceneStart + from, 3);
            beat.Duration = Math.Round(Math.Max(0.5, to - from), 3);
        }
    }

    /// <summary>
    /// A new version with new timing (a new voice, edited texts): every scene of <paramref name="timed"/> takes the plan of the same scene of
    /// <paramref name="source"/>, its beats timed again from the new clips' spans (or the caption's sentences).
    /// </summary>
    public static void Carry(RenderInput source, RenderInput timed, IReadOnlyList<IReadOnlyList<SpeechSpan>?> spans)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(timed);
        ArgumentNullException.ThrowIfNull(spans);
        for (var i = 0; i < timed.Scenes.Count && i < source.Scenes.Count; i++)
        {
            if (source.Scenes[i].Plan is not { } plan)
            {
                continue;
            }

            var scene = timed.Scenes[i];
            var heard = i < spans.Count ? spans[i] : null;
            var sentences = Sentences(scene.Caption, heard);
            foreach (var beat in plan.Beats)
            {
                beat.Sentence = Math.Clamp(beat.Sentence, 1, Math.Max(1, sentences.Count));
            }

            Apply(plan, scene.StartSeconds, scene.DurationSeconds, Starts(sentences, heard, scene.DurationSeconds));
            scene.Plan = plan;
        }
    }
}
