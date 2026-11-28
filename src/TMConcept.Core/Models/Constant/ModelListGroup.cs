namespace TMConcept.Core.Models;

/// <summary>
/// The three groups of every model list, in the order they are shown (the developer's plan, step 2, 28 Nov 2026: "yerelde çalışan → online →
/// kırmızı"; <see cref="IModelListOrder"/>). The catalog's order is kept inside each group.
/// </summary>
public enum ModelListGroup
{
    /// <summary>A model that runs on this computer: a local one this card holds (or one that needs no card), the app's Ollama, a user-added server.</summary>
    Runs = 0,

    /// <summary>An online model (a provider's API).</summary>
    Online = 1,

    /// <summary>Red: this computer cannot run it (the card's class is too low, or it needs an NVIDIA card that is not there). Always last.</summary>
    CannotRun = 2,
}
