using Microsoft.Extensions.Logging;
using TMConcept.Core.Profiles;

namespace TMConcept.Core.Documents;

/// <summary>
/// The passages of one call from a profile's documents: the ready documents chosen (none chosen = all ready ones), their passages, the
/// meaning search's vectors and the query's vector when the meaning model is installed and the call is real (an estimate never runs a model
/// of this computer), then <see cref="PassageRetriever"/> within the token budget. A failing meaning model is logged as an error and the
/// keyword search alone picks the passages (the documents stay usable).
/// </summary>
public sealed class DocumentGrounder(IDocumentStore store, IDocumentEmbedder embedder, PassageRetriever retriever, ILogger<DocumentGrounder> logger)
{
    /// <summary>The ready documents of <paramref name="profileId"/> among <paramref name="chosen"/> (empty = all ready ones), in the list's order.</summary>
    public async Task<IReadOnlyList<SourceDocument>> ReadyAsync(Guid profileId, IReadOnlyCollection<Guid> chosen, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(chosen);
        var documents = await store.ListAsync(profileId, cancellationToken).ConfigureAwait(false);
        return documents.Where(d => d.Status == DocumentStatus.Ready && d.Chunks > 0 && (chosen.Count == 0 || chosen.Contains(d.Id))).ToList();
    }

    /// <summary>
    /// The grounding of one call of <paramref name="profile"/> for <paramref name="query"/> within <paramref name="budget"/> tokens.
    /// <see cref="Providers.ProviderException"/> with the code <c>documents</c> when no chosen document is ready.
    /// </summary>
    public async Task<DocumentGrounding> GroundAsync(Profile profile, IReadOnlyCollection<Guid> chosen, string query, int budget, bool estimate,
        Func<string, long> tokens, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(profile);
        var documents = await ReadyAsync(profile.Id, chosen, cancellationToken).ConfigureAwait(false);
        if (documents.Count == 0)
        {
            throw new Providers.ProviderException("No chosen document of the profile is ready to be searched.") { Code = "documents" };
        }

        var chunks = await store.ChunksAsync(profile.Id, documents.Select(d => d.Id).ToList(), cancellationToken).ConfigureAwait(false);
        var vectors = new Dictionary<Guid, float[]>();
        float[]? queryVector = null;
        if (!estimate && embedder.Ready && query.Trim().Length > 0)
        {
            try
            {
                foreach (var stored in await store.VectorsAsync(profile.Id, embedder.ModelId, cancellationToken).ConfigureAwait(false))
                {
                    vectors[stored.ChunkId] = DocumentVectors.FromBytes(stored.Vector);
                }

                queryVector = await embedder.EmbedQueryAsync(query, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is InvalidOperationException or IOException or InvalidDataException or UnauthorizedAccessException)
            {
                logger.LogError(ex, "The meaning search of the documents failed; the passages are picked by their words only");
                vectors.Clear();
                queryVector = null;
            }
        }

        var grounding = retriever.Select(new DocumentSearch(query, chunks, vectors, queryVector, budget), documents, tokens);
        logger.LogInformation("Documents: {Passages} passages of {Documents} documents picked ({Search} search, budget {Budget} tokens)", grounding.Passages.Count,
            grounding.Documents.Count, queryVector is null ? (query.Trim().Length == 0 ? "spread" : "keyword") : "keyword + meaning", budget);
        return grounding;
    }
}
