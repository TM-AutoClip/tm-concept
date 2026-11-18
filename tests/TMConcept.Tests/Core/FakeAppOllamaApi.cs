using System.Collections.Concurrent;
using TMConcept.Core.AppOllama;
using TMConcept.Core.Tools;

namespace TMConcept.Tests.Core;

/// <summary>
/// The app's Ollama API without any server: health answers <see cref="Up"/>, a pull reports <see cref="PullLines"/> and may write the model
/// into a store (<see cref="OnPull"/>), an unload is recorded and answers <see cref="UnloadWorks"/>.
/// </summary>
internal sealed class FakeAppOllamaApi : IAppOllamaApi
{
    public bool Up { get; set; } = true;

    public bool UnloadWorks { get; set; } = true;

    public ConcurrentQueue<string> Pulls { get; } = new();

    public ConcurrentQueue<string> Unloads { get; } = new();

    public List<string> Loaded { get; } = [];

    public List<AppModelPullProgress> PullLines { get; } = [];

    public Action<string>? OnPull { get; set; }

    /// <summary>What the registry gives for a tag's manifest (a tag not here: the registry cannot be reached).</summary>
    public Dictionary<string, byte[]> RegistryManifests { get; } = [];

    public ConcurrentQueue<string> ManifestReads { get; } = new();

    public Task<byte[]> RegistryManifestAsync(string tag, CancellationToken cancellationToken)
    {
        ManifestReads.Enqueue(tag);
        return RegistryManifests.TryGetValue(tag, out var manifest)
            ? Task.FromResult(manifest)
            : Task.FromException<byte[]>(new ToolInstallException("offline") { Code = "connection" });
    }

    public Task<bool> IsUpAsync(Uri root, CancellationToken cancellationToken) => Task.FromResult(Up);

    public Task PullAsync(Uri root, string tag, IProgress<AppModelPullProgress>? progress, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Pulls.Enqueue(tag);
        foreach (var line in PullLines)
        {
            progress?.Report(line);
        }

        OnPull?.Invoke(tag);
        return Task.CompletedTask;
    }

    public Task<bool> UnloadAsync(Uri root, string tag, CancellationToken cancellationToken)
    {
        Unloads.Enqueue(tag);
        if (UnloadWorks)
        {
            Loaded.Remove(tag);
        }

        return Task.FromResult(UnloadWorks);
    }

    public Task<IReadOnlyList<string>> LoadedAsync(Uri root, CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<string>>([.. Loaded]);
}
