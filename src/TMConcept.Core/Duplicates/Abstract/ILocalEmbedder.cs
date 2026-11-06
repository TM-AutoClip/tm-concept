namespace TMConcept.Core.Duplicates;

/// <summary>An embedding model that runs on this computer (free, no key; installed when a process that uses it starts).</summary>
public interface ILocalEmbedder
{
    /// <summary>Its model id in the local catalog.</summary>
    string ModelId { get; }

    /// <summary>One normalized vector per text, in order (no tokens are billed).</summary>
    Task<EmbeddingResult> EmbedAsync(IReadOnlyList<string> texts, CancellationToken cancellationToken);
}
