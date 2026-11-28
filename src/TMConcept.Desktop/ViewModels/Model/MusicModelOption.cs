namespace TMConcept.Desktop.ViewModels;

/// <summary>
/// A music model on the video page's music choice (0.1.46; 0.1.55 local ones too): its catalog key "provider/id", its label (price or install
/// state) and its group of <see cref="Core.Models.IModelListOrder"/>; red when this computer cannot run it (the card's class is too low or it
/// needs an NVIDIA card that is not there), orange when its licence is non-commercial (28 Nov 2026), yellow when it is tight (its class is the
/// card's class, 0.1.60, K13).
/// </summary>
public sealed record MusicModelOption(string Key, string Label, Core.Models.ModelListGroup Group, bool IsCaution = false, bool IsTight = false)
{
    /// <summary>Red: this computer cannot run it (listed last, still offered: rule 19).</summary>
    public bool IsError => Group == Core.Models.ModelListGroup.CannotRun;
}
