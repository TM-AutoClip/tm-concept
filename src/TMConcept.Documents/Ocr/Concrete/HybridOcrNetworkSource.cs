using Microsoft.Extensions.Logging;
using TMConcept.Core.Models;

namespace TMConcept.Documents.Ocr;

/// <summary>
/// The local OCR's networks page by page (0.1.27): when the OCR may use the graphics card (<see cref="OcrGpuModel.Device"/>), each page asks
/// the card for <see cref="CardWait"/> at most (<see cref="IGpuCoordinator.TryAcquireAsync"/>; no question to the user); with the lease it is
/// read by the networks on the card (opened once and kept by <paramref name="model"/>), without it on the processor. After a refusal (the card
/// busy or short of memory) the pages of the next <see cref="CardRetry"/> take the card only when it is free at once, so a long scan is not
/// held up by a card another job keeps. A failure to open the card's networks makes the card unusable for this run and the page goes to the
/// processor; the processor's networks are opened the first time they are needed (or given ready, <paramref name="processor"/>) and closed
/// with the source. When the card may no longer be used ("Text recognition" set to Processor only, DirectML failed) and the card's networks
/// are still open, they are closed before the page (the card's memory is given back).
/// </summary>
public sealed class HybridOcrNetworkSource(string folder, IOcrNetworkFactory cpu, IOcrNetworkFactory gpu, OcrGpuModel model, IGpuCoordinator coordinator,
    ILogger logger, OcrNetworkPair? processor = null, TimeProvider? clock = null) : IOcrNetworkSource
{
    /// <summary>How long a page waits for a busy graphics card before it is read on the processor.</summary>
    public static readonly TimeSpan CardWait = TimeSpan.FromSeconds(2);

    /// <summary>How long after a refusal the pages take the card only when it is free at once (no wait).</summary>
    public static readonly TimeSpan CardRetry = TimeSpan.FromSeconds(30);

    private readonly TimeProvider _clock = clock ?? TimeProvider.System;
    private OcrNetworkPair? _cpu = processor;
    private bool? _lastOnCard;
    private DateTimeOffset _busyUntil = DateTimeOffset.MinValue;

    /// <inheritdoc />
    public async Task<OcrPageNetworks> ForPageAsync(CancellationToken cancellationToken)
    {
        if (model.Device == ModelDevice.Gpu)
        {
            var wait = _clock.GetUtcNow() < _busyUntil ? TimeSpan.Zero : CardWait;
            var lease = await coordinator.TryAcquireAsync(model, wait, cancellationToken).ConfigureAwait(false);
            if (lease is null)
            {
                _busyUntil = _clock.GetUtcNow() + CardRetry;
            }
            else if (lease.OnCard)
            {
                _busyUntil = DateTimeOffset.MinValue;
                try
                {
                    var pair = model.Networks(() => gpu.Open(folder));
                    Said(true);
                    return new OcrPageNetworks(pair.Detector, pair.Recognizer, lease);
                }
                catch (Exception ex) when (ex is InvalidOperationException or InvalidDataException or TypeInitializationException or DllNotFoundException
                    or BadImageFormatException or EntryPointNotFoundException)
                {
                    await lease.DisposeAsync().ConfigureAwait(false);
                    model.MarkUnusable(ex);
                }
                catch
                {
                    await lease.DisposeAsync().ConfigureAwait(false);
                    throw;
                }
            }
            else
            {
                await lease.DisposeAsync().ConfigureAwait(false);
            }
        }
        else if (model.HoldsNetworks)
        {
            // The card may not be used any more but still holds the networks: they are closed now (no page reads on them between pages).
            await model.CloseAsync(coordinator, cancellationToken).ConfigureAwait(false);
        }

        _cpu ??= cpu.Open(folder);
        Said(false);
        return new OcrPageNetworks(_cpu.Detector, _cpu.Recognizer, null);
    }

    /// <inheritdoc />
    public void Failed(OcrPageNetworks networks, Exception error)
    {
        ArgumentNullException.ThrowIfNull(networks);
        if (networks.Lease is not null)
        {
            model.MarkUnusable(error);
        }
    }

    /// <inheritdoc />
    public void Dispose() => _cpu?.Dispose();

    /// <summary>Logs the device when it changes from one page to the next.</summary>
    private void Said(bool onCard)
    {
        if (_lastOnCard != onCard)
        {
            _lastOnCard = onCard;
            logger.LogInformation("Local OCR reads on the {Device}", onCard ? gpu.Device : cpu.Device);
        }
    }
}
