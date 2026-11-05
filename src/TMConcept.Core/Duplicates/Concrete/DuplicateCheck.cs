using System.Globalization;
using System.Text.RegularExpressions;
using TMConcept.Core.Text;

namespace TMConcept.Core.Duplicates;

/// <summary>
/// Local, model-free duplicate check (layer 2 of the duplicate prevention; the history list in the prompt is layer 1). Every new concept is
/// compared with the whole history of the profile (new, approved, produced and trashed concepts; permanently deleted ones are gone) and with
/// the concepts accepted before it in the same batch, so an idea that comes twice in one answer is dropped the second time.
/// <list type="bullet">
/// <item>Similarity: average of the title word-set Jaccard and the title letter-trigram Dice (Turkish folded, filler words dropped, the part
/// marker removed), mixed with the summary words when both summaries are long enough.</item>
/// <item>Series rule: the same subject with different part numbers ("… — Part 2" vs "… — Part 3") is a continuation, not a duplicate.</item>
/// </list>
/// </summary>
public static partial class DuplicateCheck
{
    private static readonly HashSet<string> Filler = new[]
    {
        "ve", "ile", "bir", "bu", "su", "o", "da", "de", "mi", "mu", "icin", "gibi", "en", "ne", "nasil", "neden", "kim", "hangi",
        "olan", "oldu", "cok", "daha", "ki", "ya", "veya", "ama", "her", "hakkinda", "nedir", "kimdi", "bolum", "kisim", "seri",
        "the", "a", "an", "and", "or", "of", "in", "on", "to", "for", "with", "by", "is", "was", "how", "why", "what", "who", "part", "episode",
    }.ToHashSet(StringComparer.Ordinal);

    /// <summary>Features of a concept.</summary>
    public static DuplicateFeatures Features(DuplicateSubject subject)
    {
        ArgumentNullException.ThrowIfNull(subject);
        var (baseTitle, part) = SplitPart(subject.Title);
        var folded = TurkishFold.Fold(baseTitle);
        return new DuplicateFeatures
        {
            Subject = subject,
            BaseTitle = folded,
            Words = Words(folded),
            Trigrams = Trigrams(folded),
            SummaryWords = Words(TurkishFold.Fold(subject.Summary)),
            Part = part,
        };
    }

    /// <summary>Compares a new concept with one earlier concept.</summary>
    public static DuplicateResult Compare(DuplicateFeatures candidate, DuplicateFeatures earlier)
    {
        ArgumentNullException.ThrowIfNull(candidate);
        ArgumentNullException.ThrowIfNull(earlier);
        var title = TitleSimilarity(candidate, earlier);
        var summary = candidate.SummaryWords.Count >= 3 && earlier.SummaryWords.Count >= 3 ? Jaccard(candidate.SummaryWords, earlier.SummaryWords) : -1;
        var similarity = summary < 0 || candidate.BaseTitle == earlier.BaseTitle
            ? title
            : (title * (1 - DuplicateThresholds.SummaryWeight)) + (summary * DuplicateThresholds.SummaryWeight);
        var other = earlier.Subject;

        var sameSeries = candidate.BaseTitle.Length > 0 && (candidate.BaseTitle == earlier.BaseTitle
            || Dice(candidate.Trigrams, earlier.Trigrams) >= DuplicateThresholds.SameSeries);
        if (sameSeries && candidate.Part > 0 && earlier.Part > 0)
        {
            return candidate.Part == earlier.Part
                ? new DuplicateResult(DuplicateKind.Duplicate, Math.Max(similarity, 0.9), other.Id, other.Number, other.Title)
                : new DuplicateResult(DuplicateKind.Continuation, similarity, other.Id, other.Number, other.Title);
        }

        if (similarity >= DuplicateThresholds.Duplicate)
        {
            // A part number on one side only ("X" and "X — Part 2") is a continuation.
            return candidate.Part != earlier.Part && (candidate.Part > 1 || earlier.Part > 1)
                ? new DuplicateResult(DuplicateKind.Continuation, similarity, other.Id, other.Number, other.Title)
                : new DuplicateResult(DuplicateKind.Duplicate, similarity, other.Id, other.Number, other.Title);
        }

        return similarity >= DuplicateThresholds.Similar
            ? new DuplicateResult(DuplicateKind.Similar, similarity, other.Id, other.Number, other.Title)
            : DuplicateResult.None with { Similarity = similarity };
    }

    /// <summary>The heaviest result against a list (the most similar of the same weight).</summary>
    public static DuplicateResult Check(DuplicateFeatures candidate, IEnumerable<DuplicateFeatures> earlier)
    {
        ArgumentNullException.ThrowIfNull(earlier);
        var best = DuplicateResult.None;
        foreach (var e in earlier)
        {
            var result = Compare(candidate, e);
            if ((int)result.Kind > (int)best.Kind || (result.Kind == best.Kind && result.Kind != DuplicateKind.New && result.Similarity > best.Similarity))
            {
                best = result;
            }

            if (best.Kind == DuplicateKind.Duplicate && best.Similarity >= 0.999)
            {
                break;
            }
        }

        return best;
    }

    /// <summary>Checks a batch in order; an accepted concept joins the history for the ones after it.</summary>
    public static IReadOnlyList<DuplicateVerdict> Sift(IEnumerable<DuplicateSubject> candidates, IEnumerable<DuplicateSubject> history)
    {
        ArgumentNullException.ThrowIfNull(candidates);
        ArgumentNullException.ThrowIfNull(history);
        var pool = history.Select(Features).ToList();
        var verdicts = new List<DuplicateVerdict>();
        foreach (var candidate in candidates)
        {
            var features = Features(candidate);
            var result = Check(features, pool);
            verdicts.Add(new DuplicateVerdict(candidate, result));
            if (result.Accepted)
            {
                pool.Add(features);
            }
        }

        return verdicts;
    }

    /// <summary>
    /// Separates the part marker from a title: "Nika Riot — Part 2" → ("Nika Riot", 2); "Nika İsyanı 2. bölüm", "Bölüm 3", "Episode 4",
    /// "(5. kısım)", "#6". No marker → (title, 0).
    /// </summary>
    public static (string BaseTitle, int Part) SplitPart(string? title)
    {
        var text = (title ?? string.Empty).Trim();
        foreach (var regex in new[] { PartAfter(), PartBefore() })
        {
            var match = regex.Match(text);
            if (!match.Success || !int.TryParse(match.Groups["no"].Value, NumberStyles.None, CultureInfo.InvariantCulture, out var number)
                || number is <= 0 or > 10_000)
            {
                continue;
            }

            var baseTitle = (text[..match.Index] + " " + text[(match.Index + match.Length)..]).Trim(' ', '-', '–', '—', ':', '|', ',', '(', ')');
            return (baseTitle.Length > 0 ? baseTitle : text, number);
        }

        return (text, 0);
    }

    private static HashSet<string> Words(string folded) =>
        folded.Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Where(w => w.Length >= 2 && !Filler.Contains(w))
            .Select(w => w.All(char.IsDigit) || w.Length <= 6 ? w : w[..6])
            .ToHashSet(StringComparer.Ordinal);

    private static HashSet<string> Trigrams(string folded)
    {
        var padded = " " + folded.Replace(" ", "  ", StringComparison.Ordinal) + " ";
        var set = new HashSet<string>(StringComparer.Ordinal);
        for (var i = 0; i + 3 <= padded.Length; i++)
        {
            set.Add(padded.Substring(i, 3));
        }

        return set;
    }

    private static double TitleSimilarity(DuplicateFeatures a, DuplicateFeatures b)
    {
        if (a.BaseTitle.Length == 0 || b.BaseTitle.Length == 0)
        {
            return 0;
        }

        return a.BaseTitle == b.BaseTitle ? 1 : (Jaccard(a.Words, b.Words) + Dice(a.Trigrams, b.Trigrams)) / 2;
    }

    private static double Jaccard(HashSet<string> a, HashSet<string> b)
    {
        if (a.Count == 0 || b.Count == 0)
        {
            return 0;
        }

        var common = a.Count(b.Contains);
        return (double)common / (a.Count + b.Count - common);
    }

    private static double Dice(HashSet<string> a, HashSet<string> b)
    {
        if (a.Count == 0 || b.Count == 0)
        {
            return 0;
        }

        var common = a.Count(b.Contains);
        return 2.0 * common / (a.Count + b.Count);
    }

    [GeneratedRegex(@"[\s\-–—:|,(]*(?<no>\d{1,5})\s*(?:\.|'?(?:st|nd|rd|th))?\s*(?:bölüm|bolum|kısım|kisim|part|episode)\b\)?", RegexOptions.IgnoreCase)]
    private static partial Regex PartAfter();

    [GeneratedRegex(@"[\s\-–—:|,(]*(?:bölüm|bolum|kısım|kisim|part|episode|ep\.?|#)\s*(?<no>\d{1,5})\b\)?", RegexOptions.IgnoreCase)]
    private static partial Regex PartBefore();
}
