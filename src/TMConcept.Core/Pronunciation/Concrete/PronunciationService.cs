using Microsoft.Extensions.Logging;
using TMConcept.Core.Profiles;

namespace TMConcept.Core.Pronunciation;

/// <summary>
/// The pronunciation dictionary of each output language: the built-in words (<see cref="BuiltInPronunciations"/>) and the user's own (the
/// user's word of the same spelling replaces a built-in one; a word the user writes as "!WORD" matches only as written). Every narration is
/// put through it right before a voice speaks it, local or online (<see cref="Speech.SpeechService"/>), and then through the language's
/// normalizer when it has one (<see cref="ISpeechTextNormalizer"/>: Turkish numbers, dates, abbreviations, units; suffixes agreed with the
/// spoken form); subtitles and the narration file keep the original text.
/// </summary>
public sealed class PronunciationService(IPronunciationStore store, IEnumerable<ISpeechTextNormalizer> normalizers, ILogger<PronunciationService> logger)
{
    private readonly IReadOnlyList<ISpeechTextNormalizer> _normalizers = normalizers.ToList();

    /// <summary>Most characters of a word or its spoken form.</summary>
    public const int WordMax = 60;

    /// <summary>Every rule of a language: the user's words first, then the built-in ones they do not replace.</summary>
    public async Task<IReadOnlyList<PronunciationRule>> RulesAsync(string language, CancellationToken cancellationToken)
    {
        var culture = OutputLanguages.Of(language).Culture;
        var own = (await store.ListAsync(language, cancellationToken).ConfigureAwait(false)).Select(Own).ToList();
        var taken = own.Select(r => Key(r, culture)).ToHashSet(StringComparer.Ordinal);
        return [.. own, .. BuiltInPronunciations.Of(language).Where(r => !taken.Contains(Key(r, culture)))];
    }

    /// <summary>The text the voice gets for a narration in <paramref name="language"/>.</summary>
    public Task<string> PrepareAsync(string text, string language, CancellationToken cancellationToken) =>
        PrepareAsync(text, language, [], cancellationToken);

    /// <summary>
    /// The text the voice gets, with <paramref name="extra"/> rules applied before the dictionary's (the settings page listens to a word
    /// before it is saved).
    /// </summary>
    public async Task<string> PrepareAsync(string text, string language, IReadOnlyList<PronunciationRule> extra, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(extra);
        var culture = OutputLanguages.Of(language).Culture;
        var rules = await RulesAsync(language, cancellationToken).ConfigureAwait(false);
        if (extra.Count > 0)
        {
            var taken = extra.Select(r => Key(r, culture)).ToHashSet(StringComparer.Ordinal);
            rules = [.. extra, .. rules.Where(r => !taken.Contains(Key(r, culture)))];
        }

        var normalizer = _normalizers.FirstOrDefault(n => n.Supports(language));
        var spoken = PronunciationRewriter.Rewrite(text, rules, culture, normalizer is null ? null : normalizer.Join);
        return normalizer is null ? spoken : normalizer.Normalize(spoken);
    }

    /// <summary>A rule of the user's entry: "!WORD" is matched only as written.</summary>
    public static PronunciationRule Own(PronunciationEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        var word = entry.Word.Trim();
        var exact = word.Length > 1 && word[0] == PronunciationRule.ExactMark;
        return new PronunciationRule(exact ? word[1..].Trim() : word, entry.Spoken, entry, exact);
    }

    /// <summary>What makes two rules the same word (a user's word of the same key replaces a built-in one).</summary>
    public static string Key(PronunciationRule rule, System.Globalization.CultureInfo culture)
    {
        ArgumentNullException.ThrowIfNull(rule);
        return rule.ExactCase ? PronunciationRule.ExactMark + rule.Word.Trim() : PronunciationRewriter.Fold(rule.Word.Trim(), culture);
    }

    /// <summary>
    /// Adds the user's word (or changes the spoken form of the user's word of the same spelling); null when it was saved, otherwise the
    /// reason ("empty", "long").
    /// </summary>
    public async Task<string?> AddAsync(string language, string word, string spoken, CancellationToken cancellationToken)
    {
        var cleanWord = (word ?? string.Empty).Trim();
        var cleanSpoken = (spoken ?? string.Empty).Trim();
        if (cleanWord.Length == 0 || cleanSpoken.Length == 0 || !cleanWord.Any(PronunciationRewriter.IsWordCharacter))
        {
            return "empty";
        }

        if (cleanWord.Length > WordMax || cleanSpoken.Length > WordMax)
        {
            return "long";
        }

        var culture = OutputLanguages.Of(language).Culture;
        var folded = PronunciationRewriter.Fold(cleanWord, culture);
        var existing = (await store.ListAsync(language, cancellationToken).ConfigureAwait(false))
            .FirstOrDefault(e => PronunciationRewriter.Fold(e.Word, culture) == folded);
        var entry = existing ?? new PronunciationEntry { Language = language };
        entry.Word = cleanWord;
        entry.Spoken = cleanSpoken;
        await store.SaveAsync(entry, cancellationToken).ConfigureAwait(false);

        // The word is the user's text: only its length goes to the log.
        logger.LogInformation("Pronunciation {Action} for {Language} ({Length} characters)", existing is null ? "added" : "changed", language, cleanWord.Length);
        return null;
    }

    /// <summary>Removes the user's word (a built-in one of the same spelling applies again).</summary>
    public async Task DeleteAsync(PronunciationEntry entry, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(entry);
        await store.DeleteAsync(entry.Id, cancellationToken).ConfigureAwait(false);
        logger.LogInformation("Pronunciation removed for {Language}", entry.Language);
    }
}
