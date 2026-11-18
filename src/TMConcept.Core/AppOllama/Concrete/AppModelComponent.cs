using Microsoft.Extensions.Logging;
using TMConcept.Core.Components;
using TMConcept.Core.Tools;

namespace TMConcept.Core.AppOllama;

/// <summary>
/// A model of the app's own Ollama copy as a component (<c>ollama-model.&lt;tag&gt;</c>), so it is downloaded exactly like every other
/// component: only after the user confirmed the download list (Settings → Local AI, or a saved step choice that needs it), with the install
/// window's progress and Cancel, recorded, listed and removed by the storage manager. It needs the app's Ollama program
/// (<see cref="ComponentCodes.AppOllama"/>), which is installed first. Installing pulls the tag through the app's own server (started for
/// it; never the user's Ollama); Ollama checks every layer's SHA-256 and keeps what it received, so a cancelled pull goes on from there the
/// next time. Its "folders" are its manifest and the blobs no other model uses (<see cref="AppModelStore.OwnedPaths"/>).
/// </summary>
public sealed class AppModelComponent(AppModelInfo model, AppModelStore store, AppModelRegistry registry, IAppOllamaRuntime runtime, IAppOllamaServer server,
    IAppOllamaApi api, ILogger<AppModelComponent> logger) : IComponent
{
    /// <summary>The model.</summary>
    public AppModelInfo Model { get; } = model;

    /// <inheritdoc />
    public string Code { get; } = ComponentCodes.AppOllamaModelPrefix + model.Tag;

    /// <inheritdoc />
    public string Name => Model.Name + " (" + Model.Tag + ")";

    /// <inheritdoc />
    public IReadOnlyList<string> DependsOn { get; } = [ComponentCodes.AppOllama];

    /// <inheritdoc />
    public bool Supported => runtime.Supported;

    /// <inheritdoc />
    public bool Ready => store.IsInstalled(Model.Tag);

    /// <inheritdoc />
    public bool WasInstalled => store.HasManifest(Model.Tag);

    /// <inheritdoc />
    public IReadOnlyList<ComponentDownload> Downloads() =>
        Ready ? [] : [new ComponentDownload(Name, AppOllamaDefaults.RegistryHost, Model.Bytes)];

    /// <inheritdoc />
    public IReadOnlyList<string> Folders() => store.OwnedPaths(Model.Tag);

    /// <inheritdoc />
    public Task RefreshAsync(CancellationToken cancellationToken)
    {
        registry.Refresh();
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public async Task InstallAsync(IProgress<ToolProgress>? progress, CancellationToken cancellationToken)
    {
        Uri root;
        try
        {
            root = await server.StartAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (AppOllamaException ex)
        {
            throw new ToolInstallException(ex.Message, ex) { Code = "ollama", Detail = ex.Message };
        }

        var tracker = new AppModelPullTracker(Model.Bytes);
        logger.LogInformation("Pulling {Model} ({Megabytes} MB) through {Server}", Model.Tag, Model.Bytes / 1_000_000, AppOllamaDefaults.Name);
        await api.PullAsync(root, Model.Tag, new SyncProgress(p => progress?.Report(tracker.Add(p))), cancellationToken).ConfigureAwait(false);
        registry.Refresh();
        if (!store.IsInstalled(Model.Tag))
        {
            throw new ToolInstallException($"{Model.Tag} was pulled, but its files are not complete in the app's models folder.") { Code = "pull" };
        }

        CheckManifest();

        logger.LogInformation("{Model} is installed", Model.Tag);
    }

    /// <summary>
    /// A model the repository's model catalog added (0.1.26) names the digest of its manifest: Ollama checks every layer itself, the app
    /// checks that the manifest it got is that one. A different one is removed (its manifest and the layers no other model uses) and the
    /// install fails, like a download whose SHA-256 differs.
    /// </summary>
    private void CheckManifest()
    {
        if (AppModelSet.ManifestDigest(Model.Tag) is not { } expected)
        {
            return;
        }

        string actual;
        using (var file = File.OpenRead(store.ManifestPath(Model.Tag)))
        {
            actual = "sha256:" + Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(file));
        }

        if (string.Equals(actual, expected, StringComparison.Ordinal))
        {
            return;
        }

        logger.LogError("{Model}: the pulled manifest is {Actual}, the model catalog names {Expected}; the model is removed", Model.Tag, actual, expected);
        foreach (var path in store.OwnedPaths(Model.Tag))
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }

        registry.Refresh();
        throw new ToolInstallException($"{Model.Tag}: the pulled manifest is not the one the model catalog names.") { Code = "hash" };
    }

    /// <summary>Reports on the caller's thread, in order (a pull's lines must not be reordered).</summary>
    private sealed class SyncProgress(Action<AppModelPullProgress> report) : IProgress<AppModelPullProgress>
    {
        public void Report(AppModelPullProgress value) => report(value);
    }
}
