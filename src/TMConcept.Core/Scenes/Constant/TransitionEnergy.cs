namespace TMConcept.Core.Scenes;

/// <summary>
/// How much energy a scene transition has (F1, 0.1.42; new values are added at the end). The template's narration tempo sets it
/// (<see cref="TransitionCatalog.EnergyOf"/>): relaxed = calm, natural = medium, tight = high. Every transition of the catalog
/// (<see cref="SceneTransitions.Catalog"/>) belongs to one or more energies, and its length follows the energy.
/// </summary>
public enum TransitionEnergy
{
    /// <summary>Calm: soft and slow (relaxed tempo).</summary>
    Calm = 0,

    /// <summary>Medium: the usual (natural tempo).</summary>
    Medium = 1,

    /// <summary>High: short and strong (tight tempo).</summary>
    High = 2,
}
