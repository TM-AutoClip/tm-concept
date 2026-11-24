namespace TMConcept.Core.Scenes;

/// <summary>
/// One kind of scene transition of the app's drawer (F1, 0.1.42, <see cref="SceneTransitions.Catalog"/>): its name
/// (<paramref name="Family"/>, e.g. <c>push</c>), whether it goes in a direction (<paramref name="Directional"/>: then its full name carries
/// the direction, <c>push-left</c>) and the energies it may be used at (<paramref name="Energies"/>).
/// </summary>
public sealed record TransitionDefinition(string Family, bool Directional, IReadOnlyList<TransitionEnergy> Energies);
