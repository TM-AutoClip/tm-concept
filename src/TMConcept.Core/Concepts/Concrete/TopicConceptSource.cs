namespace TMConcept.Core.Concepts;

/// <summary>"Konu öner" (<see cref="TopicBrief"/>): exactly one concept per ticked topic, in order; no extra request.</summary>
public sealed class TopicConceptSource : ConceptSourceBase<TopicBrief>
{
    /// <inheritdoc />
    public override ConceptSourceKind Kind => ConceptSourceKind.Topics;

    /// <inheritdoc />
    protected override Task<ConceptSourceBatch> PrepareAsync(TopicBrief brief, ConceptSourceRequest request, CancellationToken cancellationToken) =>
        Task.FromResult(new ConceptSourceBatch(brief.ExtraPrompt, brief.Topics(request.Offset, request.Size)));
}
