using TMConcept.Core.Models;

namespace TMConcept.Engines.Images;

/// <summary>The local image models of stable-diffusion.cpp (<see cref="LocalImageModels"/>) as <see cref="ILocalModel"/>s, each of its family's class.</summary>
public sealed class SdCppModelSource(ImageBackendSelector backends) : ILocalModelSource
{
    /// <inheritdoc />
    public ILocalModel? Create(CatalogModel model)
    {
        ArgumentNullException.ThrowIfNull(model);
        return model.Provider == ModelProviders.Local && model.Capabilities.HasFlag(ModelCapability.Image)
            && LocalImageModels.All.FirstOrDefault(m => m.Id == model.Id) is { } files
            ? For(model, files, backends)
            : null;
    }

    /// <summary>The class of <paramref name="files"/>' family: <see cref="ZImageLocalModel"/> for Z-Image, <see cref="SdCppLocalModel"/> otherwise.</summary>
    public static SdCppModelBase For(CatalogModel catalog, LocalImageModel files, ImageBackendSelector backends)
    {
        ArgumentNullException.ThrowIfNull(files);
        return files.Family == "z-image" ? new ZImageLocalModel(catalog, files, backends) : new SdCppLocalModel(catalog, files, backends);
    }
}
