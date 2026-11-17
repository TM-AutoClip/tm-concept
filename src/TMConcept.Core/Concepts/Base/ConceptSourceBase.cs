using TMConcept.Core.Models;

namespace TMConcept.Core.Concepts;

/// <summary>
/// The common part of a concept source: it handles exactly one brief type (<typeparamref name="TBrief"/>), uses the batch size the answer
/// limit allows unless it says otherwise, and fills a batch from its own brief.
/// </summary>
public abstract class ConceptSourceBase<TBrief> : IConceptSource
    where TBrief : ConceptBrief
{
    /// <inheritdoc />
    public abstract ConceptSourceKind Kind { get; }

    /// <inheritdoc />
    public bool Handles(ConceptBrief brief) => brief is TBrief;

    /// <inheritdoc />
    public virtual int BatchSize(ConceptBrief brief, CatalogModel model, int fitting, double perConceptHigh) => Math.Clamp(fitting, 1, ConceptLimits.BatchSize);

    /// <inheritdoc />
    public Task<ConceptSourceBatch> PrepareAsync(ConceptSourceRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        return request.Brief is TBrief brief
            ? PrepareAsync(brief, request, cancellationToken)
            : throw new ArgumentException($"The {Kind} source got a {request.Brief.GetType().Name}.", nameof(request));
    }

    /// <summary>Fills one batch of <paramref name="brief"/>.</summary>
    protected abstract Task<ConceptSourceBatch> PrepareAsync(TBrief brief, ConceptSourceRequest request, CancellationToken cancellationToken);
}
