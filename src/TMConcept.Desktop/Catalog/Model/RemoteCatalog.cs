using TMConcept.Core.AppOllama;
using TMConcept.Core.Models;
using TMConcept.Engines.Images;

namespace TMConcept.Desktop.Catalog;

/// <summary>
/// A checked remote model catalog, ready to add: the new provider models and image models' catalog entries, the image models' typed runs, the
/// Ollama models with their manifest digests. Entries whose id the app has built in are left out (<see cref="Ignored"/>); entries that need a
/// newer stable-diffusion.cpp than this app pins are left out too (<see cref="Skipped"/>).
/// </summary>
/// <param name="Date">The file's date.</param>
/// <param name="Models">Catalog entries to add (cloud provider models and the local image models).</param>
/// <param name="Images">The local image models.</param>
/// <param name="Ollama">The app-Ollama models.</param>
/// <param name="Digests">The manifest digest of each app-Ollama model by tag.</param>
public sealed record RemoteCatalog(string Date, IReadOnlyList<CatalogModel> Models, IReadOnlyList<LocalImageModel> Images, IReadOnlyList<AppModelInfo> Ollama,
    IReadOnlyDictionary<string, string> Digests)
{
    /// <summary>Entries left out because the app has them built in.</summary>
    public int Ignored { get; init; }

    /// <summary>Entries left out because they need something newer than this app has.</summary>
    public int Skipped { get; init; }

    /// <summary>How many entries are added.</summary>
    public int Count => Models.Count(m => m.Provider != ModelProviders.Local) + Images.Count + Ollama.Count;
}
