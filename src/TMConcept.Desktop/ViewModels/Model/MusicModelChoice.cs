namespace TMConcept.Desktop.ViewModels;

/// <summary>
/// A music model on the music page (0.1.54): its key, its label with its price or install state, the model, local or online, installed, its
/// group of <see cref="Core.Models.IModelListOrder"/>; red when this computer cannot run it (the card's class is too low or it needs an NVIDIA
/// card that is not there), orange when its licence is non-commercial (28 Nov 2026), yellow when it is tight (its class is the card's class,
/// 0.1.60, K13).
/// </summary>
public sealed record MusicModelChoice(string Key, string Label, Core.Models.CatalogModel Model, bool Local, bool Ready, Core.Models.ModelListGroup Group,
    bool IsCaution = false, bool IsTight = false)
{
    /// <summary>Red: this computer cannot run it (listed last, still offered: rule 19).</summary>
    public bool IsError => Group == Core.Models.ModelListGroup.CannotRun;
}
