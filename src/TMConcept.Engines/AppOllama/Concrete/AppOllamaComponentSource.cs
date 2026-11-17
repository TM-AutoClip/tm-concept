using TMConcept.Core.Components;

namespace TMConcept.Engines.AppOllama;

/// <summary>The app's own Ollama program as a component (its models are components of their own, see Core's AppModelComponentSource).</summary>
public sealed class AppOllamaComponentSource(AppOllamaComponent runtime) : IComponentSource
{
    private readonly IReadOnlyList<IComponent> _components = [runtime];

    /// <inheritdoc />
    public IReadOnlyList<IComponent> Components => _components;
}
