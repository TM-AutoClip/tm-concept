using TMConcept.Core.Models;

namespace TMConcept.Core.Duplicates;

/// <summary>The duplicate check's embedding calls inside a graphics card lease (<see cref="IGpuCoordinator"/>; the local embedder runs on the processor and never waits).</summary>
public sealed class LeasedEmbeddingService(IEmbeddingService inner, IGpuCoordinator gpu) : IEmbeddingService
{
    /// <inheritdoc />
    public async Task<EmbeddingResult> EmbedAsync(ResolvedModel model, IReadOnlyList<string> texts, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(model);
        var lease = await gpu.AcquireAsync(model.Model, cancellationToken).ConfigureAwait(false);
        await using (lease.ConfigureAwait(false))
        {
            return await inner.EmbedAsync(model, texts, cancellationToken).ConfigureAwait(false);
        }
    }
}