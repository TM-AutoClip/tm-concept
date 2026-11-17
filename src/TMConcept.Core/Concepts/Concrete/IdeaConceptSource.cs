namespace TMConcept.Core.Concepts;

/// <summary>"Fikirden" (<see cref="IdeaBrief"/>): the idea as the extra request and a count; never any topics.</summary>
public sealed class IdeaConceptSource : ConceptSourceBase<IdeaBrief>
{
    /// <inheritdoc />
    public override ConceptSourceKind Kind => ConceptSourceKind.Idea;

    /// <inheritdoc />
    protected override Task<ConceptSourceBatch> PrepareAsync(IdeaBrief brief, ConceptSourceRequest request, CancellationToken cancellationToken) =>
        Task.FromResult(new ConceptSourceBatch(brief.ExtraPrompt, brief.Topics(request.Offset, request.Size)));
}
