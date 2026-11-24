using System.Globalization;
using TMConcept.Core.Speech;

namespace TMConcept.Core.Music;

/// <summary>
/// The English prompt of a generated piece (0.1.46, C5): the scene plan's music hints (K11; only the app's own tag words, so no concept text
/// reaches the music model) decide the mood, style and tempo — the most asked one wins, the earlier scene on a tie; without hints the
/// template's narration tempo gives a calm cinematic bed. The piece is without words by default (it lies under the narration) and asks to stay
/// out of the voice's way. Pure.
/// </summary>
public static class MusicPromptBuilder
{
    /// <summary>The mood without hints.</summary>
    public const string DefaultMood = "calm";

    /// <summary>The style without hints.</summary>
    public const string DefaultStyle = "cinematic";

    /// <summary>The prompt for <paramref name="seconds"/> of music.</summary>
    public static string Build(IReadOnlyList<MusicHint?> hints, NarrationTempo tempo, bool instrumental, int seconds)
    {
        ArgumentNullException.ThrowIfNull(hints);
        var (mood, style, pace) = Choose(hints, tempo);
        var text = string.Create(CultureInfo.InvariantCulture,
            $"{mood} {style} background music for a narrated video, {pace} tempo, about {seconds} seconds, steady and unobtrusive under a voice-over, sparse arrangement, no sudden loud hits")
            + (instrumental ? ", instrumental only, no vocals, no singing" : string.Empty);
        return text.Length > MusicGenerationRules.MaxPromptCharacters ? text[..MusicGenerationRules.MaxPromptCharacters] : text;
    }

    /// <summary>The tempo the prompt asks for (<see cref="MusicTags.Tempos"/>), as beats per minute for the local model's plan.</summary>
    public static int Bpm(string pace) => pace switch
    {
        MusicTags.Slow => 72,
        MusicTags.Fast => 132,
        _ => 104,
    };

    /// <summary>The mood, style and tempo of the hints (each the most asked known word; the defaults without one).</summary>
    public static (string Mood, string Style, string Tempo) Choose(IReadOnlyList<MusicHint?> hints, NarrationTempo tempo)
    {
        ArgumentNullException.ThrowIfNull(hints);
        var given = hints.OfType<MusicHint>().ToList();
        var mood = Most(given.Select(h => h.Mood), MusicTags.Moods) ?? DefaultMood;
        var style = Most(given.Select(h => h.Style), MusicTags.Styles) ?? DefaultStyle;
        var pace = Most(given.Select(h => h.Tempo), MusicTags.Tempos) ?? tempo switch
        {
            NarrationTempo.Tight => MusicTags.Fast,
            NarrationTempo.Relaxed => MusicTags.Slow,
            _ => MusicTags.Medium,
        };
        return (mood, style, pace);
    }

    private static string? Most(IEnumerable<string> words, IReadOnlyList<string> known) =>
        words.Select(w => w.Trim().ToLowerInvariant())
            .Select((w, i) => (Word: w, Index: i))
            .Where(p => known.Contains(p.Word))
            .GroupBy(p => p.Word, StringComparer.Ordinal)
            .OrderByDescending(g => g.Count())
            .ThenBy(g => g.Min(p => p.Index))
            .Select(g => g.Key)
            .FirstOrDefault();
}
