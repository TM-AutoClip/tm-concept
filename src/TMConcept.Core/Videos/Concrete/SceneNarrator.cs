using System.Globalization;
using Microsoft.Extensions.Logging;
using TMConcept.Core.Markers;
using TMConcept.Core.Profiles;
using TMConcept.Core.Speech;

namespace TMConcept.Core.Videos;

/// <summary>
/// The voice of one scene from its script, the same for the first video (<see cref="VideoPipeline"/>) and every new version
/// (<see cref="VideoRemaker"/>): the spoken parts between the narration placeholders, each with its speed (the voice's own speed × the
/// placeholder's), joined with the pauses into <c>audio/&lt;n&gt;.wav</c>. Transition placeholders and other bracket text are never spoken.
/// 0.1.31 (the developer: "no pause between the sentences, it speaks too fast"): every part is spoken sentence by sentence
/// (<see cref="SentenceSplitter"/>) with the template tempo's silence after each sentence, and the scene's clip ends with the tempo's scene
/// silence (<see cref="NarrationPacing"/>), so the picture changes after a breath. Each sentence is a span of the clip, so the subtitles
/// follow it exactly. A user's pause placeholder is added as written, on top of the sentence silence.
/// </summary>
public sealed class SceneNarrator(SpeechService speech, ILogger<SceneNarrator> logger)
{
    /// <summary>
    /// The clip of scene <paramref name="number"/> (null when the script says nothing and has no pause). Each part's cost goes to
    /// <paramref name="paid"/> as soon as it is made, so what a later failure leaves behind is counted. Speech and file errors go up as they are.
    /// </summary>
    public async Task<SpeechClip?> VoiceAsync(SpeechSetup voice, string language, int number, string script, string work, NarrationPacing pacing,
        Action<decimal> paid, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(voice);
        ArgumentNullException.ThrowIfNull(pacing);
        ArgumentNullException.ThrowIfNull(paid);
        var parts = Sentences(MarkerText.Parts(script, out var ignored), pacing);
        if (ignored.Count > 0)
        {
            // Only the count: the text is the user's.
            logger.LogInformation("Scene {Number}: {Count} bracket text(s) that are no placeholder are not spoken", number, ignored.Count);
        }

        if (parts.Count == 0)
        {
            return null;
        }

        var joined = Path.Combine(work, VideoFolders.Audio, number.ToString(CultureInfo.InvariantCulture) + ".wav");
        if (parts is [{ PauseAfterMs: 0 } only] && only.Text.Length > 0 && pacing.SceneGapMs == 0)
        {
            var single = await speech.SynthesizeAsync(Speed(voice, only.Speed), language, only.Text, joined, cancellationToken).ConfigureAwait(false);
            paid(single.CostUsd);
            return single;
        }

        // The scene's breath after its last word (a silent last part keeps its own pause and gets it too).
        parts[^1] = parts[^1] with { PauseAfterMs = parts[^1].PauseAfterMs + pacing.SceneGapMs };
        var pieces = new List<(string? File, int SilenceAfterMs)>();
        decimal cost = 0;
        var characters = 0;
        var skipped = new HashSet<string>(StringComparer.Ordinal);
        var spans = new List<SpeechSpan>();
        long at = 0;
        for (var k = 0; k < parts.Count; k++)
        {
            var part = parts[k];
            if (part.Text.Length == 0)
            {
                pieces.Add((null, part.PauseAfterMs));
                at += part.PauseAfterMs;
                continue;
            }

            var file = Path.Combine(work, VideoFolders.Audio, "parts", string.Create(CultureInfo.InvariantCulture, $"{number}-{k + 1}.wav"));
            var clip = await speech.SynthesizeAsync(Speed(voice, part.Speed), language, part.Text, file, cancellationToken).ConfigureAwait(false);
            paid(clip.CostUsd);
            cost += clip.CostUsd;
            characters += clip.Characters;
            skipped.UnionWith(clip.SkippedWords);
            pieces.Add((clip.File, part.PauseAfterMs));
            spans.Add(new SpeechSpan(at, clip.DurationMs, part.Text));
            at += clip.DurationMs + part.PauseAfterMs;
        }

        var length = WaveFile.Join(pieces, joined);
        return new SpeechClip(joined, length, characters, cost, voice.Engine.Code)
        {
            SkippedWords = skipped.Order(StringComparer.Ordinal).ToList(),
            Spans = spans,
        };
    }

    /// <summary>
    /// The parts split into sentences: every sentence but a part's last gets <see cref="NarrationPacing.SentenceGapMs"/> after it; a part's last
    /// sentence keeps the part's own pause, and gets the sentence silence too when it ends a sentence and another part follows (a speed
    /// placeholder between two sentences must not glue them together).
    /// </summary>
    internal static List<NarrationPart> Sentences(IReadOnlyList<NarrationPart> parts, NarrationPacing pacing)
    {
        var result = new List<NarrationPart>();
        for (var p = 0; p < parts.Count; p++)
        {
            var part = parts[p];
            IReadOnlyList<string> sentences = part.Text.Length == 0 ? [] : SentenceSplitter.Split(part.Text);
            if (sentences.Count == 0)
            {
                result.Add(part);
                continue;
            }

            for (var i = 0; i < sentences.Count; i++)
            {
                var last = i == sentences.Count - 1;
                var pause = !last ? pacing.SentenceGapMs
                    : part.PauseAfterMs + (p < parts.Count - 1 && EndsSentence(sentences[i]) ? pacing.SentenceGapMs : 0);
                result.Add(new NarrationPart(sentences[i], part.Speed, pause));
            }
        }

        return result;
    }

    private static bool EndsSentence(string sentence) => sentence.TrimEnd('"', '\'', '»', ')', ']') is { Length: > 0 } t && t[^1] is '.' or '!' or '?' or '…';

    private static SpeechSetup Speed(SpeechSetup voice, double factor) =>
        factor == 1 ? voice : voice with { Speed = Math.Clamp(voice.Speed * factor, ProfileLimits.SpeedMin, ProfileLimits.SpeedMax) };
}
