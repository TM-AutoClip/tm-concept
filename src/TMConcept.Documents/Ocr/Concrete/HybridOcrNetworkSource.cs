using Microsoft.Extensions.Logging;
using TMConcept.Core.Models;

namespace TMConcept.Documents.Ocr;

/// <summary>
/// The local OCR's networks page by page (0.1.27): when the OCR may use the graphics card (<see cref="OcrGpuModel.Device"/>), each page asks
/// the card for <see cref="CardWait"/> at most (<see cref="IGpuCoordinator.TryAcquireAsync"/>; no question to the user); with the lease it is
/// read by the networks on the card (opened once and kept by <paramref name="model"/>), without it on the processor. A failure to open the
/// card's networks makes the card unusable for this run and the page goes to the processor; the processor's networks are opened the first
/// time they are needed (or given ready, <paramref name="processor"/>) and closed with the source.
/// </summary>
public sealed class HybridOcrNetworkSource(string folder, IOcrNetworkFactory cpu, IOcrNetworkFactory gpu, OcrGpuModel model, IGpuCoordinator coordinator,
    ILogger logger, OcrNetworkPair? processor = null) : IOcrNetworkSource
{
    /// <summary>How long a page waits for a busy graphics card before it is read on the processor.</summary>
    public static readonly TimeSpan CardWait = TimeSpan.FromSeconds(2);

    private OcrNetworkPair? _cpu = processor;
    private bool? _lastOnCard;

    /// <inheritdoc />
    public async Task<OcrPageNetworks> ForPageAsync(CancellationToken cancellationToken)
    {
        if (model.Device == ModelDevice.Gpu)
        {
            var lease = await coordinator.TryAcquireAsync(model, CardWait, cancellationToken).ConfigureAwait(false);
            if (lease is { OnCard: true })
            {
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
            else if (lease is not null)
            {
                await lease.DisposeAsync().ConfigureAwait(false);
            }
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
