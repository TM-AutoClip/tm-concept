using Microsoft.Extensions.Logging;
using TMConcept.Core.Activity;
using TMConcept.Core.Documents;
using TMConcept.Core.Hardware;
using TMConcept.Core.Models;

namespace TMConcept.Documents.Ocr;

/// <summary>
/// The local OCR as a user of the graphics card (0.1.27): it takes the card through <see cref="IGpuCoordinator.TryAcquireAsync"/> one page
/// at a time, so a video or a local model waits for one page at most, and OCR goes on on the processor while the card is busy. It is on the
/// card (<see cref="Device"/>) only on Windows x64 with a graphics card, when the "Text recognition" setting is Automatic, the DirectML runtime
/// is installed and DirectML has not failed in this run; its networks stay on the card between pages and documents and are closed when
/// another model asks for the card (<see cref="LocalModelBase.UnloadAsync"/>). The first failure of DirectML (it cannot load, a network
/// cannot be opened or run) makes it unusable until the app is started again; that is logged once and said once on screen.
/// </summary>
public sealed class OcrGpuModel(DirectMlRuntimeStore store, IOcrAccelerationSetting setting, ILogger<OcrGpuModel> logger)
    : LocalModelBase(new CatalogModel
    {
        Provider = ModelProviders.Local,
        Id = PageTextEngines.LocalOcrId,
        Name = LocalOcrModels.Name,
        Capabilities = ModelCapability.Vision,
    })
{
    /// <summary>What the two networks take on the card and in main memory (an estimate until measured on a real card).</summary>
    public static readonly ModelMemoryNeed Need = ModelMemoryNeed.FromGigabytes(ModelQualities.Standard, 0.6, 0.5);

    private readonly Lock _gate = new();
    private OcrNetworkPair? _networks;
    private int _unusable;
    private int _told;

    /// <inheritdoc />
    public override ModelDevice Device => Usable ? ModelDevice.Gpu : ModelDevice.Cpu;

    /// <inheritdoc />
    public override IReadOnlyList<ModelMemoryNeed> Needs => [Need];

    /// <summary>Whether the card may be used now (see the class).</summary>
    public bool Usable => setting.Value == OcrAcceleration.Automatic && !Unusable && store.Supported && store.Ready;

    /// <summary>Whether DirectML failed in this run.</summary>
    public bool Unusable => Volatile.Read(ref _unusable) != 0;

    /// <summary>Why DirectML failed (empty while it has not).</summary>
    public string FailureReason { get; private set; } = string.Empty;

    /// <summary>
    /// True once after DirectML failed (the next caller says it on screen in the user's language); false before and after.
    /// </summary>
    public bool TakeFailureNotice() => Unusable && Interlocked.Exchange(ref _told, 1) == 0;

    /// <summary>The networks on the card, opened by <paramref name="open"/> the first time (and after an unload).</summary>
    public OcrNetworkPair Networks(Func<OcrNetworkPair> open)
    {
        ArgumentNullException.ThrowIfNull(open);
        lock (_gate)
        {
            if (_networks is null || _networks.Disposed)
            {
                _networks = open();
            }

            return _networks;
        }
    }

    /// <summary>DirectML failed: the networks on the card are closed and the card is not used again in this run (logged once).</summary>
    public void MarkUnusable(Exception error)
    {
        ArgumentNullException.ThrowIfNull(error);
        if (Interlocked.Exchange(ref _unusable, 1) == 0)
        {
            FailureReason = error.Message;
            logger.LogWarning(LogEvents.OnScreen, error, "Text recognition on the graphics card (DirectML) failed; the processor reads the pages until the app is started again");
        }

        CloseNetworks();
    }

    /// <inheritdoc />
    protected override long MinimumCardBytes(ModelMemoryNeed need)
    {
        ArgumentNullException.ThrowIfNull(need);
        return need.VideoMemoryBytes;
    }

    /// <inheritdoc />
    protected override bool OffloadsToMainMemory => false;

    /// <inheritdoc />
    protected override Task OnUnloadAsync(CancellationToken cancellationToken)
    {
        CloseNetworks();
        return Task.CompletedTask;
    }

    private void CloseNetworks()
    {
        OcrNetworkPair? networks;
        lock (_gate)
        {
            networks = _networks;
            _networks = null;
        }

        networks?.Dispose();
    }
}
