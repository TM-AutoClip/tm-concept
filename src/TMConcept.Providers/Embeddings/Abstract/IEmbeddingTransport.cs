using TMConcept.Core.Duplicates;
using TMConcept.Core.Models;

namespace TMConcept.Providers.Embeddings;

/// <summary>A provider that serves embeddings.</summary>
public interface IEmbeddingTransport
{
    /// <summary>Provider code.</summary>
    string Code { get; }

    /// <summary>Embeds.</summary>
    Task<EmbeddingResult> EmbedAsync(CatalogModel model, string apiKey, IReadOnlyList<string> texts, CancellationToken cancellationToken);
}
