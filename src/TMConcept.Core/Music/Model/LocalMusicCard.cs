namespace TMConcept.Core.Music;

/// <summary>
/// How the local music model fits this computer (0.1.46, C5; <see cref="LocalMusicSupport"/>): it is made for NVIDIA cards only (rakkase and
/// ACE-Step were measured on CUDA), and on Linux it was never tried. Never a block: the option is labelled and a warning is shown.
/// </summary>
/// <param name="Nvidia">An NVIDIA card is there.</param>
/// <param name="Linux">The computer runs Linux (the model is not verified there).</param>
public sealed record LocalMusicCard(bool Nvidia, bool Linux)
{
    /// <summary>The option carries "(NVIDIA only)" and, once chosen, the yellow "this card is not supported" warning.</summary>
    public bool NvidiaOnly => !Nvidia;
}
