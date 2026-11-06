using TMConcept.Core.Duplicates;
using TMConcept.Core.Models;
using TMConcept.Core.Providers;

namespace TMConcept.Providers.Embeddings;

/// <summary>Finds the provider's embedding transport (from DI), or the local model on this computer (free, no key).</summary>
public sealed class EmbeddingService(IEnumerable<IEmbeddingTransport> transports, IEnumerable<ILocalEmbedder> locals) : IEmbeddingService
{
    /// <inheritdoc />
    public Task<EmbeddingResult> EmbedAsync(ResolvedModel model, IReadOnlyList<string> texts, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(model);
        if (model.Model.Provider == ModelProviders.Local)
        {
            var local = locals.FirstOrDefault(l => l.ModelId == model.Model.Id)
                ?? throw new ProviderException($"The local embedding model {model.Model.Id} is not available.") { Code = "model" };
            return local.EmbedAsync(texts, cancellationToken);
        }

        var transport = transports.FirstOrDefault(t => t.Code == model.Model.Provider)
            ?? throw new ProviderException($"{model.Model.Provider} has no embeddings.") { Code = "model" };
        return transport.EmbedAsync(model.Model, model.Key.Secret, texts, cancellationToken);
    }
}
