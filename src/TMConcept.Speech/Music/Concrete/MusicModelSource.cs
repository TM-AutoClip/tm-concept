using TMConcept.Core.Hardware;
using TMConcept.Core.Models;

namespace TMConcept.Speech.Music;

/// <summary>The local music model's <see cref="ILocalModel"/> (0.1.46, C5).</summary>
public sealed class MusicModelSource(IGpuInfo gpu) : ILocalModelSource
{
    /// <inheritdoc />
    public ILocalModel? Create(CatalogModel model)
    {
        ArgumentNullException.ThrowIfNull(model);
        return model.Provider == ModelProviders.Local && model.Capabilities.HasFlag(ModelCapability.Music) && model.Id == RakkaseMusicGenerator.ModelId
            ? new MusicLocalModel(model, Core.Music.LocalMusicSupport.Of(gpu.Detect(), OperatingSystem.IsLinux()).Nvidia)
            : null;
    }
}
