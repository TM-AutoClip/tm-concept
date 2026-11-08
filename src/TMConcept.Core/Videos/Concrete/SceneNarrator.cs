using System.Globalization;
using Microsoft.Extensions.Logging;
using TMConcept.Core.Markers;
using TMConcept.Core.Profiles;
using TMConcept.Core.Speech;

namespace TMConcept.Core.Videos;

/// <summary>
/// The voice of one scene from its script, the same for the first video (<see cref="VideoPipeline"/>) and every new version
/// (<see cref="VideoRemaker"/>): the spoken parts between the narration placeholders, each with its speed (the voice's own speed × the
/// placeholder's), joined with the pauses into <c>audio/&lt;n&gt;.wav</c>. A script without placeholders is spoken in one go straight into
/// that file. Transition placeholders and other bracket text are never spoken.
/// </summary>
public sealed class SceneNarrator(SpeechService speech, ILogger<SceneNarrator> logger)
{
    /// <summary>
    /// The clip of scene <paramref name="number"/> (null when the script says nothing and has no pause). Each part's cost goes to
    /// <paramref name="paid"/> as soon as it is made, so what a later failure leaves behind is counted. Speech and file errors go up as they are.
    /// </summary>
    public async Task<SpeechClip?> VoiceAsync(SpeechSetup voice, string language, int number, string script, string work, Action<decimal> paid,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(voice);
        ArgumentNullException.ThrowIfNull(paid);
        var parts = MarkerText.Parts(script, out var ignored);
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
        if (parts is [{ PauseAfterMs: 0 } only] && only.Text.Length > 0)
        {
            var single = await speech.SynthesizeAsync(Speed(voice, only.Speed), language, only.Text, joined, cancellationToken).ConfigureAwait(false);
            paid(single.CostUsd);
            return single;
        }

        var pieces = new List<(string? File, int SilenceAfterMs)>();
        decimal cost = 0;
        var characters = 0;
        var skipped = new HashSet<string>(StringComparer.Ordinal);
        for (var k = 0; k < parts.Count; k++)
        {
            var part = parts[k];
            if (part.Text.Length == 0)
            {
                pieces.Add((null, part.PauseAfterMs));
                continue;
            }

            var file = Path.Combine(work, VideoFolders.Audio, "parts", string.Create(CultureInfo.InvariantCulture, $"{number}-{k + 1}.wav"));
            var clip = await speech.SynthesizeAsync(Speed(voice, part.Speed), language, part.Text, file, cancellationToken).ConfigureAwait(false);
            paid(clip.CostUsd);
            cost += clip.CostUsd;
            characters += clip.Characters;
            skipped.UnionWith(clip.SkippedWords);
            pieces.Add((clip.File, part.PauseAfterMs));
        }

        var length = WaveFile.Join(pieces, joined);
        return new SpeechClip(joined, length, characters, cost, voice.Engine.Code) { SkippedWords = skipped.Order(StringComparer.Ordinal).ToList() };
    }

    private static SpeechSetup Speed(SpeechSetup voice, double factor) =>
        factor == 1 ? voice : voice with { Speed = Math.Clamp(voice.Speed * factor, ProfileLimits.SpeedMin, ProfileLimits.SpeedMax) };
}
