using TMConcept.Core.Documents;

namespace TMConcept.Core.Concepts;

/// <summary>
/// "Belgelerden" on the concept page (0.1.21): concepts that rest only on the profile's documents. <see cref="Subject"/> (optional) narrows
/// them; <see cref="DocumentIds"/> picks documents (empty = every ready document of the profile). Without topics it writes
/// <see cref="Requested"/> concepts (1–<see cref="DocumentLimits.ConceptCountMax"/>); with <see cref="TopicList"/> (the ticked topics "Find
/// topics" found in the documents) exactly one concept per topic, in order, each from the passages about it: a found topic keeps its
/// documents and its search words (<see cref="Topic"/>), so it is searched where it was found. <see cref="Phase"/>: where the passages of a
/// run without a subject start in each document (0–1; the page picks another one at every press, so a second press reads other parts).
/// </summary>
public sealed record DocumentBrief : ConceptBrief
{
    private readonly IReadOnlyList<DocumentTopic>? _topics;

    /// <summary>Creates the brief with topics given by their text only.</summary>
    public DocumentBrief(string subject, int requested, IReadOnlyList<Guid> documentIds, IReadOnlyList<string>? topicList = null)
        : this(subject, requested, documentIds, topicList?.Select(t => new DocumentTopic(t, [])).ToList())
    {
    }

    /// <summary>Creates the brief with found topics (their documents and search words go along).</summary>
    public DocumentBrief(string subject, int requested, IReadOnlyList<Guid> documentIds, IReadOnlyList<DocumentTopic>? topics)
    {
        ArgumentNullException.ThrowIfNull(documentIds);
        if (topics is not null)
        {
            ArgumentOutOfRangeException.ThrowIfLessThan(topics.Count, ConceptLimits.CountMin, nameof(topics));
            ArgumentOutOfRangeException.ThrowIfGreaterThan(topics.Count, DocumentLimits.ConceptCountMax, nameof(topics));
        }

        Subject = (subject ?? string.Empty).Trim();
        Requested = requested;
        DocumentIds = documentIds.ToList();
        _topics = topics?.ToList();
        TopicList = _topics?.Select(t => t.Text).ToList();
    }

    /// <summary>The subject (empty = what the documents hold).</summary>
    public string Subject { get; }

    /// <summary>How many concepts without topics.</summary>
    public int Requested { get; }

    /// <summary>The chosen documents (empty = all ready ones of the profile).</summary>
    public IReadOnlyList<Guid> DocumentIds { get; }

    /// <summary>The ticked topics, one concept each (null = none).</summary>
    public IReadOnlyList<string>? TopicList { get; }

    /// <summary>Where a run without a subject starts reading each document (0–1).</summary>
    public double Phase { get; init; }

    /// <summary>Only the passages on these pages of the chosen PDFs are used (0.1.24; null = every page; passages without a page always count).</summary>
    public DocumentPageRange? Pages { get; init; }

    /// <summary>
    /// The notes of "read everything" (0.1.24, <see cref="DocumentDigester"/>): the concepts are grounded in them instead of the stored
    /// passages (null = the passages, as before).
    /// </summary>
    public IReadOnlyList<DocumentChunk>? Digest { get; init; }

    /// <inheritdoc />
    public override int Count => TopicList is { Count: > 0 } topics ? topics.Count : Math.Clamp(Requested, ConceptLimits.CountMin, DocumentLimits.ConceptCountMax);

    /// <inheritdoc />
    public override string ExtraPrompt => Subject;

    /// <inheritdoc />
    public override bool Grounded => true;

    /// <inheritdoc />
    public override IReadOnlyList<string>? Topics(int offset, int size) => TopicList?.Skip(offset).Take(size).ToList();

    /// <summary>The found topic at <paramref name="index"/> with its documents and search words; null outside the list or without topics.</summary>
    public DocumentTopic? Topic(int index) => _topics is { } topics && index >= 0 && index < topics.Count ? topics[index] : null;
}
