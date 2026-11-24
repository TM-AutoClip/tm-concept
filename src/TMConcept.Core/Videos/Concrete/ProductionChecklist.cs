namespace TMConcept.Core.Videos;

/// <summary>
/// The list of what is missing before production (F16, 0.1.49; ReelMimic's "every input is given or skipped on purpose", our rules): a
/// template and a video code model must be chosen and every chosen online model's provider must have a key (required: "Produce" is off until
/// they are); the narration being off, the template's own music file missing or unreadable, and an empty pronunciation dictionary are named
/// and can be fixed or skipped (optional). A skipped music gap makes the video without its music; the others change nothing. With the
/// defaults (a template, a code model with a key, a voice) only the pronunciation suggestion may show, and nothing is turned off.
/// </summary>
public static class ProductionChecklist
{
    /// <summary>The gaps of <paramref name="facts"/>, required ones first.</summary>
    public static IReadOnlyList<ProductionGap> Gaps(ChecklistFacts facts)
    {
        ArgumentNullException.ThrowIfNull(facts);
        var gaps = new List<ProductionGap>();
        if (!facts.Template)
        {
            gaps.Add(new ProductionGap(ProductionGaps.Template, GapKind.Required));
        }

        if (!facts.CodeModel)
        {
            gaps.Add(new ProductionGap(ProductionGaps.CodeModel, GapKind.Required));
        }

        gaps.AddRange(facts.ProvidersWithoutKey.Distinct(StringComparer.Ordinal).Select(p => new ProductionGap(ProductionGaps.Key, GapKind.Required, p)));
        if (!facts.Voice)
        {
            gaps.Add(new ProductionGap(ProductionGaps.Voice, GapKind.Optional));
        }

        if (facts.OwnMusic && !facts.MusicExists)
        {
            gaps.Add(new ProductionGap(ProductionGaps.MusicMissing, GapKind.Optional));
        }
        else if (facts.OwnMusic && facts.MusicReadable == false)
        {
            gaps.Add(new ProductionGap(ProductionGaps.MusicUnreadable, GapKind.Optional));
        }

        if (facts.Voice && facts.PronunciationEmpty)
        {
            gaps.Add(new ProductionGap(ProductionGaps.Pronunciation, GapKind.Optional));
        }

        return gaps;
    }

    /// <summary>"Produce" is off: a required gap is open (optional ones never turn it off, skipped or not).</summary>
    public static bool Blocks(IReadOnlyList<ProductionGap> gaps)
    {
        ArgumentNullException.ThrowIfNull(gaps);
        return gaps.Any(g => g.Kind == GapKind.Required);
    }

    /// <summary>The gaps still shown: required ones always, optional ones until they are skipped (by code).</summary>
    public static IReadOnlyList<ProductionGap> Open(IReadOnlyList<ProductionGap> gaps, IReadOnlySet<string> skipped)
    {
        ArgumentNullException.ThrowIfNull(gaps);
        ArgumentNullException.ThrowIfNull(skipped);
        return gaps.Where(g => g.Kind == GapKind.Required || !skipped.Contains(Key(g))).ToList();
    }

    /// <summary>The key a skip is kept under (the code, with the detail when there is one).</summary>
    public static string Key(ProductionGap gap)
    {
        ArgumentNullException.ThrowIfNull(gap);
        return gap.Detail.Length == 0 ? gap.Code : gap.Code + ":" + gap.Detail;
    }

    /// <summary>
    /// A music gap of <paramref name="gaps"/> that was skipped: the video is made without its template's music (this video's snapshot only; the
    /// template keeps its file). True when the snapshot was changed.
    /// </summary>
    public static bool WithoutMusic(Styles.VideoStyle? snapshot, IReadOnlyList<ProductionGap> gaps, IReadOnlySet<string> skipped)
    {
        ArgumentNullException.ThrowIfNull(gaps);
        ArgumentNullException.ThrowIfNull(skipped);
        if (snapshot?.Music is not { Source: Styles.MusicSource.OwnFile } music
            || !gaps.Any(g => g.Code is ProductionGaps.MusicMissing or ProductionGaps.MusicUnreadable && skipped.Contains(Key(g))))
        {
            return false;
        }

        music.Source = Styles.MusicSource.Off;
        return true;
    }
}
