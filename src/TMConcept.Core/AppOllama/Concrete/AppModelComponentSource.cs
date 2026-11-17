using Microsoft.Extensions.Logging;
using TMConcept.Core.Components;

namespace TMConcept.Core.AppOllama;

/// <summary>The models of the app's own Ollama copy as components: one per model the app offers (<see cref="AppModelCatalog.Models"/>).</summary>
public sealed class AppModelComponentSource(AppModelStore store, AppModelRegistry registry, IAppOllamaRuntime runtime, IAppOllamaServer server, IAppOllamaApi api,
    ILoggerFactory loggers) : IComponentSource
{
    private readonly Lazy<IReadOnlyList<IComponent>> _components = new(() => AppModelCatalog.Models
        .Select(m => (IComponent)new AppModelComponent(m, store, registry, runtime, server, api, loggers.CreateLogger<AppModelComponent>())).ToList());

    /// <inheritdoc />
    public IReadOnlyList<IComponent> Components => _components.Value;
}
