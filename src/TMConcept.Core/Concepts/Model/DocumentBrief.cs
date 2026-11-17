using TMConcept.Core.Documents;

namespace TMConcept.Core.Concepts;

/// <summary>
/// "Belgelerden" on the concept page (0.1.21): concepts that rest only on the profile's documents. <see cref="Subject"/> (optional) narrows
/// them; <see cref="DocumentIds"/> picks documents (empty = every ready document of the profile). Without topics it writes
/// <see cref="Requested"/> concepts (1–<see cref="DocumentLimits.ConceptCountMax"/>); with <see cref="TopicList"/> (the ticked topics "Find
/// topics" found in the documents) exactly one concept per topic, in order, each from the passages about it.
/// </summary>
public sealed record DocumentBrief : ConceptBrief
{
    /// <summary>Creates the brief.</summary>
    public DocumentBrief(string subject, int requested, IReadOnlyList<Guid> documentIds, IReadOnlyList<string>? topicList = null)
    {
        ArgumentNullException.ThrowIfNull(documentIds);
        if (topicList is not null)
        {
            ArgumentOutOfRangeException.ThrowIfLessThan(topicList.Count, ConceptLimits.CountMin, nameof(topicList));
            ArgumentOutOfRangeException.ThrowIfGreaterThan(topicList.Count, DocumentLimits.ConceptCountMax, nameof(topicList));
        }

        Subject = (subject ?? string.Empty).Trim();
        Requested = requested;
        DocumentIds = documentIds.ToList();
        TopicList = topicList?.ToList();
    }

    /// <summary>The subject (empty = what the documents hold).</summary>
    public string Subject { get; }

    /// <summary>How many concepts without topics.</summary>
    public int Requested { get; }

    /// <summary>The chosen documents (empty = all ready ones of the profile).</summary>
    public IReadOnlyList<Guid> DocumentIds { get; }

    /// <summary>The ticked topics, one concept each (null = none).</summary>
    public IReadOnlyList<string>? TopicList { get; }

    /// <inheritdoc />
    public override int Count => TopicList is { Count: > 0 } topics ? topics.Count : Math.Clamp(Requested, ConceptLimits.CountMin, DocumentLimits.ConceptCountMax);

    /// <inheritdoc />
    public override string ExtraPrompt => Subject;

    /// <inheritdoc />
    public override bool Grounded => true;

    /// <inheritdoc />
    public override IReadOnlyList<string>? Topics(int offset, int size) => TopicList?.Skip(offset).Take(size).ToList();
}
