namespace TMConcept.Core.Models;

/// <summary>
/// Gives the <see cref="ILocalModel"/> of the catalog entries it knows (the image engine: its local image models; the speech worker: the local
/// voices and the embedder; the local servers: their models on this computer). Registered once per kind; <see cref="IModelDirectory"/> asks
/// every source and keeps the first answer per model.
/// </summary>
public interface ILocalModelSource
{
    /// <summary>The local model of <paramref name="model"/>; null when it is not one of this source's.</summary>
    ILocalModel? Create(CatalogModel model);
}