using System.Globalization;
using System.Text;
using System.Text.Json;

namespace TMConcept.Speech.Chatterbox;

/// <summary>
/// The grapheme BPE tokenizer of Chatterbox Multilingual V3, read from its <c>tokenizer.json</c>, doing what the Hugging Face tokenizers library
/// and upstream's MTLTokenizer do (V3's text_preproc "NFKD,fullcase"): NFKD, "[&lt;language&gt;]" in front, spaces as "[SPACE]"; then the
/// added tokens are cut out (leftmost, longest), the rest is normalized (NFKD, space → [SPACE]), split like the Whitespace pre-tokenizer
/// (<c>\w+|[^\w\s]+</c>), merged by rank (BPE) and an unknown character becomes [UNK]; the template puts &lt;EXAGGERATION&gt; &lt;s&gt; in front and
/// &lt;/s&gt; &lt;START_SPEECH&gt; &lt;START_SPEECH&gt; after. V3 is trained on text as written, so nothing is lowercased (V2 lowercased with
/// Python's str.lower(), which turns "I" into "i" also in Turkish; not needed here). Turkish letters survive NFKD as a base letter and a
/// combining mark ("ş" = s + U+0327, "İ" = I + U+0307; "ı" has no decomposition), all in the vocabulary.
/// </summary>
internal sealed class ChatterboxTokenizer
{
    private const string SpaceToken = "[SPACE]";
    private readonly Dictionary<string, int> _vocab;
    private readonly Dictionary<(string, string), int> _ranks;
    private readonly List<(string Content, int Id, bool Normalized)> _added;
    private readonly long[] _prefix;
    private readonly long[] _suffix;
    private readonly int _unknown;
    private readonly Dictionary<string, int[]> _cache = new(StringComparer.Ordinal);

    private ChatterboxTokenizer(Dictionary<string, int> vocab, Dictionary<(string, string), int> ranks, List<(string, int, bool)> added, long[] prefix,
        long[] suffix, int unknown)
    {
        _vocab = vocab;
        _ranks = ranks;

        // Longest first: a match at the same place takes the longest added token (the library's leftmost-longest search).
        _added = added.OrderByDescending(a => a.Item1.Length).ToList();
        _prefix = prefix;
        _suffix = suffix;
        _unknown = unknown;
    }

    /// <summary>Reads a <c>tokenizer.json</c> with a BPE model and a template post-processor.</summary>
    public static ChatterboxTokenizer Load(string file)
    {
        using var document = JsonDocument.Parse(File.ReadAllBytes(file));
        var root = document.RootElement;
        var model = root.GetProperty("model");
        if (model.GetProperty("type").GetString() != "BPE")
        {
            throw new InvalidDataException("The Chatterbox tokenizer is not a BPE tokenizer.");
        }

        var vocab = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var entry in model.GetProperty("vocab").EnumerateObject())
        {
            vocab[entry.Name] = entry.Value.GetInt32();
        }

        var ranks = new Dictionary<(string, string), int>();
        var rank = 0;
        foreach (var merge in model.GetProperty("merges").EnumerateArray())
        {
            var (left, right) = merge.ValueKind == JsonValueKind.Array
                ? (merge[0].GetString() ?? string.Empty, merge[1].GetString() ?? string.Empty)
                : Split(merge.GetString() ?? string.Empty);
            ranks.TryAdd((left, right), rank++);
        }

        var added = new List<(string, int, bool)>();
        foreach (var token in root.GetProperty("added_tokens").EnumerateArray())
        {
            added.Add((token.GetProperty("content").GetString() ?? string.Empty, token.GetProperty("id").GetInt32(),
                token.TryGetProperty("normalized", out var n) && n.ValueKind == JsonValueKind.True));
        }

        var processor = root.GetProperty("post_processor");
        var specials = processor.GetProperty("special_tokens");
        var prefix = new List<long>();
        var suffix = new List<long>();
        var seenSequence = false;
        foreach (var piece in processor.GetProperty("single").EnumerateArray())
        {
            if (piece.TryGetProperty("Sequence", out _))
            {
                seenSequence = true;
                continue;
            }

            var id = piece.GetProperty("SpecialToken").GetProperty("id").GetString() ?? string.Empty;
            foreach (var value in specials.GetProperty(id).GetProperty("ids").EnumerateArray())
            {
                (seenSequence ? suffix : prefix).Add(value.GetInt64());
            }
        }

        var unknownName = model.TryGetProperty("unk_token", out var u) ? u.GetString() ?? "[UNK]" : "[UNK]";
        var unknown = vocab.TryGetValue(unknownName, out var unk) ? unk : throw new InvalidDataException("The tokenizer has no unknown token.");
        return new ChatterboxTokenizer(vocab, ranks, added, prefix.ToArray(), suffix.ToArray(), unknown);

        static (string, string) Split(string merge)
        {
            var space = merge.IndexOf(' ', StringComparison.Ordinal);
            return space < 0 ? (merge, string.Empty) : (merge[..space], merge[(space + 1)..]);
        }
    }

    /// <summary>The token ids of a (punctuation-normalized) text in <paramref name="language"/>, with the template's special tokens.</summary>
    public long[] Encode(string text, string language)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(language);

        // Upstream: preprocess (NFKD) → "[lang]" + text → spaces as [SPACE] → tokenizer.
        var prepared = "[" + language.ToLowerInvariant() + "]" + text.Normalize(NormalizationForm.FormKD);
        prepared = prepared.Replace(" ", SpaceToken, StringComparison.Ordinal);
        var ids = new List<long>(_prefix);
        foreach (var (segment, special) in Cut(prepared, normalized: false))
        {
            if (special >= 0)
            {
                ids.Add(special);
                continue;
            }

            // The tokenizer's own normalizer (NFKD, space → [SPACE]); then the normalized added tokens ([SPACE]) are cut out of it.
            var normal = segment.Normalize(NormalizationForm.FormKD).Replace(" ", SpaceToken, StringComparison.Ordinal);
            foreach (var (piece, inner) in Cut(normal, normalized: true))
            {
                if (inner >= 0)
                {
                    ids.Add(inner);
                    continue;
                }

                foreach (var word in Words(piece))
                {
                    ids.AddRange(Bpe(word).Select(i => (long)i));
                }
            }
        }

        ids.AddRange(_suffix);
        return ids.ToArray();
    }

    /// <summary>
    /// The Whitespace pre-tokenizer: runs of word characters, and runs of characters that are neither word characters nor white space.
    /// Word characters as the library's regex has them (Unicode \w: letters, marks, decimal digits, connector punctuation, letter numbers),
    /// so a letter keeps its combining marks after NFKD.
    /// </summary>
    internal static List<string> Words(string text)
    {
        var words = new List<string>();
        var builder = new StringBuilder();
        var kind = 0;
        foreach (var rune in text.EnumerateRunes())
        {
            // 0: white space (ends a word, kept out), 1: word character, 2: anything else.
            var current = Rune.IsWhiteSpace(rune) ? 0 : IsWord(rune) ? 1 : 2;
            if (current != kind && builder.Length > 0)
            {
                words.Add(builder.ToString());
                builder.Clear();
            }

            if (current != 0)
            {
                builder.Append(rune.ToString());
            }

            kind = current;
        }

        if (builder.Length > 0)
        {
            words.Add(builder.ToString());
        }

        return words;
    }

    private static bool IsWord(Rune rune) => Rune.GetUnicodeCategory(rune) switch
    {
        UnicodeCategory.UppercaseLetter or UnicodeCategory.LowercaseLetter or UnicodeCategory.TitlecaseLetter or UnicodeCategory.ModifierLetter
            or UnicodeCategory.OtherLetter or UnicodeCategory.NonSpacingMark or UnicodeCategory.SpacingCombiningMark or UnicodeCategory.EnclosingMark
            or UnicodeCategory.DecimalDigitNumber or UnicodeCategory.ConnectorPunctuation or UnicodeCategory.LetterNumber => true,
        _ => false,
    };

    /// <summary>Cuts the added tokens (of one kind: matched before or after normalization) out of a text: (text, -1) or ("", id) pieces.</summary>
    private List<(string Text, int Id)> Cut(string text, bool normalized)
    {
        var pieces = new List<(string, int)>();
        var from = 0;
        var i = 0;
        while (i < text.Length)
        {
            var match = text[i] == '[' || text[i] == '<'
                ? _added.FirstOrDefault(a => a.Normalized == normalized && string.CompareOrdinal(text, i, a.Content, 0, a.Content.Length) == 0
                    && a.Content.Length > 0)
                : default;
            if (match.Content is { Length: > 0 })
            {
                if (i > from)
                {
                    pieces.Add((text[from..i], -1));
                }

                pieces.Add((string.Empty, match.Id));
                i += match.Content.Length;
                from = i;
                continue;
            }

            i++;
        }

        if (from < text.Length)
        {
            pieces.Add((text[from..], -1));
        }

        return pieces;
    }

    private int[] Bpe(string word)
    {
        if (_cache.TryGetValue(word, out var cached))
        {
            return cached;
        }

        var parts = word.EnumerateRunes().Select(r => r.ToString()).ToList();
        while (parts.Count > 1)
        {
            var best = -1;
            var bestRank = int.MaxValue;
            for (var i = 0; i < parts.Count - 1; i++)
            {
                if (_ranks.TryGetValue((parts[i], parts[i + 1]), out var rank) && rank < bestRank)
                {
                    bestRank = rank;
                    best = i;
                }
            }

            if (best < 0)
            {
                break;
            }

            // Every occurrence of the best pair is merged in one pass, left to right (as the library does).
            var pair = (parts[best], parts[best + 1]);
            var merged = new List<string>(parts.Count);
            for (var i = 0; i < parts.Count; i++)
            {
                if (i < parts.Count - 1 && (parts[i], parts[i + 1]) == pair)
                {
                    merged.Add(parts[i] + parts[i + 1]);
                    i++;
                }
                else
                {
                    merged.Add(parts[i]);
                }
            }

            parts = merged;
        }

        var ids = parts.Select(p => _vocab.TryGetValue(p, out var id) ? id : _unknown).ToArray();
        _cache[word] = ids;
        return ids;
    }
}
