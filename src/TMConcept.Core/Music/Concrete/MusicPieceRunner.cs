using Microsoft.Extensions.Logging;
using TMConcept.Core.Models;

namespace TMConcept.Core.Music;

/// <inheritdoc cref="IMusicPieceRunner" />
public sealed class MusicPieceRunner(IGpuCoordinator gpu, ILogger<MusicPieceRunner> logger) : IMusicPieceRunner
{
    /// <inheritdoc />
    public async Task<GeneratedMusic> RunAsync(IMusicGenerator generator, MusicGenerationRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(generator);
        ArgumentNullException.ThrowIfNull(request);
        var model = request.Model.Model;
        try
        {
            if (LocalMusicSupport.NeedsCard(model))
            {
                // The graphics card's one local model at a time (rule 18): another local model is unloaded first.
                await using var lease = await gpu.AcquireAsync(model, cancellationToken).ConfigureAwait(false);
                return await generator.GenerateAsync(request, cancellationToken).ConfigureAwait(false);
            }

            return await generator.GenerateAsync(request, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException and not MusicGenerationException and not OutOfMemoryException)
        {
            // Rule 17: no stray failure (a process that could not start, a provider's error, too little memory) reaches the caller unnamed.
            logger.LogWarning(ex, "The music model {Model} failed", ModelBase.KeyOf(model));
            throw new MusicGenerationException(ex.Message, ex) { Code = ex is MemoryShortfallException ? MusicFailure.Memory : MusicFailure.Music };
        }
    }
}
