using TMConcept.Core.Duplicates;
using TMConcept.Core.Models;
using TMConcept.Core.Providers;

namespace TMConcept.Providers.Embeddings;

/// <summary>Finds the provider's embedding transport (from DI).</summary>
public sealed class EmbeddingService(IEnumerable<IEmbeddingTransport> transports) : IEmbeddingService
{
    /// <inheritdoc />
    public Task<EmbeddingResult> EmbedAsync(ResolvedModel model, IReadOnlyList<string> texts, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(model);
        var transport = transports.FirstOrDefault(t => t.Code == model.Model.Provider)
            ?? throw new ProviderException($"{model.Model.Provider} has no embeddings.") { Code = "model" };
        return transport.EmbedAsync(model.Model, model.Key.Secret, texts, cancellationToken);
    }
}
