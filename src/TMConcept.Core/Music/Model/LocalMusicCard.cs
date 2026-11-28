using TMConcept.Core.Hardware;
using TMConcept.Core.Models;

namespace TMConcept.Core.Music;

/// <summary>
/// How the local music models fit this computer (0.1.46, C5; <see cref="LocalMusicSupport.Of"/>): the card models are made for NVIDIA cards only
/// (rakkase and ACE-Step were measured on CUDA), and on Linux they were never tried. Never a block (rule 19): the option is labelled, coloured
/// and a note is shown; the order of the lists is <see cref="IModelListOrder"/>'s with <see cref="CannotRun"/> as the red verdict.
/// </summary>
/// <param name="Nvidia">An NVIDIA card is there.</param>
/// <param name="Linux">The computer runs Linux (the model is not verified there).</param>
/// <param name="Tier">
/// The memory class of the largest NVIDIA card (28 Nov 2026, more local music models with card classes of their own: <see cref="TooBig"/>);
/// <see cref="VramTier.None"/> without one or when its memory is unknown.
/// </param>
public sealed record LocalMusicCard(bool Nvidia, bool Linux, VramTier Tier = VramTier.None)
{
    /// <summary>The option carries "(NVIDIA only)" and, once chosen, the yellow "this card is not supported" warning.</summary>
    public bool NvidiaOnly => !Nvidia;

    /// <summary>
    /// Whether <paramref name="model"/> needs a higher graphics card class than this NVIDIA card (28 Nov 2026: red "this card cannot run it",
    /// the same rule as the image models and the voices). False without an NVIDIA card or when its memory is unknown (<see cref="NoNvidia"/>
    /// says that) and for a model that needs no card.
    /// </summary>
    public bool TooBig(CatalogModel model) =>
        LocalMusicSupport.NeedsCard(model) && Nvidia && Tier != VramTier.None && !VramTiers.Sees(Tier, VramTiers.ForModel(model.MinVideoMemoryGb));

    /// <summary>Whether <paramref name="model"/> needs an NVIDIA card and this computer has none.</summary>
    public bool NoNvidia(CatalogModel model) => LocalMusicSupport.NeedsCard(model) && !Nvidia;

    /// <summary>
    /// Red, listed last (0.1.60, the plan's step 2): the card's class is too low (<see cref="TooBig"/>) or the model needs an NVIDIA card that
    /// is not there (<see cref="NoNvidia"/>). Still offered and tried when chosen (rule 19).
    /// </summary>
    public bool CannotRun(CatalogModel model) => TooBig(model) || NoNvidia(model);

    /// <summary>
    /// Yellow "tight" (0.1.60, K13): the model's card class is exactly this card's class. It fits on paper, but Windows, the desktop and the
    /// browser keep part of the card (the 6 GB card of 28 Nov 2026 had about 5 GB free), so other programs should be closed first.
    /// </summary>
    public bool Tight(CatalogModel model) =>
        LocalMusicSupport.NeedsCard(model) && Nvidia && Tier != VramTier.None && VramTiers.ForModel(model.MinVideoMemoryGb) == Tier;
}
