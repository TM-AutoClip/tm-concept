namespace TMConcept.Desktop.ViewModels;

/// <summary>
/// What the template editor's generated music needs (0.1.46, C5): the music models of the catalog, whether one can be used now (the local
/// one installed), how the local model fits this computer's card and system, and the install of the local model (one confirmation of the
/// app's install flow, with its size and memory need; true when it is ready afterwards).
/// </summary>
/// <param name="Models">The music models (local first).</param>
/// <param name="Ready">Whether a model can be used now.</param>
/// <param name="Card">The local model on this computer (NVIDIA only; not verified on Linux).</param>
/// <param name="InstallLocalAsync">Installs the local model after the user agreed; true when it is ready.</param>
public sealed record MusicGeneratorServices(IReadOnlyList<Core.Models.CatalogModel> Models, Func<Core.Models.CatalogModel, bool> Ready,
    Core.Music.LocalMusicCard Card, Func<Task<bool>> InstallLocalAsync);
