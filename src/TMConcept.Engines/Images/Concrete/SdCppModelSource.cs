using TMConcept.Core.Models;

namespace TMConcept.Engines.Images;

/// <summary>The local image models of stable-diffusion.cpp (<see cref="LocalImageModels"/>) as <see cref="ILocalModel"/>s.</summary>
public sealed class SdCppModelSource(ImageBackendSelector backends) : ILocalModelSource
{
    /// <inheritdoc />
    public ILocalModel? Create(CatalogModel model)
    {
        ArgumentNullException.ThrowIfNull(model);
        return model.Provider == ModelProviders.Local && model.Capabilities.HasFlag(ModelCapability.Image)
            && LocalImageModels.All.FirstOrDefault(m => m.Id == model.Id) is { } files
            ? new SdCppLocalModel(model, files, backends)
            : null;
    }
}