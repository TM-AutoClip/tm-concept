using TMConcept.Core.Models;

namespace TMConcept.Core.Music;

/// <summary>
/// The music models a template can choose (0.1.46, C5) and their generators: the catalog's entries with <see cref="ModelCapability.Music"/>,
/// whether one can be used now, the key it is called with and the generator that makes its music.
/// </summary>
public interface IMusicGenerators
{
    /// <summary>The music models of the catalog (local first, then online).</summary>
    IReadOnlyList<CatalogModel> Models { get; }

    /// <summary>The model of a template's <see cref="Styles.VideoMusic.Generator"/> ("provider/id"); null when the catalog has none such.</summary>
    CatalogModel? Find(string generator);

    /// <summary>Whether <paramref name="model"/> can be used now (a local model installed; files only).</summary>
    bool Ready(CatalogModel model);

    /// <summary>The model with the key to call it with (an online model's first working key); throws <see cref="ModelNotReadyException"/>.</summary>
    Task<ResolvedModel> ResolveAsync(CatalogModel model, CancellationToken cancellationToken);

    /// <summary>The generator of <paramref name="model"/>; null when none is registered.</summary>
    IMusicGenerator? For(CatalogModel model);
}
