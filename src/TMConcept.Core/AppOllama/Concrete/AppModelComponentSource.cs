using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using TMConcept.Core.Components;

namespace TMConcept.Core.AppOllama;

/// <summary>
/// The models of the app's own Ollama copy as components: one per model the app offers (<see cref="AppModelCatalog.Models"/>), and one per
/// installed model the catalogue no longer names (a tag an older version offered: it stays in the step pickers through
/// <see cref="AppModelRegistry"/>, so the storage manager must list and remove it too; its facts come from the disk,
/// <see cref="AppModelAdvisor.Unknown"/>). The list follows the registry, so it changes after a refresh; each tag keeps one component.
/// </summary>
public sealed class AppModelComponentSource(AppModelStore store, AppModelRegistry registry, IAppOllamaRuntime runtime, IAppOllamaServer server, IAppOllamaApi api,
    ILoggerFactory loggers) : IComponentSource
{
    private readonly Lazy<IReadOnlyList<IComponent>> _offered = new(() => AppModelCatalog.Models
        .Select(m => (IComponent)new AppModelComponent(m, store, registry, runtime, server, api, loggers.CreateLogger<AppModelComponent>())).ToList());

    private readonly ConcurrentDictionary<string, IComponent> _others = new(StringComparer.Ordinal);

    /// <inheritdoc />
    public IReadOnlyList<IComponent> Components
    {
        get
        {
            var others = registry.Installed.Where(m => AppModelAdvisor.Find(m.Tag) is null)
                .Select(m => _others.GetOrAdd(m.Tag, _ => new AppModelComponent(m, store, registry, runtime, server, api, loggers.CreateLogger<AppModelComponent>())));
            return [.. _offered.Value, .. others];
        }
    }
}
