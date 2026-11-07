using Microsoft.Extensions.Logging;
using TMConcept.Core.Profiles;

namespace TMConcept.Core.Pronunciation;

/// <summary>
/// The pronunciation dictionary of each output language: the built-in words (<see cref="BuiltInPronunciations"/>) and the user's own (the
/// user's word of the same spelling replaces a built-in one). Every narration is put through it right before a voice speaks it
/// (<see cref="Speech.SpeechService"/>); subtitles and the narration file keep the original text.
/// </summary>
public sealed class PronunciationService(IPronunciationStore store, ILogger<PronunciationService> logger)
{
    /// <summary>Most characters of a word or its spoken form.</summary>
    public const int WordMax = 60;

    /// <summary>Every rule of a language: the user's words first, then the built-in ones they do not replace.</summary>
    public async Task<IReadOnlyList<PronunciationRule>> RulesAsync(string language, CancellationToken cancellationToken)
    {
        var culture = OutputLanguages.Of(language).Culture;
        var own = (await store.ListAsync(language, cancellationToken).ConfigureAwait(false))
            .Select(e => new PronunciationRule(e.Word, e.Spoken, e)).ToList();
        var taken = own.Select(r => PronunciationRewriter.Fold(r.Word, culture)).ToHashSet(StringComparer.Ordinal);
        return [.. own, .. BuiltInPronunciations.Of(language).Where(r => !taken.Contains(PronunciationRewriter.Fold(r.Word, culture)))];
    }

    /// <summary>The text the voice gets for a narration in <paramref name="language"/>.</summary>
    public async Task<string> PrepareAsync(string text, string language, CancellationToken cancellationToken)
    {
        var rules = await RulesAsync(language, cancellationToken).ConfigureAwait(false);
        return PronunciationRewriter.Rewrite(text, rules, OutputLanguages.Of(language).Culture);
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
