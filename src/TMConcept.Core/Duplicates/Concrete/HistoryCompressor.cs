using System.Globalization;
using TMConcept.Core.Text;

namespace TMConcept.Core.Duplicates;

/// <summary>
/// Layer 4: keeps the history part of the prompt a fixed size. Up to <see cref="Threshold"/> titles (and about 25k tokens) every title is
/// listed. Above that the newest <see cref="NewestFull"/> titles are listed in full and the older ones are grouped locally (no model): a
/// series becomes "Nika Riot: parts 1–4 done", other titles are grouped by their first meaningful word with a count and a few examples.
/// </summary>
public static class HistoryCompressor
{
    /// <summary>Titles listed one by one up to this count.</summary>
    public const int Threshold = 1500;

    /// <summary>Newest titles kept in full when compressing.</summary>
    public const int NewestFull = 300;

    /// <summary>About 25k tokens of titles.</summary>
    public const int MaxCharacters = 75_000;

    private static readonly HashSet<string> Filler = new(StringComparer.Ordinal)
    {
        "the", "a", "an", "of", "and", "in", "on", "how", "why", "what", "who", "when", "bir", "ve", "ile", "nasil", "neden", "kim", "ne", "bu", "o",
    };

    /// <summary>History lines (oldest first; the caller adds "- ").</summary>
    public static IReadOnlyList<string> Lines(IReadOnlyList<string> titles)
    {
        ArgumentNullException.ThrowIfNull(titles);
        if (titles.Count <= Threshold && titles.Sum(t => t.Length + 3) <= MaxCharacters)
        {
            return titles;
        }

        var older = titles.Take(Math.Max(0, titles.Count - NewestFull)).ToList();
        var lines = new List<string>();
        var series = older.Select(t => (Title: t, Split: DuplicateCheck.SplitPart(t))).ToList();
        foreach (var group in series.Where(s => s.Split.Part > 0).GroupBy(s => TurkishFold.Fold(s.Split.BaseTitle)))
        {
            var parts = group.Select(s => s.Split.Part).Distinct().Order().ToList();
            lines.Add(string.Create(CultureInfo.InvariantCulture, $"{group.First().Split.BaseTitle}: parts {Ranges(parts)} done"));
        }

        foreach (var group in series.Where(s => s.Split.Part == 0).GroupBy(s => Key(s.Title)).OrderByDescending(g => g.Count()))
        {
            var examples = string.Join("; ", group.Take(3).Select(s => s.Title.Length > 60 ? s.Title[..60] + "…" : s.Title));
            lines.Add(group.Count() == 1 ? group.First().Title : string.Create(CultureInfo.InvariantCulture, $"{group.Key}: {group.Count()} topics (e.g. {examples})"));
        }

        lines.AddRange(titles.Skip(older.Count));
        return lines;
    }

    private static string Key(string title)
    {
        var words = TurkishFold.Fold(title).Split([' ', ',', ':', '—', '-', '?', '!', '.', '\''], StringSplitOptions.RemoveEmptyEntries);
        return words.FirstOrDefault(w => w.Length > 2 && !Filler.Contains(w)) ?? words.FirstOrDefault() ?? "?";
    }

    private static string Ranges(List<int> parts)
    {
        var ranges = new List<string>();
        for (var i = 0; i < parts.Count; i++)
        {
            var start = parts[i];
            while (i + 1 < parts.Count && parts[i + 1] == parts[i] + 1)
            {
                i++;
            }

            ranges.Add(start == parts[i] ? start.ToString(CultureInfo.InvariantCulture) : string.Create(CultureInfo.InvariantCulture, $"{start}–{parts[i]}"));
        }

        return string.Join(", ", ranges);
    }
}
