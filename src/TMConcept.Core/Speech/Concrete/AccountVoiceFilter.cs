namespace TMConcept.Core.Speech;

/// <summary>
/// Rule 11 for account voices: a voice that does not know the output language is not listed in it. The provider's language list decides
/// when it has one (ElevenLabs' <c>verified_languages</c>, else its <c>labels.language</c>). A voice the provider says nothing about IS
/// listed: ElevenLabs' models offered here (v4, v4 Turbo, Flash v2.5, Multilingual v2) are multilingual and speak every content language
/// with any voice, and hiding a voice the user made (a clone usually has no language) would leave them unable to use it. Decision of
/// 0.1.47, reversible: drop the "unknown" case here to list only voices verified in the language.
/// </summary>
public static class AccountVoiceFilter
{
    /// <summary>Whether <paramref name="voice"/> is listed for <paramref name="language"/> (a two-letter content language code).</summary>
    public static bool Speaks(AccountVoice voice, string language)
    {
        ArgumentNullException.ThrowIfNull(voice);
        if (voice.Languages.Count == 0)
        {
            return true;
        }

        return voice.Languages.Any(l => string.Equals(Code(l), Code(language), StringComparison.Ordinal));
    }

    /// <summary>The two-letter part of a language or locale ("en-US" → "en"), lower case.</summary>
    public static string Code(string? language)
    {
        var value = (language ?? string.Empty).Trim().ToLowerInvariant();
        var cut = value.IndexOfAny(['-', '_']);
        return cut > 0 ? value[..cut] : value;
    }
}
