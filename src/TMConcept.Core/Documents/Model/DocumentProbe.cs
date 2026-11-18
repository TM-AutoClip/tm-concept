namespace TMConcept.Core.Documents;

/// <summary>
/// What a quick look at a file found before it is added (0.1.24): its pages (0 = not a paged document), its own title (empty = none), and of
/// the <see cref="Sampled"/> pages looked at (spread over the document), how many have no text layer (a scan).
/// </summary>
public sealed record DocumentProbe(int Pages, string Title, int Sampled, int SampledWithoutText)
{
    /// <summary>Nothing known (not a paged document).</summary>
    public static DocumentProbe None { get; } = new(0, string.Empty, 0, 0);

    /// <summary>Whether at least one sampled page has no text: a text extraction may be worth offering.</summary>
    public bool HasImagePages => SampledWithoutText > 0;

    /// <summary>About how many of <paramref name="pages"/> pages have no text (the sampled share; an estimate).</summary>
    public int ImagePagesAbout(int pages) => Sampled == 0 ? 0 : (int)Math.Round(pages * (double)SampledWithoutText / Sampled);
}
