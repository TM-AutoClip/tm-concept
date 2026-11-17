using Microsoft.Extensions.Logging;
using TMConcept.Core.Components;
using TMConcept.Core.Endpoints;
using TMConcept.Core.Models;

namespace TMConcept.Core.AppOllama;

/// <summary>
/// The installed models of the app's own Ollama copy as catalog entries (provider <see cref="ModelProviders.AppOllama"/>): read from the
/// models folder (<see cref="AppModelStore"/>), so the step pickers of every text step list exactly the models that are on disk, free and
/// without a key, like a local server's. <see cref="Refresh"/> reads the folder again (after a pull, a removal, and whenever the components
/// are looked at); <see cref="Changed"/> is raised only when the list changed, so the merged catalog rebuilds.
/// </summary>
public sealed class AppModelRegistry(AppModelStore store, ILogger<AppModelRegistry> logger)
{
    private readonly Lock _gate = new();
    private IReadOnlyList<CatalogModel> _catalog = [];
    private IReadOnlyList<AppModelInfo> _installed = [];
    private bool _loaded;

    /// <summary>Raised after the installed models changed; on any thread.</summary>
    public event EventHandler? Changed;

    /// <summary>The catalog entries of the installed, complete models (read from the disk on the first use).</summary>
    public IReadOnlyList<CatalogModel> Catalog
    {
        get
        {
            EnsureLoaded();
            return _catalog;
        }
    }

    /// <summary>The facts of the installed, complete models.</summary>
    public IReadOnlyList<AppModelInfo> Installed
    {
        get
        {
            EnsureLoaded();
            return _installed;
        }
    }

    /// <summary>The facts of <paramref name="tag"/>: the catalogue's, or (for a model the app does not offer) what the disk says; null when not installed.</summary>
    public AppModelInfo? Info(string tag) => Installed.FirstOrDefault(m => string.Equals(m.Tag, tag, StringComparison.Ordinal));

    /// <summary>A catalog entry by tag; null when not installed.</summary>
    public CatalogModel? Find(string tag) => Catalog.FirstOrDefault(m => string.Equals(m.Id, tag, StringComparison.Ordinal));

    /// <summary>Reads the models folder again; raises <see cref="Changed"/> when the installed models differ.</summary>
    public void Refresh()
    {
        var models = store.List().Where(m => m.Complete).Select(m => AppModelAdvisor.Find(m.Tag) ?? AppModelAdvisor.Unknown(m.Tag, m.Bytes)).ToList();
        bool changed;
        lock (_gate)
        {
            changed = !_loaded || !models.Select(m => m.Tag).SequenceEqual(_installed.Select(m => m.Tag), StringComparer.Ordinal);
            _installed = models;
            _catalog = models.Select(Entry).ToList();
            _loaded = true;
        }

        if (changed)
        {
            logger.LogInformation("{Server}: {Count} model(s) installed ({Tags})", AppOllamaDefaults.Name, models.Count,
                models.Count == 0 ? "none" : string.Join(", ", models.Select(m => m.Tag)));
            Changed?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <summary>
    /// The catalog entry of a model: chat with a JSON schema (Ollama turns it into a grammar) and vision when it reads images; free; its
    /// context and an answer limit from that context (<see cref="EndpointCodes.DefaultMaxOutput"/>); it needs the app's Ollama and itself
    /// (the storage manager shows who uses them); its graphics card class and license.
    /// </summary>
    public static CatalogModel Entry(AppModelInfo model)
    {
        ArgumentNullException.ThrowIfNull(model);
        return new CatalogModel
        {
            Provider = ModelProviders.AppOllama,
            Id = model.Tag,
            Name = model.Name + " · " + AppOllamaDefaults.Name,
            Capabilities = ModelCapability.Chat | ModelCapability.Schema | (model.Vision ? ModelCapability.Vision : 0),
            ContextTokens = model.ContextTokens,
            MaxOutputTokens = EndpointCodes.DefaultMaxOutput(model.ContextTokens),
            Requires = [ComponentCodes.AppOllama, ComponentCodes.AppOllamaModelPrefix + model.Tag],
            MinVideoMemoryGb = (int)AppModelAdvisor.Tier(model),
            MemoryGb = (int)Math.Ceiling(model.Bytes / (1024.0 * 1024 * 1024)),
            License = model.License,
            LicenseUse = model.LicenseUse,
            LicenseUrl = model.LicenseUrl,
        };
    }

    private void EnsureLoaded()
    {
        if (!_loaded)
        {
            Refresh();
        }
    }
}
