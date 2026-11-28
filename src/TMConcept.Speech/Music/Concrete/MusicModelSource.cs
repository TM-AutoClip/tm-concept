using TMConcept.Core.Hardware;
using TMConcept.Core.Models;

namespace TMConcept.Speech.Music;

/// <summary>
/// The <see cref="ILocalModel"/> of every local music model made for a graphics card (0.1.46, C5: ACE-Step 1.5; 28 Nov 2026: every family of
/// <see cref="LocalMusicFamilies.All"/> too, MusicGen to YuE): its catalog entry names a card class (<see cref="Core.Music.LocalMusicSupport.NeedsCard"/>). "Program composes" runs on the
/// processor and needs no place on the card.
/// </summary>
public sealed class MusicModelSource(IGpuInfo gpu) : ILocalModelSource
{
    /// <inheritdoc />
    public ILocalModel? Create(CatalogModel model)
    {
        ArgumentNullException.ThrowIfNull(model);
        return model.Capabilities.HasFlag(ModelCapability.Music) && Core.Music.LocalMusicSupport.NeedsCard(model)
            ? new MusicLocalModel(model, Core.Music.LocalMusicSupport.Of(gpu.Detect(), OperatingSystem.IsLinux()).Nvidia)
            : null;
    }
}
