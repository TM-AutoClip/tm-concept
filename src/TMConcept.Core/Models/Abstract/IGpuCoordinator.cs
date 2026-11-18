using TMConcept.Core.Hardware;

namespace TMConcept.Core.Models;

/// <summary>
/// One local model on the graphics card at a time, for the whole app (0.1.17, the developer: load → use → unload → next; the video queue, the
/// concept page, topics, the AI edit, image description and the duplicate check all share the card). Every use of a model goes through a
/// lease: <c>await using var lease = await gpu.AcquireAsync(model, null, token);</c>. For a local model on the card it waits until no other
/// local model is in use, asks the one on the card to unload (<see cref="ILocalModel.UnloadAsync"/>), checks the memory with the new model's
/// own plan (<see cref="IMemoryWaiter"/>: 3 s after the unload, again 2.5 s later, then the user's Retry / Cancel), loads the new one and
/// measures the memory it may use
/// (<see cref="IGpuLease.Memory"/>). An online model and a processor model get their lease at once. Several leases of the SAME model may be
/// held together (a page's AI edit while the queue writes with the same local model; an image loop that leases each image again). A flow
/// must never hold leases of two different local models at once: that would wait for itself, so it throws
/// <see cref="InvalidOperationException"/> instead.
/// </summary>
public interface IGpuCoordinator
{
    /// <summary>The local model on the card now (in use or idle); null when the card is free.</summary>
    ILocalModel? Loaded { get; }

    /// <summary>Raised when the model on the card changes (loaded, unloaded); on any thread.</summary>
    event EventHandler? Changed;

    /// <summary>
    /// A lease for <paramref name="model"/>; <paramref name="need"/> is what to wait for after unloading the previous model (null = the model's
    /// first <see cref="ILocalModel.Needs"/>). Canceled with <paramref name="cancellationToken"/> while waiting.
    /// </summary>
    Task<IGpuLease> AcquireAsync(IModel model, ModelMemoryNeed? need, CancellationToken cancellationToken);

    /// <summary>
    /// A lease for <paramref name="model"/> only when the card can be had soon (0.1.27, the local OCR's pages): it waits at most
    /// <paramref name="wait"/> for another local model in use, never asks the user anything (the memory is measured once; too little gives the
    /// card back) and returns null when the card is busy, short of memory, or this flow already holds another model's lease; the caller then
    /// works on the processor. A model that is not on the card gets its lease at once. <see cref="OperationCanceledException"/> only for
    /// <paramref name="cancellationToken"/>.
    /// </summary>
    Task<IGpuLease?> TryAcquireAsync(IModel model, TimeSpan wait, CancellationToken cancellationToken);

    /// <summary>A lease for the model of a catalog entry (<see cref="IModelDirectory.Get"/>).</summary>
    Task<IGpuLease> AcquireAsync(CatalogModel model, CancellationToken cancellationToken);

    /// <summary>
    /// Unloads the model on the card, and any model whose earlier unload failed, when nobody uses the card (end of the video queue, app exit);
    /// nothing when it is in use.
    /// </summary>
    Task FreeAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Asks every local model whose earlier unload failed (it may still hold graphics memory: a server that "still holds" the model, one no
    /// control can unload) to unload once more, except <paramref name="keep"/> (the model about to use the card). A memory check calls it
    /// before every reading after its first, so the user's Retry also asks our own stuck model again (a lease being checked blocks every
    /// other change of model, so nothing else would). Returns the names of the models that still could not be unloaded (empty = none).
    /// </summary>
    Task<IReadOnlyList<string>> RetryStuckAsync(IModel keep, CancellationToken cancellationToken);
}