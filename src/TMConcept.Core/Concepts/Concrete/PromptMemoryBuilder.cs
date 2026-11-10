using System.Globalization;
using System.Text;
using TMConcept.Core.Duplicates;
using TMConcept.Core.Profiles;
using TMConcept.Core.Text;

namespace TMConcept.Core.Concepts;

/// <summary>
/// Writes a profile's past for the prompts, locally (no model call; 10 Nov 2026, the developer: "with 1 000 videos, adding 1 000 lines is
/// silly"). 0.1.9 listed up to 1 500 titles one by one (about 25 000 tokens a call); now:
/// <list type="bullet">
/// <item>Covered: every concept of the profile except the listed topic dislikes. The newest <see cref="PromptMemoryLimits.CoveredNewest"/>
/// titles one per line ("[video]" = made into a video); the older ones condensed into at most <see cref="PromptMemoryLimits.CoveredThemeLines"/>
/// lines: a series as "Nika Riot — Part 1–4", then themes as "Vikings ×12: example; example" (the word most of the remaining titles share,
/// picked again and again, so "Hagia Sophia" is found wherever it stands in a title; a word of the channel content or one most titles share,
/// such as "Ottoman" on an Ottoman channel, names no theme), then single titles. The whole block is cut to
/// <see cref="PromptMemoryLimits.CoveredMaxCharacters"/>; the newest titles leave <see cref="PromptMemoryLimits.CoveredOlderReserve"/> of it
/// to the condensed lines, so long titles do not push the older history out. A trashed concept without a reason is only covered.</item>
/// <item>Topic dislikes (🗑 "I did not like the topic"): the newest <see cref="PromptMemoryLimits.DislikesEach"/> titles; older ones are
/// covered.</item>
/// <item>Telling dislikes (🗑 "I did not like the telling"): the newest titles with the opening sentence of their narration (the hook, which
/// shows the telling; the summary when there is no content). Their topic was done, so they are covered too.</item>
/// <item>Wishes: the kept AI-edit instructions, each on one line, cut to <see cref="PromptMemoryLimits.WishMax"/>.</item>
/// </list>
/// The local duplicate check stays the hard guard against repeats; this only steers the model.
/// </summary>
public static class PromptMemoryBuilder
{
    /// <summary>Words shorter than this never name a theme.</summary>
    private const int ThemeWordMin = 3;

    /// <summary>Folded words are compared by this many first letters (a rough stem: "vikingler" and "viking" are one theme).</summary>
    private const int StemLength = 6;

    private static readonly char[] WordBreaks = [' ', ',', ':', ';', '—', '–', '-', '?', '!', '.', '\'', '’', '"', '(', ')', '/', '|'];

    /// <summary>
    /// Builds the memory of <paramref name="concepts"/> (any order; the content is needed only for the telling dislikes) and
    /// <paramref name="wishes"/> (newest first); the words of <paramref name="channelContent"/> name no theme.
    /// </summary>
    public static PromptMemory Build(IReadOnlyList<Concept> concepts, IReadOnlyList<string> wishes, string contentLanguage, string? channelContent = null)
    {
        ArgumentNullException.ThrowIfNull(concepts);
        ArgumentNullException.ThrowIfNull(wishes);
        var language = OutputLanguages.Of(contentLanguage);
        var newestFirst = concepts.OrderByDescending(c => c.Number).ToList();
        var trashed = newestFirst.Where(c => c.Status == ConceptStatus.Trashed)
            .OrderByDescending(c => c.DecidedAtUtc ?? c.CreatedAtUtc).ThenByDescending(c => c.Number).ToList();
        var topicDislikes = trashed.Where(c => c.TrashReason == TrashReason.Topic).Take(PromptMemoryLimits.DislikesEach).ToList();
        var tellingDislikes = trashed.Where(c => c.TrashReason == TrashReason.Telling).Take(PromptMemoryLimits.DislikesEach).ToList();
        var listed = topicDislikes.Select(c => c.Id).ToHashSet();
        var covered = newestFirst.Where(c => !listed.Contains(c.Id)).ToList();

        return new PromptMemory(
            covered.Count == 0 ? null : Covered(covered, language, channelContent),
            covered.Count,
            List(topicDislikes.Select(c => PromptText.Line(c.Title, ConceptLimits.TitleMax))),
            List(tellingDislikes.Select(c => PromptText.Line(c.Title, ConceptLimits.TitleMax) + ": " + PromptText.Line(Opening(c), PromptMemoryLimits.DislikeSummaryMax))),
            List(wishes.Select(w => "«" + PromptText.Line(w, PromptMemoryLimits.WishMax) + "»").Where(w => w.Length > 2)));
    }

    /// <summary>The covered block of <paramref name="newestFirst"/>: newest titles, then the condensed older ones, cut to the cap.</summary>
    private static string Covered(List<Concept> newestFirst, OutputLanguage language, string? channelContent)
    {
        var newest = newestFirst.Take(PromptMemoryLimits.CoveredNewest)
            .Select(c => (c.Status == ConceptStatus.Produced ? "[video] " : string.Empty) + PromptText.Line(c.Title, ConceptLimits.TitleMax)).ToList();
        var older = Condense(newestFirst.Skip(PromptMemoryLimits.CoveredNewest).Select(c => PromptText.Line(c.Title, ConceptLimits.TitleMax)).ToList(), language,
            channelContent);

        // The newest titles leave room for the condensed older lines (with 80-character titles the 40 newest alone would fill the cap), then
        // the older lines are cut from their end until the whole block fits.
        var newestBudget = PromptMemoryLimits.CoveredMaxCharacters - Math.Min(Length(older), PromptMemoryLimits.CoveredOlderReserve);
        while (newest.Count > 0 && Length(newest) > newestBudget)
        {
            newest.RemoveAt(newest.Count - 1);
        }

        while (older.Count > 0 && Length(newest) + Length(older) > PromptMemoryLimits.CoveredMaxCharacters)
        {
            older.RemoveAt(older.Count - 1);
        }

        return List(newest.Concat(older)) ?? string.Empty;
    }

    private static int Length(List<string> lines) => lines.Sum(l => l.Length + 3);

    /// <summary>The first sentence the narration says (the [on screen: …] lines and marks left out); the summary when there is none.</summary>
    private static string Opening(Concept concept)
    {
        var spoken = new StringBuilder();
        var depth = 0;
        foreach (var ch in concept.Content ?? string.Empty)
        {
            if (ch == '[')
            {
                depth++;
            }
            else if (ch == ']' && depth > 0)
            {
                depth--;
            }
            else if (depth == 0)
            {
                spoken.Append(char.IsWhiteSpace(ch) ? ' ' : ch);
                if ((ch is '.' or '!' or '?' or '…') && spoken.ToString().Trim().Length > 1)
                {
                    break;
                }
            }
        }

        var opening = string.Join(' ', spoken.ToString().Split(' ', StringSplitOptions.RemoveEmptyEntries));
        return opening.Length > 0 ? opening : concept.Summary;
    }

    /// <summary>
    /// The older titles (newest first) in at most <see cref="PromptMemoryLimits.CoveredThemeLines"/> lines: series, themes with counts,
    /// then single titles. A word of <paramref name="channelContent"/> or one shared by more than
    /// <see cref="PromptMemoryLimits.ThemeShareMax"/> percent of the titles names no theme: on a single-subject channel it would take in
    /// nearly every title and tell the model nothing.
    /// </summary>
    public static List<string> Condense(IReadOnlyList<string> olderNewestFirst, OutputLanguage language, string? channelContent = null)
    {
        ArgumentNullException.ThrowIfNull(olderNewestFirst);
        ArgumentNullException.ThrowIfNull(language);
        var lines = new List<string>();
        var split = olderNewestFirst.Where(t => t.Length > 0).Select(t => (Title: t, Split: DuplicateCheck.SplitPart(t))).ToList();
        foreach (var series in split.Where(s => s.Split.Part > 0).GroupBy(s => TurkishFold.Fold(s.Split.BaseTitle)).OrderByDescending(g => g.Count()))
        {
            var parts = series.Select(s => s.Split.Part).Distinct().Order().ToList();
            lines.Add(string.Create(CultureInfo.InvariantCulture, $"{series.First().Split.BaseTitle} — {language.PartWord} {Ranges(parts)}"));
        }

        // The filler words of the language and the English ones (as the word duplicate check uses them; titles often mix both).
        var stop = language.StopWords.Concat(ContentLanguages.EnglishStopWords).Select(TurkishFold.Fold).ToHashSet(StringComparer.Ordinal);
        var rest = split.Where(s => s.Split.Part == 0).Select(s => (s.Title, Stems: Stems(s.Title, stop))).ToList();
        var skip = Stems(channelContent ?? string.Empty, stop).Keys.ToHashSet(StringComparer.Ordinal);
        if (rest.Count >= PromptMemoryLimits.ThemeShareMinTitles)
        {
            skip.UnionWith(rest.SelectMany(r => r.Stems.Keys).GroupBy(k => k, StringComparer.Ordinal)
                .Where(g => g.Count() * 100 > rest.Count * PromptMemoryLimits.ThemeShareMax).Select(g => g.Key));
        }

        while (lines.Count < PromptMemoryLimits.CoveredThemeLines && rest.Count > 1)
        {
            var best = rest.SelectMany(r => r.Stems.Keys).Where(k => !skip.Contains(k)).GroupBy(s => s, StringComparer.Ordinal)
                .Select(g => (Stem: g.Key, Count: g.Count()))
                // Ties: the longer stem (more specific: "sophia" before "year"), then the alphabet (the same lines every time).
                .OrderByDescending(g => g.Count).ThenByDescending(g => g.Stem.Length).ThenBy(g => g.Stem, StringComparer.Ordinal).FirstOrDefault();
            if (best.Count < 2)
            {
                break;
            }

            var group = rest.Where(r => r.Stems.ContainsKey(best.Stem)).ToList();
            var word = group.Select(r => r.Stems[best.Stem]).GroupBy(w => w, StringComparer.Ordinal)
                .OrderByDescending(g => g.Count()).ThenBy(g => g.Key.Length).First().Key;
            var examples = string.Join("; ", group.Take(2).Select(r => Short(r.Title)));
            lines.Add(string.Create(CultureInfo.InvariantCulture, $"{word} ×{group.Count}: {examples}"));
            rest.RemoveAll(r => r.Stems.ContainsKey(best.Stem));
        }

        lines.AddRange(rest.Take(Math.Max(0, PromptMemoryLimits.CoveredThemeLines - lines.Count)).Select(r => r.Title));
        return lines.Take(PromptMemoryLimits.CoveredThemeLines).ToList();
    }

    /// <summary>The meaningful words of a title by their stem (folded, first <see cref="StemLength"/> letters), each with its first spelling.</summary>
    private static Dictionary<string, string> Stems(string title, HashSet<string> stop)
    {
        var stems = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var word in title.Split(WordBreaks, StringSplitOptions.RemoveEmptyEntries))
        {
            var folded = TurkishFold.Fold(word);
            if (folded.Length < ThemeWordMin || stop.Contains(folded) || folded.All(char.IsAsciiDigit))
            {
                continue;
            }

            stems.TryAdd(folded.Length > StemLength ? folded[..StemLength] : folded, word);
        }

        return stems;
    }

    private static string Short(string title) =>
        title.Length > PromptMemoryLimits.ThemeExampleMax ? title[..PromptMemoryLimits.ThemeExampleMax].TrimEnd() + "…" : title;

    /// <summary>Lines as one value: each on its own line after "- ", starting with a line break (so it follows its heading); <c>null</c> when none.</summary>
    private static string? List(IEnumerable<string> lines)
    {
        var text = new StringBuilder();
        foreach (var line in lines.Where(l => l.Length > 0))
        {
            text.Append("\n- ").Append(line);
        }

        return text.Length == 0 ? null : text.ToString();
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
