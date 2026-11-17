namespace TMConcept.Core.Components;

/// <summary>
/// A component that runs with other components it does not install through <see cref="ComponentPlanner"/> (an engine renders with the shared
/// Chrome Headless Shell, which the engine's own install fetches). Not a <see cref="IComponent.DependsOn"/>: nothing is planned or installed
/// for it; it only counts for who uses what (<see cref="DiskSpace.IComponentUsage"/>), so "Clean up unused" never takes what an engine in use
/// renders with.
/// </summary>
public interface IComponentUses
{
    /// <summary>The codes of the components it runs with (<see cref="ComponentCodes"/>).</summary>
    IReadOnlyList<string> Uses { get; }
}
