using TMConcept.Core.Text;

namespace TMConcept.Core.Research;

/// <summary>
/// The fixed plan of a research run, without any model: the languages read are the profile's, English, the ones the user added and the
/// ones the subject points at (<see cref="ResearchLanguageHints"/>), at most <see cref="ResearchLimits.LanguagesMax"/>, in that order. Only
/// the profile's language and English are searched directly (a Turkish subject finds little on the Greek Wikipedia); the other languages are
/// read through the found articles' language links, so the same entity's Greek article is fetched by its own title. A per-language backend
/// (Wikipedia, Wikidata) gets one query per searched language, any other backend one query in the profile's language; the addresses
/// backend gets the user's addresses.
/// </summary>
public static class ResearchPlanner
{
    /// <summary>The plan of <paramref name="request"/> over <paramref name="backends"/>.</summary>
    public static ResearchPlan Plan(ResearchRequest request, IReadOnlyList<IResearchBackend> backends)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(backends);
        var subject = TextClean.OneLine(request.Subject, ResearchLimits.SubjectMax);
        var languages = Languages(subject, request.Language, request.ExtraLanguages);
        var searched = languages.Take(ResearchLimits.SearchLanguagesMax).ToList();
        var urls = request.Urls.Select(u => u.Trim()).Where(u => u.Length > 0).Distinct(StringComparer.Ordinal).Take(ResearchLimits.UrlsMax).ToList();
        var queries = new List<ResearchPlannedQuery>();
        foreach (var backend in backends)
        {
            if (backend.Kind == ResearchBackendKind.Url)
            {
                if (urls.Count > 0)
                {
                    queries.Add(new ResearchPlannedQuery(backend.Id, new ResearchQuery(subject, languages[0], ResearchLimits.UrlsMax) { Urls = urls }));
                }

                continue;
            }

            if (subject.Length == 0)
            {
                continue;
            }

            foreach (var language in backend.PerLanguage ? searched : [languages[0]])
            {
                queries.Add(new ResearchPlannedQuery(backend.Id, new ResearchQuery(subject, language, ResearchLimits.HitsPerQuery)));
            }
        }

        return new ResearchPlan(subject, languages, searched, queries);
    }

    /// <summary>
    /// The languages read for <paramref name="subject"/> of a profile in <paramref name="language"/>: that language, English, the user's
    /// <paramref name="extra"/> ones, then the subject's (<see cref="ResearchLanguageHints"/>, in the table's order), each once, at most
    /// <see cref="ResearchLimits.LanguagesMax"/>.
    /// </summary>
    public static IReadOnlyList<string> Languages(string subject, string language, IReadOnlyList<string> extra)
    {
        ArgumentNullException.ThrowIfNull(extra);
        var result = new List<string>();
        void Add(string? code)
        {
            if (code is not null && !result.Contains(code, StringComparer.Ordinal) && result.Count < ResearchLimits.LanguagesMax)
            {
                result.Add(code);
            }
        }

        Add(ResearchText.Language(language) ?? "en");
        Add("en");
        foreach (var code in extra)
        {
            Add(ResearchText.Language(code));
        }

        foreach (var code in Hinted(subject))
        {
            Add(code);
        }

        return result;
    }

    /// <summary>The languages the words of <paramref name="subject"/> point at, in the table's order.</summary>
    public static IReadOnlyList<string> Hinted(string subject)
    {
        var folded = TurkishFold.Fold(subject);
        var words = folded.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return ResearchLanguageHints.Table.Where(entry => entry.Words.Any(hint => Matches(folded, words, hint))).Select(entry => entry.Language).ToList();
    }

    private static bool Matches(string folded, string[] words, string hint) => hint.EndsWith('*')
        ? words.Any(w => w.StartsWith(hint[..^1], StringComparison.Ordinal))
        : hint.Contains(' ', StringComparison.Ordinal)
            ? (" " + folded + " ").Contains(" " + hint + " ", StringComparison.Ordinal)
            : words.Contains(hint, StringComparer.Ordinal);
}
