namespace TMConcept.Core.Documents;

/// <summary>
/// The meaning search of the documents: a small multilingual model that turns a passage or a question into a vector, run on this
/// computer's processor. It is a component, downloaded only after the user confirmed it; without it the documents are searched by their
/// words (<see cref="Bm25Ranker"/>).
/// </summary>
public interface IDocumentEmbedder
{
    /// <summary>The model id the vectors are stored under.</summary>
    string ModelId { get; }

    /// <summary>Its component code (installed through the confirmation flow).</summary>
    string ComponentCode { get; }

    /// <summary>Whether its files are on this computer.</summary>
    bool Ready { get; }

    /// <summary>One normalized vector per passage.</summary>
    Task<IReadOnlyList<float[]>> EmbedPassagesAsync(IReadOnlyList<string> passages, CancellationToken cancellationToken);

    /// <summary>The normalized vector of a question or subject.</summary>
    Task<float[]> EmbedQueryAsync(string query, CancellationToken cancellationToken);
}
