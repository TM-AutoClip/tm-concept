namespace TMConcept.Desktop.ViewModels;

/// <summary>
/// The music models a page offers (0.1.46, C5; 0.1.55: without an install of its own, a model is installed when it starts): the music models
/// of the catalog, whether one can be used now (a local one installed) and how the local ones fit this computer's card and system.
/// </summary>
/// <param name="Models">The music models (local first, those made for this computer first: <see cref="Core.Music.LocalMusicSupport.Ordered"/>).</param>
/// <param name="Ready">Whether a model can be used now.</param>
/// <param name="Card">This computer's card and system for the local models (NVIDIA; not verified on Linux).</param>
public sealed record MusicGeneratorServices(IReadOnlyList<Core.Models.CatalogModel> Models, Func<Core.Models.CatalogModel, bool> Ready,
    Core.Music.LocalMusicCard Card);
