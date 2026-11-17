using TMConcept.Core.Models;

namespace TMConcept.Engines.Images;

/// <summary>A local image model with the default memory figures of <see cref="SdCppModelBase.MemorySettings"/> (every family but Z-Image).</summary>
public sealed class SdCppLocalModel(CatalogModel catalog, LocalImageModel files, ImageBackendSelector backends) : SdCppModelBase(catalog, files, backends);
