using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
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

    /// <summary>
    /// The manifest digest the repository's model catalog pins for this model (<c>sha256:…</c>); null for a built-in model (tests give one).
    /// </summary>
    public string? ManifestDigest { get; init; } = AppModelSet.ManifestDigest(model.Tag);

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
        // A pinned model: the registry's manifest for the tag is checked first, so a moved tag downloads nothing.
        var pinned = ManifestDigest is { } expected ? await PinnedLayersAsync(expected, cancellationToken).ConfigureAwait(false) : null;
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

        if (pinned is not null)
        {
            CheckLayers(pinned);
        }

        logger.LogInformation("{Model} is installed", Model.Tag);
    }

    /// <summary>
    /// A model the repository's model catalog added (0.1.26) names the digest of its manifest on Ollama's registry. Before the pull the
    /// registry's manifest for the tag is read and hashed (a few kilobytes): a different one means the tag moved, and nothing is downloaded.
    /// The layers it names (config included) are what the pull must bring; Ollama checks every layer's SHA-256 itself. Ollama writes its own
    /// serialisation of the manifest, so the local file's bytes are never compared, only the layers it names.
    /// </summary>
    private async Task<IReadOnlyList<string>> PinnedLayersAsync(string expected, CancellationToken cancellationToken)
    {
        var manifest = await api.RegistryManifestAsync(Model.Tag, cancellationToken).ConfigureAwait(false);
        var actual = "sha256:" + Convert.ToHexStringLower(SHA256.HashData(manifest));
        if (!string.Equals(actual, expected, StringComparison.Ordinal))
        {
            logger.LogError("{Model}: the registry's manifest is {Actual}, the model catalog names {Expected}; nothing is downloaded", Model.Tag, actual, expected);
            throw new ToolInstallException($"{Model.Tag}: the registry now names another model than the model catalog; nothing was downloaded.") { Code = "hash" };
        }

        return Layers(manifest) ?? throw new ToolInstallException($"{Model.Tag}: the registry's manifest names no layers.") { Code = "hash" };
    }

    /// <summary>The pulled model must use exactly the pinned manifest's layers; a different one is removed and the install fails.</summary>
    private void CheckLayers(IReadOnlyList<string> pinned)
    {
        var pulled = store.Read(Model.Tag)?.Digests ?? [];
        if (pulled.ToHashSet(StringComparer.Ordinal).SetEquals(pinned))
        {
            return;
        }

        logger.LogError("{Model}: the pulled model uses other layers than the pinned manifest names (the tag moved during the pull); the model is removed",
            Model.Tag);
        foreach (var path in store.OwnedPaths(Model.Tag))
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }

        registry.Refresh();
        throw new ToolInstallException($"{Model.Tag}: the pulled model is not the one the model catalog names.") { Code = "hash" };
    }

    /// <summary>The config and layer digests a manifest names; null when it is not a manifest with layers.</summary>
    internal static IReadOnlyList<string>? Layers(byte[] manifest)
    {
        try
        {
            if (JsonNode.Parse(manifest) is not JsonObject root || root["layers"] is not JsonArray layers || layers.Count == 0)
            {
                return null;
            }

            var digests = new List<string>();
            foreach (var node in layers.Prepend(root["config"]))
            {
                if (node?["digest"] is not JsonValue digest || digest.GetValueKind() != JsonValueKind.String)
                {
                    return null;
                }

                digests.Add(digest.GetValue<string>());
            }

            return digests;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>Reports on the caller's thread, in order (a pull's lines must not be reordered).</summary>
    private sealed class SyncProgress(Action<AppModelPullProgress> report) : IProgress<AppModelPullProgress>
    {
        public void Report(AppModelPullProgress value) => report(value);
    }
}
