using System.Text;
using System.Text.Json;

namespace TMConcept.Speech.Embeddings;

/// <summary>
/// The SentencePiece unigram tokenizer of an XLM-RoBERTa vocabulary, read from a Hugging Face <c>tokenizer.json</c>: text normalized with
/// NFKC (close to the model's precompiled character map), split at white space, each word with "▁" in front, then the most likely split into
/// vocabulary pieces (Viterbi over the piece scores; an unknown character becomes the unknown piece). Wrapped in &lt;s&gt; … &lt;/s&gt; and cut
/// to <see cref="MaxTokens"/>, like the model was trained.
/// </summary>
internal sealed class UnigramTokenizer
{
    private const string Space = "▁";
    private readonly Dictionary<string, (int Id, double Score)> _pieces;
    private readonly int _longest;
    private readonly double _unknownScore;

    private UnigramTokenizer(Dictionary<string, (int Id, double Score)> pieces, int unknown, int start, int end)
    {
        _pieces = pieces;
        Unknown = unknown;
        Start = start;
        End = end;
        _longest = pieces.Keys.Max(k => k.Length);
        _unknownScore = pieces.Values.Min(v => v.Score) - 10;
    }

    /// <summary>Most tokens per text (the model's maximum sequence length).</summary>
    public const int MaxTokens = 128;

    /// <summary>Id of the unknown piece.</summary>
    public int Unknown { get; }

    /// <summary>Id of &lt;s&gt;.</summary>
    public int Start { get; }

    /// <summary>Id of &lt;/s&gt;.</summary>
    public int End { get; }

    /// <summary>Reads a <c>tokenizer.json</c> with a unigram model.</summary>
    public static UnigramTokenizer Load(string file)
    {
        using var document = JsonDocument.Parse(File.ReadAllBytes(file));
        var model = document.RootElement.GetProperty("model");
        if (model.GetProperty("type").GetString() != "Unigram")
        {
            throw new InvalidDataException("The tokenizer is not a unigram tokenizer.");
        }

        var pieces = new Dictionary<string, (int, double)>(StringComparer.Ordinal);
        var id = 0;
        foreach (var entry in model.GetProperty("vocab").EnumerateArray())
        {
            pieces.TryAdd(entry[0].GetString() ?? string.Empty, (id, entry[1].GetDouble()));
            id++;
        }

        var unknown = model.TryGetProperty("unk_id", out var u) && u.ValueKind == JsonValueKind.Number ? u.GetInt32() : 3;
        int Special(string token) => pieces.TryGetValue(token, out var value) ? value.Item1 : throw new InvalidDataException("The tokenizer has no " + token + ".");
        return new UnigramTokenizer(pieces, unknown, Special("<s>"), Special("</s>"));
    }

    /// <summary>The ids of <paramref name="text"/> with &lt;s&gt; and &lt;/s&gt;.</summary>
    public long[] Encode(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        var ids = new List<long> { Start };
        foreach (var word in text.Normalize(NormalizationForm.FormKC).Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries))
        {
            ids.AddRange(Pieces(Space + word));
            if (ids.Count >= MaxTokens - 1)
            {
                break;
            }
        }

        if (ids.Count > MaxTokens - 1)
        {
            ids.RemoveRange(MaxTokens - 1, ids.Count - (MaxTokens - 1));
        }

        ids.Add(End);
        return [.. ids];
    }

    /// <summary>The best split of one word (Viterbi over the piece scores).</summary>
    private List<long> Pieces(string word)
    {
        var best = new double[word.Length + 1];
        var from = new int[word.Length + 1];
        var piece = new int[word.Length + 1];
        Array.Fill(best, double.NegativeInfinity);
        best[0] = 0;
        for (var end = 1; end <= word.Length; end++)
        {
            for (var start = Math.Max(0, end - _longest); start < end; start++)
            {
                if (double.IsNegativeInfinity(best[start]))
                {
                    continue;
                }

                if (_pieces.TryGetValue(word[start..end], out var found) && best[start] + found.Score > best[end])
                {
                    best[end] = best[start] + found.Score;
                    from[end] = start;
                    piece[end] = found.Id;
                }
            }

            if (double.IsNegativeInfinity(best[end]) && !double.IsNegativeInfinity(best[end - 1]))
            {
                // No piece ends here: the character is unknown.
                best[end] = best[end - 1] + _unknownScore;
                from[end] = end - 1;
                piece[end] = Unknown;
            }
        }

        var ids = new List<long>();
        for (var at = word.Length; at > 0; at = from[at])
        {
            // Consecutive unknown characters are one unknown piece.
            if (piece[at] == Unknown && ids.Count > 0 && ids[^1] == Unknown)
            {
                continue;
            }

            ids.Add(piece[at]);
        }

        ids.Reverse();
        return ids;
    }
}
