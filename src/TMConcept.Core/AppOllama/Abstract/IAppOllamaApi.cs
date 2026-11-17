namespace TMConcept.Core.AppOllama;

/// <summary>
/// The calls the app makes to its own Ollama server (always at the address <see cref="IAppOllamaServer"/> gave; never to the user's Ollama):
/// whether it answers, pulling a model with progress, unloading a model (<c>keep_alive: 0</c>) and the models in memory.
/// </summary>
public interface IAppOllamaApi
{
    /// <summary>Whether a server answers <c>GET /api/version</c> at <paramref name="root"/> now (a short wait; false on no answer).</summary>
    Task<bool> IsUpAsync(Uri root, CancellationToken cancellationToken);

    /// <summary>
    /// Pulls <paramref name="tag"/> (<c>POST /api/pull</c>, streamed): every progress line goes to <paramref name="progress"/>; returns after
    /// the closing <c>success</c> line. Ollama keeps what it received of a layer, so a cancelled or broken pull goes on from there the next
    /// time. Throws <see cref="Tools.ToolInstallException"/> when Ollama reports an error or the stream ends early, and
    /// <see cref="OperationCanceledException"/> on cancel.
    /// </summary>
    Task PullAsync(Uri root, string tag, IProgress<AppModelPullProgress>? progress, CancellationToken cancellationToken);

    /// <summary>Unloads <paramref name="tag"/> (<c>keep_alive: 0</c>) and waits until it is gone; false when the server still holds it.</summary>
    Task<bool> UnloadAsync(Uri root, string tag, CancellationToken cancellationToken);

    /// <summary>The models in memory now (<c>/api/ps</c>); empty when the server does not answer.</summary>
    Task<IReadOnlyList<string>> LoadedAsync(Uri root, CancellationToken cancellationToken);
}
