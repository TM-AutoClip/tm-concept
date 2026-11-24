namespace TMConcept.Core.Scenes;

/// <summary>
/// One kind of scene transition of the app's drawer (F1, 0.1.42, <see cref="SceneTransitions.Catalog"/>): its name
/// (<paramref name="Family"/>, e.g. <c>push</c>), whether it goes in a direction (<paramref name="Directional"/>: then its full name carries
/// the direction, <c>push-left</c>) and the energies it may be used at (<paramref name="Energies"/>). <paramref name="StyleOnly"/> (F13,
/// 0.1.51): offered only to a drawing style that names it in its family (<see cref="Styles.DrawStyleDefinition.Families"/>; the pixel art
/// dissolve, iris and blocks), never to a video without one.
/// </summary>
public sealed record TransitionDefinition(string Family, bool Directional, IReadOnlyList<TransitionEnergy> Energies, bool StyleOnly = false);
