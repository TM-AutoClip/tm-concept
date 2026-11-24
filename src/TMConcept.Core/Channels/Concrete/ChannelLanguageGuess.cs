using TMConcept.Core.Profiles;
using TMConcept.Core.Text;

namespace TMConcept.Core.Channels;

/// <summary>
/// Guesses a channel's language from its titles ("Kanaldan esinlen", 0.1.53): each output language (<see cref="ContentLanguages.Definitions"/>)
/// scores the folded title words that are its filler words (English: <see cref="ContentLanguages.EnglishStopWords"/>), Turkish also scores its
/// own letters (ğ, ş, ı, İ) and Russian the Cyrillic ones. A language is given only when it is sure: at least
/// <see cref="MinScore"/> points and twice the next one; otherwise null and the user chooses. Pure.
/// </summary>
public static class ChannelLanguageGuess
{
    /// <summary>The fewest points of a sure guess.</summary>
    public const int MinScore = 4;

    /// <summary>The language of <paramref name="texts"/> (titles), or null when unsure.</summary>
    public static string? Guess(IEnumerable<string> texts)
    {
        ArgumentNullException.ThrowIfNull(texts);
        var all = texts.Where(t => !string.IsNullOrWhiteSpace(t)).ToList();
        if (all.Count == 0)
        {
            return null;
        }

        var words = all.SelectMany(t => TurkishFold.Fold(t).Split(' ', StringSplitOptions.RemoveEmptyEntries)).ToList();
        var scores = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var language in ContentLanguages.Definitions)
        {
            var stop = language.Code == ContentLanguages.English ? ContentLanguages.EnglishStopWords : language.StopWords;
            var set = stop.Select(TurkishFold.Fold).Where(w => w.Length > 1).ToHashSet(StringComparer.Ordinal);
            scores[language.Code] = words.Count(set.Contains);
        }

        var letters = string.Concat(all);
        scores[ContentLanguages.Turkish] += letters.Count(c => c is 'ğ' or 'Ğ' or 'ş' or 'Ş' or 'ı' or 'İ');
        scores[ContentLanguages.Russian] += letters.Count(c => c is >= 'Ѐ' and <= 'ӿ') / 4;
        var ranked = scores.OrderByDescending(s => s.Value).ToList();
        var best = ranked[0];
        var next = ranked.Count > 1 ? ranked[1].Value : 0;
        return best.Value >= MinScore && best.Value >= 2 * next ? best.Key : null;
    }
}
