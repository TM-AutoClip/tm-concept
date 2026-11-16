using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace TMConcept.Speech.AhenkCep;

/// <summary>
/// Ahenk Cep's text side, a C# port of its front end (site/js/front-native.js of github.com/ozguradmin/ahenk-turkish-tts, Apache-2.0,
/// which copies EMA Lightning's frontend.py, chunker.py and Engine.piece): typographic quotes and dashes made plain, Turkish lower case,
/// other letters without their accents, every character the model does not know turned into a space (<see cref="Alphabet"/>); pieces of at
/// most 250 characters (fewer at a slow speed: 18 letters per second, 10 s) cut at the last sentence end, else the last clause mark, else
/// the last word that fits, with 0.25 s after a sentence and 0.12 s after a clause (<see cref="Chunk"/>); a piece without a full stop gets
/// one (<see cref="Finish"/>); ids, word of each character and word starts (<see cref="Piece"/>). The text arrives already in its spoken
/// form (the app's Turkish normalizer reads numbers and abbreviations), so trnorm itself is not ported.
/// </summary>
internal static partial class AhenkCepText
{
    /// <summary>Letters per second the piece length is planned with.</summary>
    public const double LettersPerSecond = 18.0;

    /// <summary>Longest piece in seconds (at speed 1).</summary>
    public const double MaxSeconds = 10.0;

    /// <summary>Longest piece in characters.</summary>
    public const int MaxLetters = 250;

    /// <summary>Pause after a sentence, in seconds.</summary>
    public const double SentencePause = 0.25;

    /// <summary>Pause after a clause, in seconds.</summary>
    public const double ClausePause = 0.12;

    private const string Turkish = "çğıöşüÇĞİÖŞÜ";

    private static readonly Dictionary<string, int> Ids = AhenkCepModel.Vocabulary.Select((c, i) => (c, i))
        .GroupBy(p => p.c, StringComparer.Ordinal).ToDictionary(g => g.Key, g => g.Last().i, StringComparer.Ordinal);

    /// <summary>The pieces of a scene's spoken text at <paramref name="speed"/>.</summary>
    public static IReadOnlyList<AhenkCepPiece> Pieces(string spoken, double speed)
    {
        ArgumentNullException.ThrowIfNull(spoken);
        var pieces = new List<AhenkCepPiece>();
        foreach (var (text, pause) in Chunk(Alphabet(spoken), speed))
        {
            var (ids, words, starts) = Piece(text);
            pieces.Add(new AhenkCepPiece(text, pause, ids, words, starts));
        }

        return pieces;
    }

    /// <summary>
    /// The text in the model's characters: typography made plain, Turkish lower case (I → ı, İ → i), Turkish letters kept and other letters
    /// without their accents, any character the model does not know a space, spaces collapsed.
    /// </summary>
    public static string Alphabet(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        var plain = new StringBuilder(text.Length);
        foreach (var c in text)
        {
            plain.Append(c switch
            {
                '’' or '‘' or 'ʼ' or '´' or '`' => "'",
                '“' or '”' or '„' or '«' or '»' => "\"",
                '–' or '—' or '−' => "-",
                '…' => "...",
                'İ' => "i",
                'I' => "ı",
                _ => char.IsControl(c) ? " " : c.ToString(),
            });
        }

        var lower = plain.ToString().ToLower(CultureInfo.InvariantCulture);
        var output = new StringBuilder(lower.Length);
        foreach (var rune in lower.EnumerateRunes())
        {
            output.Append(Letter(rune.ToString()));
        }

        return Spaces().Replace(output.ToString(), " ").Trim();
    }

    /// <summary>
    /// The pieces of <paramref name="text"/> (already in the model's characters) with the pause after each; the last piece has none.
    /// </summary>
    public static IReadOnlyList<(string Text, double Pause)> Chunk(string text, double speed)
    {
        ArgumentNullException.ThrowIfNull(text);
        var limit = (int)Math.Min(MaxLetters, LettersPerSecond * MaxSeconds * Math.Max(speed, 0.1));
        var pieces = new List<(string Text, double Pause)>();
        var rest = text.Trim();
        while (rest.Length > 0)
        {
            var cut = rest.Length;
            var pause = 0.0;
            if (rest.Length > limit)
            {
                cut = limit;
                var window = rest[..Math.Min(limit + 1, rest.Length)];
                foreach (var (pattern, gap) in new[] { (SentenceCut(), SentencePause), (ClauseCut(), ClausePause), (WordCut(), ClausePause) })
                {
                    var end = -1;
                    foreach (Match m in pattern.Matches(window))
                    {
                        end = m.Index + m.Length;
                    }

                    if (end >= 0)
                    {
                        cut = end;
                        pause = gap;
                        break;
                    }
                }
            }

            var piece = rest[..cut].Trim();
            rest = rest[cut..].Trim();
            if (piece.Any(char.IsLetter))
            {
                pieces.Add((Finish(piece), pause));
            }
        }

        if (pieces.Count > 0)
        {
            pieces[^1] = (pieces[^1].Text, 0.0);
        }

        return pieces;
    }

    /// <summary>A piece ends with a full stop, a question or an exclamation mark (one is added, trailing commas and dashes go).</summary>
    public static string Finish(string piece)
    {
        ArgumentNullException.ThrowIfNull(piece);
        var bare = piece.TrimEnd('"', '\'', ')');
        return bare.Length > 0 && bare[^1] is '.' or '!' or '?' ? piece : piece.TrimEnd(',', ';', ':', '-', ' ') + ".";
    }

    /// <summary>The id of each character, the word it belongs to and where that word starts.</summary>
    public static (long[] Ids, long[] Words, long[] WordStarts) Piece(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        var ids = new long[text.Length];
        var starts = new List<int>();
        for (var i = 0; i < text.Length; i++)
        {
            ids[i] = Ids.TryGetValue(text[i].ToString(), out var id) ? id : 1;
            if (text[i] != ' ' && (i == 0 || text[i - 1] == ' '))
            {
                starts.Add(i);
            }
        }

        if (starts.Count == 0)
        {
            starts.Add(0);
        }

        var bounds = new List<int> { 0 };
        bounds.AddRange(starts.Skip(1));
        bounds.Add(text.Length);
        var words = new long[text.Length];
        var wordStarts = new long[text.Length];
        for (var w = 0; w + 1 < bounds.Count; w++)
        {
            for (var i = bounds[w]; i < bounds[w + 1]; i++)
            {
                words[i] = w;
                wordStarts[i] = bounds[w];
            }
        }

        return (ids, words, wordStarts);
    }

    private static string Letter(string character)
    {
        if (!Turkish.Contains(character, StringComparison.Ordinal))
        {
            var decomposed = character.Normalize(NormalizationForm.FormKD);
            var kept = new StringBuilder(decomposed.Length);
            foreach (var c in decomposed)
            {
                if (CharUnicodeInfo.GetUnicodeCategory(c) is not (UnicodeCategory.NonSpacingMark or UnicodeCategory.SpacingCombiningMark
                    or UnicodeCategory.EnclosingMark))
                {
                    kept.Append(c);
                }
            }

            character = kept.ToString();
        }

        if (character.Length == 0)
        {
            return " ";
        }

        foreach (var c in character)
        {
            if (!Ids.ContainsKey(c.ToString()))
            {
                return " ";
            }
        }

        return character;
    }

    [GeneratedRegex(@"[.!?]+[""')]*(?= )", RegexOptions.CultureInvariant)]
    private static partial Regex SentenceCut();

    [GeneratedRegex(@"[,;:](?= )", RegexOptions.CultureInvariant)]
    private static partial Regex ClauseCut();

    [GeneratedRegex(@"\S(?= )", RegexOptions.CultureInvariant)]
    private static partial Regex WordCut();

    [GeneratedRegex(@"\s+", RegexOptions.CultureInvariant)]
    private static partial Regex Spaces();
}
