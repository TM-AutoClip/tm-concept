using TMConcept.Core.Models;

namespace TMConcept.Desktop.ViewModels;

/// <summary>
/// The music models a page offers (0.1.46, C5; 0.1.55: without an install of its own, a model is installed when it starts): the music models
/// of the catalog, whether one can be used now (a local one installed), how the local ones fit this computer's card and system, and the one
/// order of the model lists (0.1.60: the music page and the video page's music models use the same <see cref="IModelListOrder"/> as the step pickers).
/// </summary>
/// <param name="Models">The music models in <see cref="IModelListOrder"/>'s order: the local ones that run here, the online ones, the red ones last.</param>
/// <param name="Ready">Whether a model can be used now.</param>
/// <param name="Card">This computer's card and system for the local models (NVIDIA; not verified on Linux).</param>
/// <param name="Order">The order of the model lists (the groups, the first choice when nothing is chosen).</param>
public sealed record MusicGeneratorServices(IReadOnlyList<CatalogModel> Models, Func<CatalogModel, bool> Ready, Core.Music.LocalMusicCard Card,
    IModelListOrder Order)
{
    /// <summary>The group of <paramref name="model"/> on this computer (red: <see cref="Core.Music.LocalMusicCard.CannotRun"/>).</summary>
    public ModelListGroup Group(CatalogModel model) => Order.GroupOf(model, Card.CannotRun(model));
}
