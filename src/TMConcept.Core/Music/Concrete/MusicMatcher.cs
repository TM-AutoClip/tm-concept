using TMConcept.Core.Speech;

namespace TMConcept.Core.Music;

/// <summary>
/// Chooses a library track for a video from its scenes' music hints (0.1.45, C4; pure). The hints are counted (the mood, style and tempo most
/// scenes ask for); without a tempo hint the template's narration tempo decides (tight = fast, relaxed = slow), without a mood one the tempo's
/// usual mood. Each track scores: the asked mood 6, a near mood 3/2/1 (<see cref="MusicTags.NearMoods"/>), every other asked mood 1; the asked
/// style 4, a near style 3/2/1 (<see cref="MusicTags.NearStyles"/>); the asked tempo 2, the one next to it 1. When no track has the asked style
/// the nearest one wins (<see cref="MusicChoice.Nearest"/>). Ties take the first id.
/// </summary>
public static class MusicMatcher
{
    /// <summary>The best track of <paramref name="tracks"/>; <c>null</c> when there is none.</summary>
    public static MusicChoice? Choose(IReadOnlyList<MusicTrack> tracks, IEnumerable<MusicHint?> hints, NarrationTempo tempo)
    {
        ArgumentNullException.ThrowIfNull(tracks);
        ArgumentNullException.ThrowIfNull(hints);
        if (tracks.Count == 0)
        {
            return null;
        }

        var given = hints.OfType<MusicHint>().ToList();
        var moods = Count(given.Select(h => h.Mood), MusicTags.Moods);
        var style = Count(given.Select(h => h.Style), MusicTags.Styles).FirstOrDefault() ?? string.Empty;
        var pace = Count(given.Select(h => h.Tempo), MusicTags.Tempos).FirstOrDefault() ?? TempoOf(tempo);
        var mood = moods.FirstOrDefault() ?? (tempo == NarrationTempo.Relaxed ? "calm" : "uplifting");
        var nearest = style.Length > 0 && !tracks.Any(t => t.Styles.Contains(style, StringComparer.Ordinal));
        return tracks
            .Select(t => new MusicChoice(t, Score(t, mood, moods, style, pace), nearest))
            .OrderByDescending(c => c.Score)
            .ThenBy(c => c.Track.Id, StringComparer.Ordinal)
            .First();
    }

    /// <summary>The tempo word of a track's beats per minute (<see cref="MusicTags.SlowBelowBpm"/>, <see cref="MusicTags.FastAboveBpm"/>).</summary>
    public static string TempoOf(int bpm) => bpm switch
    {
        <= 0 => string.Empty,
        < MusicTags.SlowBelowBpm => MusicTags.Slow,
        > MusicTags.FastAboveBpm => MusicTags.Fast,
        _ => MusicTags.Medium,
    };

    /// <summary>The tempo word of a narration tempo.</summary>
    public static string TempoOf(NarrationTempo tempo) => tempo switch
    {
        NarrationTempo.Tight => MusicTags.Fast,
        NarrationTempo.Relaxed => MusicTags.Slow,
        _ => MusicTags.Medium,
    };

    /// <summary>The score of one track (<see cref="MusicMatcher"/>).</summary>
    public static int Score(MusicTrack track, string mood, IReadOnlyList<string> moods, string style, string tempo)
    {
        ArgumentNullException.ThrowIfNull(track);
        ArgumentNullException.ThrowIfNull(moods);
        var score = 0;
        if (track.Moods.Contains(mood, StringComparer.Ordinal))
        {
            score += 6;
        }
        else if (MusicTags.NearMoods.TryGetValue(mood, out var nearMoods))
        {
            score += Near(track.Moods, nearMoods);
        }

        score += moods.Skip(1).Count(m => track.Moods.Contains(m, StringComparer.Ordinal));
        if (style.Length > 0)
        {
            if (track.Styles.Contains(style, StringComparer.Ordinal))
            {
                score += 4;
            }
            else if (MusicTags.NearStyles.TryGetValue(style, out var nearStyles))
            {
                score += Near(track.Styles, nearStyles);
            }
        }

        var own = TempoOf(track.Bpm);
        if (own.Length > 0 && tempo.Length > 0)
        {
            var distance = Math.Abs(MusicTags.Tempos.ToList().IndexOf(own) - MusicTags.Tempos.ToList().IndexOf(tempo));
            score += distance == 0 ? 2 : distance == 1 ? 1 : 0;
        }

        return score;
    }

    /// <summary>3 for the nearest word the track has, 2 for the second, 1 for the third, 0 for none.</summary>
    private static int Near(IReadOnlyList<string> own, IReadOnlyList<string> near)
    {
        for (var i = 0; i < near.Count; i++)
        {
            if (own.Contains(near[i], StringComparer.Ordinal))
            {
                return Math.Max(0, 3 - i);
            }
        }

        return 0;
    }

    /// <summary>The known words of <paramref name="values"/>, most asked first (ties in the vocabulary's order).</summary>
    private static List<string> Count(IEnumerable<string> values, IReadOnlyList<string> known) =>
        values.Select(v => (v ?? string.Empty).Trim().ToLowerInvariant())
            .Where(v => known.Contains(v, StringComparer.Ordinal))
            .GroupBy(v => v, StringComparer.Ordinal)
            .OrderByDescending(g => g.Count())
            .ThenBy(g => known.ToList().IndexOf(g.Key))
            .Select(g => g.Key)
            .ToList();
}
