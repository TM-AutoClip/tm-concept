using TMConcept.Core.Models;

namespace TMConcept.Core.Concepts;

/// <summary>
/// One source of concepts (0.1.21: "Konu öner", "Fikirden", "Belgelerden"). The page turns the user's choice into a <see cref="ConceptBrief"/>;
/// <see cref="ConceptGenerator"/> asks the registered sources which one <see cref="Handles"/> it and lets that one fill every batch's request
/// (<see cref="PrepareAsync"/>). Whether the model is online or local makes no difference to a source: it gets the model's limits and fits
/// its material into them. Every source adds to the same concept list; none knows of another.
/// </summary>
public interface IConceptSource
{
    /// <summary>Which source this is.</summary>
    ConceptSourceKind Kind { get; }

    /// <summary>Whether <paramref name="brief"/> is this source's request.</summary>
    bool Handles(ConceptBrief brief);

    /// <summary>
    /// Concepts per call for <paramref name="brief"/> with <paramref name="model"/>: <paramref name="fitting"/> is what the model's answer
    /// limit allows when one concept takes <paramref name="perConceptHigh"/> output tokens (thinking included); a source may ask for fewer.
    /// </summary>
    int BatchSize(ConceptBrief brief, CatalogModel model, int fitting, double perConceptHigh);

    /// <summary>What one batch sends: the extra request, the topics of the batch and any outside material (document passages).</summary>
    Task<ConceptSourceBatch> PrepareAsync(ConceptSourceRequest request, CancellationToken cancellationToken);
}
