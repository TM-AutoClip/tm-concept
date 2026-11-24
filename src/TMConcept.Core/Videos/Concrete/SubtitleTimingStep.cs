using System.Globalization;
using System.Text;
using Microsoft.Extensions.Logging;
using TMConcept.Core.Render;
using TMConcept.Core.Speech;
using TMConcept.Core.Subtitles;

namespace TMConcept.Core.Videos;

/// <summary>
/// The subtitles of a video (0.1.48, F5 + F6, S3). Without whisper.cpp (not installed, or no build for this computer) they are exactly what
/// they were: <see cref="SubtitleBuilder.Build"/> over <see cref="VideoAssembler.SubtitleSources"/>, the words' times estimated by length.
/// With it, each scene's narration clip is heard once (a voice engine that gives its words' times is not heard again), the script's words are
/// placed on the heard times part by part (<see cref="WordAligner"/>; the text stays the script's) and cut by the breath of the speech
/// (<see cref="PauseGrouper"/>). A scene that cannot be heard or aligned keeps the estimated subtitles and the video gets
/// <see cref="VideoWarnings.SubtitleEstimated"/>; a sentence heard poorly gives <see cref="VideoWarnings.SubtitleListen"/>. Neither stops
/// the video: the subtitles are there either way.
/// </summary>
public sealed class SubtitleTimingStep(IWordRecognizer recognizer, ILogger<SubtitleTimingStep> logger)
{
    /// <summary>Whether the words will be heard (whisper.cpp installed and usable here).</summary>
    public bool Hears => recognizer.Supported && recognizer.Ready;

    /// <summary>
    /// The cues of <paramref name="input"/>'s narration (<paramref name="clips"/> per scene, <paramref name="language"/> the narration's
    /// language code) at <paramref name="lineLength"/> characters per line. <paramref name="shown"/>: whether the subtitles are drawn on the
    /// picture; subtitles that are not (only the SRT file next to the video) are never heard, so a video without subtitles costs no
    /// whisper.cpp run and gets no subtitle warning (times a voice engine gave itself are still used).
    /// </summary>
    public async Task<SubtitleTimingResult> BuildAsync(RenderInput input, IReadOnlyList<SpeechClip?> clips, string language, int lineLength,
        bool shown, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(clips);
        var hears = shown && Hears;
        if (!hears && !clips.Any(c => c is { Words.Count: > 0 }))
        {
            return new SubtitleTimingResult(SubtitleBuilder.Build(VideoAssembler.SubtitleSources(input, clips), lineLength), [], []);
        }

        var cues = new List<SubtitleCue>();
        var sentences = new List<AlignedSentence>();
        var estimated = false;
        foreach (var scene in input.Scenes)
        {
            var sceneStart = (long)Math.Round(scene.StartSeconds * 1000);
            var clip = clips.ElementAtOrDefault(scene.Number - 1);
            var sources = VideoAssembler.SceneSources(scene, clip);
            var heard = await HeardAsync(clip, scene.Number, language, hears, cancellationToken).ConfigureAwait(false);
            if (heard is null)
            {
                estimated |= clip is not null && File.Exists(clip.File) && hears;
                cues.AddRange(SubtitleBuilder.Build(sources, lineLength));
                continue;
            }

            foreach (var source in sources.Where(s => s.Text.Trim().Length > 0 && s.DurationMs > 0))
            {
                var from = source.StartMs - sceneStart;
                var to = from + source.DurationMs;
                var part = heard.Where(w => (w.StartMs + w.EndMs) / 2 >= from && (w.StartMs + w.EndMs) / 2 < to).ToList();
                var aligned = WordAligner.Align(source.Text, part, from, to);
                if (aligned is null)
                {
                    logger.LogWarning("Scene {Scene}: a part of {Ms} ms is too long to align; its subtitles keep estimated times", scene.Number,
                        source.DurationMs);
                    estimated = true;
                    cues.AddRange(SubtitleBuilder.Build([source], lineLength));
                    continue;
                }

                var shifted = aligned.Select(s => s with { Words = s.Words.Select(w => w with { StartMs = w.StartMs + sceneStart, EndMs = w.EndMs + sceneStart }).ToList() })
                    .ToList();
                sentences.AddRange(shifted);
                cues.AddRange(PauseGrouper.Group(shifted.SelectMany(s => s.Words).ToList(), language, lineLength, source.StartMs,
                    source.StartMs + source.DurationMs));
            }
        }

        var listen = sentences.Count(s => s.NeedsListening);
        logger.LogInformation("Subtitles timed by the voice: {Sentences} sentences, {Listen} below the match threshold, {Interpolated} words placed between neighbours",
            sentences.Count, listen, sentences.Sum(s => s.Words.Count(w => w.Interpolated)));
        var warnings = new List<string>();
        if (listen > 0)
        {
            warnings.Add(VideoWarnings.SubtitleListen);
        }

        if (estimated)
        {
            warnings.Add(VideoWarnings.SubtitleEstimated);
        }

        return new SubtitleTimingResult(cues, sentences, warnings);
    }

    /// <summary>
    /// The text of <see cref="VideoFolders.SubtitleCheckFile"/>: each sentence below the threshold with its time and match share; empty when
    /// there is none.
    /// </summary>
    public static string CheckText(IReadOnlyList<AlignedSentence> sentences)
    {
        ArgumentNullException.ThrowIfNull(sentences);
        var text = new StringBuilder();
        foreach (var sentence in sentences.Where(s => s.NeedsListening && s.Words.Count > 0))
        {
            text.Append(SubtitleBuilder.AssTime(sentence.Words[0].StartMs)).Append(" – ").Append(SubtitleBuilder.AssTime(sentence.Words[^1].EndMs))
                .Append("  ").Append(Math.Round(sentence.MatchRatio * 100).ToString(CultureInfo.InvariantCulture)).Append(" %  ")
                .Append(sentence.Text).Append('\n');
        }

        return text.ToString();
    }

    /// <summary>The clip's words with times in the clip: given by the voice engine, else heard; null when neither (estimate).</summary>
    private async Task<IReadOnlyList<RecognizedWord>?> HeardAsync(SpeechClip? clip, int scene, string language, bool hears,
        CancellationToken cancellationToken)
    {
        if (clip is null)
        {
            return null;
        }

        if (clip.Words.Count > 0)
        {
            return clip.Words;
        }

        if (!hears || !File.Exists(clip.File))
        {
            return null;
        }

        try
        {
            return await recognizer.RecognizeAsync(clip.File, language, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is InvalidOperationException or FormatException or IOException or UnauthorizedAccessException)
        {
            logger.LogWarning(ex, "Scene {Scene}: whisper.cpp could not hear the narration; its subtitles keep estimated times", scene);
            return null;
        }
    }
}
