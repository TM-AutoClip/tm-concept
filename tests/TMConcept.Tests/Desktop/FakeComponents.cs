using TMConcept.Core.Components;

namespace TMConcept.Tests.Desktop;

/// <summary>Only the given fake components (the real sources are removed in the tests that use it).</summary>
internal sealed class FakeComponents(params IComponent[] components) : IComponentSource
{
    public IReadOnlyList<IComponent> Components { get; } = components;
}
