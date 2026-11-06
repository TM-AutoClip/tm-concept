namespace TMConcept.Core.Components;

/// <summary>A group of components (tools, engines, voices, image models), registered in DI.</summary>
public interface IComponentSource
{
    /// <summary>The components.</summary>
    IReadOnlyList<IComponent> Components { get; }
}
