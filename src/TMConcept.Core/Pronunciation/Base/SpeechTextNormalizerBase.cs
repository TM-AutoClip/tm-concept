using System.Text.RegularExpressions;

namespace TMConcept.Core.Pronunciation;

/// <summary>
/// The shared part of a language's normalizer: the language code, a step that replaces the matches of a pattern (a step returns null to
/// keep a match as written) and the spacing a reading needs when it replaces something glued to a word ("25km" → "25 kilometre").
/// </summary>
public abstract class SpeechTextNormalizerBase : ISpeechTextNormalizer
{
    /// <summary>The output language code this normalizer handles.</summary>
    protected abstract string Language { get; }

    /// <inheritdoc />
    public bool Supports(string language) => string.Equals(language, Language, StringComparison.OrdinalIgnoreCase);

    /// <inheritdoc />
    public string Normalize(string text) => string.IsNullOrWhiteSpace(text) ? text ?? string.Empty : Steps(text);

    /// <inheritdoc />
    public virtual string Join(string spoken, string suffix)
    {
        ArgumentNullException.ThrowIfNull(spoken);
        return string.IsNullOrEmpty(suffix) ? spoken : spoken + "'" + suffix;
    }

    /// <summary>Every step of the language over non-empty text.</summary>
    protected abstract string Steps(string text);

    /// <summary>Replaces each match with what <paramref name="reading"/> returns for it (null keeps the match).</summary>
    protected static string Replace(string text, Regex pattern, Func<Match, string, string?> reading)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(pattern);
        ArgumentNullException.ThrowIfNull(reading);
        return pattern.Replace(text, m => reading(m, text) ?? m.Value);
    }

    /// <summary>A space in front of the reading when the match is glued to what is before it.</summary>
    protected static string SpaceBefore(string text, int index, string reading)
    {
        ArgumentNullException.ThrowIfNull(text);
        return index > 0 && !char.IsWhiteSpace(text[index - 1]) ? " " + reading : reading;
    }

    /// <summary>A space after the reading when a letter or digit follows the match.</summary>
    protected static string SpaceAfter(string text, int end, string reading)
    {
        ArgumentNullException.ThrowIfNull(text);
        return end < text.Length && char.IsLetterOrDigit(text[end]) ? reading + " " : reading;
    }

    /// <summary>Whether a sentence ends after <paramref name="end"/>: the end of the text, a new line, or spaces and a capital letter.</summary>
    protected static bool SentenceEndsAt(string text, int end)
    {
        ArgumentNullException.ThrowIfNull(text);
        if (end >= text.Length)
        {
            return true;
        }

        if (!char.IsWhiteSpace(text[end]))
        {
            return false;
        }

        var i = end;
        while (i < text.Length && char.IsWhiteSpace(text[i]))
        {
            if (text[i] == '\n')
            {
                return true;
            }

            i++;
        }

        return i >= text.Length || char.IsUpper(text[i]);
    }

    /// <summary>Whether the next word after <paramref name="end"/> starts with a capital letter.</summary>
    protected static bool NextWordIsCapital(string text, int end)
    {
        ArgumentNullException.ThrowIfNull(text);
        var i = end;
        while (i < text.Length && char.IsWhiteSpace(text[i]))
        {
            i++;
        }

        return i < text.Length && char.IsUpper(text[i]);
    }
}
