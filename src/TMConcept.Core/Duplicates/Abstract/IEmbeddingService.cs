using TMConcept.Core.Models;

namespace TMConcept.Core.Duplicates;

/// <summary>Turns texts into vectors with the embedding model of a provider (the key goes only to that provider).</summary>
public interface IEmbeddingService
{
    /// <summary>One vector per text, in order, and the billed tokens.</summary>
    Task<EmbeddingResult> EmbedAsync(ResolvedModel model, IReadOnlyList<string> texts, CancellationToken cancellationToken);
}
