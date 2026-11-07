using System.Text;
using System.Text.RegularExpressions;
using TMConcept.Core.Profiles;
using TMConcept.Core.Text;

namespace TMConcept.Core.Markers;

/// <summary>
/// Markers in a text: which ones a text uses (both languages' spellings, any letter case, "0,5" or "0.5"), the legend a prompt gets for
/// them, inserting one at the caret, and a narration text split into spoken parts with their pauses and speeds.
/// </summary>
public static partial class MarkerText
{
    /// <summary>The markers of <paramref name="kind"/> that the texts use, in catalog order.</summary>
    public static IReadOnlyList<Marker> Used(MarkerKind kind, params string?[] texts)
    {
        var found = new HashSet<string>(StringComparer.Ordinal);
        foreach (var text in texts)
        {
            foreach (Match match in Bracket().Matches(text ?? string.Empty))
            {
                if (Find(match.Value) is { } marker && marker.Kind == kind)
                {
                    found.Add(marker.Id);
                }
            }
        }

        return MarkerCatalog.All.Where(m => found.Contains(m.Id)).ToList();
    }

    /// <summary>The marker a bracket text stands for ("[Sert geçiş]", "[hard cut]"); null when it is none of them.</summary>
    public static Marker? Find(string bracket)
    {
        var key = Key(bracket);
        return MarkerCatalog.All.FirstOrDefault(m => Key(m.Token.Tr) == key || Key(m.Token.En) == key);
    }

    /// <summary>
    /// The legend of the prompt markers a prompt's user texts use, in <paramref name="language"/> (the prompt's own language): each marker in
    /// both spellings with what it means; null when none is used (the prompt line is left out).
    /// </summary>
    public static string? Legend(string language, params string?[] texts)
    {
        var used = Used(MarkerKind.Prompt, texts);
        if (used.Count == 0)
        {
            return null;
        }

        var turkish = OutputLanguages.Of(language).PromptLanguage == ContentLanguages.Turkish;
        return string.Join("; ", used.Select(m => m.Token.Tr + " / " + m.Token.En + " = " + (turkish ? m.Meaning.Tr : m.Meaning.En)));
    }

    /// <summary>
    /// <paramref name="token"/> put into <paramref name="text"/> at the caret (replacing the selected text, when there is a selection), with a
    /// space on each side where a word touches it; returns the new text and the caret after the token.
    /// </summary>
    public static (string Text, int Caret) Insert(string? text, int selectionStart, int selectionEnd, string token)
    {
        ArgumentException.ThrowIfNullOrEmpty(token);
        var value = text ?? string.Empty;
        var start = Math.Clamp(Math.Min(selectionStart, selectionEnd), 0, value.Length);
        var end = Math.Clamp(Math.Max(selectionStart, selectionEnd), 0, value.Length);
        var before = value[..start];
        var after = value[end..];
        var insert = (before.Length > 0 && !char.IsWhiteSpace(before[^1]) ? " " : string.Empty) + token
            + (after.Length > 0 && !char.IsWhiteSpace(after[0]) ? " " : string.Empty);
        return (before + insert + after, before.Length + insert.Length);
    }

    /// <summary>The text without any bracket text (what is spoken and shown as subtitles), spaces collapsed.</summary>
    public static string Spoken(string? text) => Spaces().Replace(Bracket().Replace(text ?? string.Empty, " "), " ").Trim();

    /// <summary>
    /// A scene's edited narration as spoken parts: the text between markers, each with the speed in force (<see cref="MarkerCatalog.Slow"/>
    /// and the others multiply the voice's own speed until the next speed marker) and the silence of the pause markers after it (a pause
    /// at the start is a silent part). Bracket text that is no narration marker is not spoken; it is returned in
    /// <paramref name="ignored"/> so the caller can say so.
    /// </summary>
    public static IReadOnlyList<NarrationPart> Parts(string? text, out IReadOnlyList<string> ignored)
    {
        var parts = new List<NarrationPart>();
        var skipped = new List<string>();
        var speed = 1.0;
        var pending = new StringBuilder();
        void Flush()
        {
            var spoken = Spaces().Replace(pending.ToString(), " ").Trim();
            pending.Clear();
            if (spoken.Length > 0)
            {
                parts.Add(new NarrationPart(spoken, speed, 0));
            }
        }

        var value = text ?? string.Empty;
        var at = 0;
        foreach (Match match in Bracket().Matches(value))
        {
            pending.Append(value, at, match.Index - at);
            at = match.Index + match.Length;
            var marker = Find(match.Value);
            if (marker is not { Kind: MarkerKind.Narration })
            {
                skipped.Add(match.Value);
                pending.Append(' ');
                continue;
            }

            Flush();
            if (marker.PauseMs > 0)
            {
                if (parts.Count == 0)
                {
                    parts.Add(new NarrationPart(string.Empty, speed, marker.PauseMs));
                }
                else
                {
                    parts[^1] = parts[^1] with { PauseAfterMs = parts[^1].PauseAfterMs + marker.PauseMs };
                }
            }

            if (marker.Speed > 0)
            {
                speed = marker.Speed;
            }
        }

        pending.Append(value, at, value.Length - at);
        Flush();
        ignored = skipped;
        return parts;
    }

    /// <summary>A bracket text for matching: folded letters (Turkish and case), single spaces, "," as ".".</summary>
    private static string Key(string bracket) =>
        Spaces().Replace(TurkishFold.Fold(bracket.Trim().TrimStart('[').TrimEnd(']')).Replace(',', '.'), " ").Trim();

    [GeneratedRegex(@"\[[^\[\]\n]*\]")]
    private static partial Regex Bracket();

    [GeneratedRegex(@"\s+")]
    private static partial Regex Spaces();
}
