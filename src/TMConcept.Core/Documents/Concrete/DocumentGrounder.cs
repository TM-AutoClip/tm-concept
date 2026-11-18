using Microsoft.Extensions.Logging;
using TMConcept.Core.Profiles;

namespace TMConcept.Core.Documents;

/// <summary>
/// The passages of one call from a profile's documents: the ready documents chosen (none chosen = all ready ones), their passages, the
/// meaning search's vectors and the query's vector when the meaning model is installed and the call is real (an estimate never runs a model
/// of this computer), then <see cref="PassageRetriever"/> within the token budget. A failing meaning model is logged as an error and the
/// keyword search alone picks the passages (the documents stay usable). An estimate (the price line, typed into at every key) never reads
/// the passages: <see cref="EstimateTokensAsync"/> charges the budget, or less when the chosen documents are shorter.
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
    /// The passage tokens a call of <paramref name="profile"/> would carry within <paramref name="budget"/>: the budget, or the whole of the
    /// chosen ready documents when they are shorter (counted from their stored length, nothing is decrypted). Throws like
    /// <see cref="GroundAsync"/> when no chosen document is ready.
    /// </summary>
    public async Task<long> EstimateTokensAsync(Profile profile, IReadOnlyCollection<Guid> chosen, int budget, CancellationToken cancellationToken,
        DocumentPageRange? pages = null, IReadOnlyList<DocumentChunk>? digest = null)
    {
        ArgumentNullException.ThrowIfNull(profile);
        var documents = await RequireReadyAsync(profile.Id, chosen, cancellationToken).ConfigureAwait(false);
        var perToken = OutputLanguages.Of(profile.ContentLanguage).CharactersPerToken;
        var whole = digest is not null
            ? digest.Sum(n => (long)Math.Ceiling(n.Text.Length / perToken) + PassageTokens.LineOverhead)
            : documents.Sum(d => (long)Math.Ceiling(((d.Characters / perToken) + (d.Chunks * (double)PassageTokens.LineOverhead)) * DocumentDigester.Fraction(d, pages)));
        return Math.Min(budget, whole);
    }

    /// <summary>
    /// The grounding of one call of <paramref name="profile"/> for <paramref name="query"/> within <paramref name="budget"/> tokens;
    /// <paramref name="used"/>: passages earlier batches of the run sent (they come last), <paramref name="phase"/>: where the spread starts.
    /// <see cref="Providers.ProviderException"/> with the code <c>documents</c> when no chosen document is ready.
    /// </summary>
    public async Task<DocumentGrounding> GroundAsync(Profile profile, IReadOnlyCollection<Guid> chosen, string query, int budget, bool estimate,
        Func<string, long> tokens, CancellationToken cancellationToken, IReadOnlySet<Guid>? used = null, double phase = 0, DocumentPageRange? pages = null,
        IReadOnlyList<DocumentChunk>? digest = null)
    {
        ArgumentNullException.ThrowIfNull(profile);
        var documents = await RequireReadyAsync(profile.Id, chosen, cancellationToken).ConfigureAwait(false);

        // "Read everything" (0.1.24) grounds in its notes of the chosen documents; a page range keeps the passages on its pages (and every
        // passage without a page: a Word or text file, a web page).
        var ids = documents.Select(d => d.Id).ToHashSet();
        var chunks = digest is not null
            ? digest.Where(n => ids.Contains(n.DocumentId)).ToList()
            : (await store.ChunksAsync(profile.Id, ids, cancellationToken).ConfigureAwait(false)).Where(c => pages is null || c.Page <= 0 || pages.Contains(c.Page)).ToList();
        if (chunks.Count == 0 && pages is not null)
        {
            throw new Providers.ProviderException("No passage of the chosen documents is on the chosen pages.") { Code = "pages" };
        }

        var vectors = new Dictionary<Guid, float[]>();
        float[]? queryVector = null;
        if (!estimate && digest is null && embedder.Ready && query.Trim().Length > 0)
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

        var search = new DocumentSearch(query, chunks, vectors, queryVector, budget) { Used = used ?? new HashSet<Guid>(), Phase = phase };
        var grounding = retriever.Select(search, documents, tokens);
        logger.LogInformation("Documents: {Passages} passages of {Documents} documents picked ({Search} search, budget {Budget} tokens)", grounding.Passages.Count,
            grounding.Documents.Count, queryVector is null ? (query.Trim().Length == 0 ? "spread" : "keyword") : "keyword + meaning", budget);
        return grounding;
    }

    private async Task<IReadOnlyList<SourceDocument>> RequireReadyAsync(Guid profileId, IReadOnlyCollection<Guid> chosen, CancellationToken cancellationToken)
    {
        var documents = await ReadyAsync(profileId, chosen, cancellationToken).ConfigureAwait(false);
        return documents.Count > 0
            ? documents
            : throw new Providers.ProviderException("No chosen document of the profile is ready to be searched.") { Code = "documents" };
    }
}
