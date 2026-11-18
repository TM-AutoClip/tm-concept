using System.Net.Http.Headers;
using System.Text;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging;
using TMConcept.Core.AppOllama;
using TMConcept.Core.Tools;
using TMConcept.Providers.Common;
using TMConcept.Providers.Endpoints;

namespace TMConcept.Providers.AppOllama;

/// <summary>
/// <see cref="IAppOllamaApi"/> over Ollama's own API, used only with the address of the app's own server (the callers take it from
/// <see cref="IAppOllamaServer"/>; the user's Ollama is never asked). Health, unload and the loaded list reuse <see cref="OllamaServer"/>; a
/// pull streams <c>POST /api/pull</c> line by line (<see cref="AppModelPullParser"/>), with no time limit for the whole pull (a 20 GB model
/// takes hours on a slow line) but a cancel at any time; Ollama keeps the layers it received, so the next pull goes on from there.
/// </summary>
public sealed class AppOllamaApi : IAppOllamaApi, IDisposable
{
    /// <summary>The largest manifest read from the registry (real ones are a few kilobytes).</summary>
    private const int MaxManifestBytes = 1 << 20;

    /// <summary>The longest wait for the registry's manifest.</summary>
    private static readonly TimeSpan RegistryTimeout = TimeSpan.FromSeconds(30);

    private readonly ProviderHttp _http;
    private readonly HttpClient _pull;
    private readonly ILogger _logger;

    /// <summary>Over the local server HTTP layer (tests give one pointed at a fake server).</summary>
    public AppOllamaApi(EndpointTransport transport, ILoggerFactory loggers)
    {
        ArgumentNullException.ThrowIfNull(transport);
        ArgumentNullException.ThrowIfNull(loggers);
        _http = new ProviderHttp(transport.Http, loggers.CreateLogger<ProviderHttp>());
        Servers = new LocalServerHttp(transport, _http);
        Ollama = new OllamaServer(Servers, loggers.CreateLogger<OllamaServer>());
        _pull = new HttpClient(transport.Http.CreateHandler(), disposeHandler: true) { Timeout = Timeout.InfiniteTimeSpan };
        _logger = loggers.CreateLogger<AppOllamaApi>();
    }

    /// <summary>Ollama's own API (shared with the chat client of the app's models).</summary>
    public OllamaServer Ollama { get; }

    /// <summary>The local server HTTP layer.</summary>
    public LocalServerHttp Servers { get; }

    /// <inheritdoc />
    public async Task<bool> IsUpAsync(Uri root, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(root);
        var (_, version) = await Ollama.VersionAsync(root, string.Empty, cancellationToken, TimeSpan.FromSeconds(2)).ConfigureAwait(false);
        return version is not null;
    }

    /// <inheritdoc />
    public async Task PullAsync(Uri root, string tag, IProgress<AppModelPullProgress>? progress, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(root);
        if (!AppModelStore.IsValidTag(tag))
        {
            throw new ArgumentException("Not an Ollama model tag.", nameof(tag));
        }

        using var request = new HttpRequestMessage(HttpMethod.Post, new Uri(root, "api/pull"))
        {
            Content = new StringContent(new JsonObject { ["model"] = tag, ["stream"] = true }.ToJsonString(), Encoding.UTF8, "application/json"),
        };
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/x-ndjson"));
        try
        {
            using var response = await _pull.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
                var error = AppModelPullParser.Read(body)?.Error is { Length: > 0 } said ? said : body.Trim();
                _logger.LogError("{Server} refused the pull of {Model} (HTTP {Status}): {Error}", AppOllamaDefaults.Name, tag, (int)response.StatusCode, error);
                throw new ToolInstallException($"{AppOllamaDefaults.Name} refused the download of {tag} (HTTP {(int)response.StatusCode}).")
                {
                    Code = "pull",
                    Detail = error,
                };
            }

            var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
            await using (stream.ConfigureAwait(false))
            {
                using var reader = new StreamReader(stream, Encoding.UTF8);
                while (await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false) is { } line)
                {
                    if (AppModelPullParser.Read(line) is not { } item)
                    {
                        continue;
                    }

                    if (item.Error.Length > 0)
                    {
                        _logger.LogError("{Server}: the pull of {Model} failed: {Error}", AppOllamaDefaults.Name, tag, item.Error);
                        throw new ToolInstallException($"The download of {tag} failed: {item.Error}") { Code = "pull", Detail = item.Error };
                    }

                    progress?.Report(item);
                    if (item.Success)
                    {
                        _logger.LogInformation("{Server}: {Model} pulled", AppOllamaDefaults.Name, tag);
                        return;
                    }
                }
            }

            _logger.LogWarning("{Server}: the pull of {Model} ended before it finished; what was received is kept for the next try", AppOllamaDefaults.Name, tag);
            throw new ToolInstallException($"The download of {tag} ended before it finished.") { Code = "connection" };
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            _logger.LogInformation("{Server}: the pull of {Model} was cancelled; what was received is kept for the next try", AppOllamaDefaults.Name, tag);
            throw;
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException)
        {
            _logger.LogError(ex, "{Server}: the pull of {Model} broke off", AppOllamaDefaults.Name, tag);
            throw new ToolInstallException($"The download of {tag} broke off: {ex.Message}", ex) { Code = "connection", Detail = ex.Message };
        }
    }

    /// <inheritdoc />
    public async Task<byte[]> RegistryManifestAsync(string tag, CancellationToken cancellationToken)
    {
        var address = RegistryManifestAddress(tag);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(RegistryTimeout);
        using var request = new HttpRequestMessage(HttpMethod.Get, address);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.docker.distribution.manifest.v2+json"));
        try
        {
            using var response = await _pull.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogError("The registry did not give the manifest of {Model} (HTTP {Status})", tag, (int)response.StatusCode);
                throw new ToolInstallException($"Ollama's registry did not give the manifest of {tag} (HTTP {(int)response.StatusCode}).")
                {
                    Code = "pull",
                    Detail = "HTTP " + (int)response.StatusCode,
                };
            }

            var stream = await response.Content.ReadAsStreamAsync(timeout.Token).ConfigureAwait(false);
            await using (stream.ConfigureAwait(false))
            {
                using var memory = new MemoryStream();
                var buffer = new byte[16_384];
                int read;
                while ((read = await stream.ReadAsync(buffer, timeout.Token).ConfigureAwait(false)) > 0)
                {
                    memory.Write(buffer, 0, read);
                    if (memory.Length > MaxManifestBytes)
                    {
                        throw new ToolInstallException($"The registry's manifest of {tag} is larger than a manifest can be.") { Code = "hash" };
                    }
                }

                return memory.ToArray();
            }
        }
        catch (OperationCanceledException ex) when (!cancellationToken.IsCancellationRequested)
        {
            _logger.LogError("The registry did not answer for the manifest of {Model} within {Seconds} s", tag, RegistryTimeout.TotalSeconds);
            throw new ToolInstallException($"Ollama's registry did not answer for {tag}.", ex) { Code = "connection", Detail = ex.Message };
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException)
        {
            _logger.LogError(ex, "The manifest of {Model} could not be read from the registry", tag);
            throw new ToolInstallException($"Ollama's registry could not be reached for {tag}: {ex.Message}", ex) { Code = "connection", Detail = ex.Message };
        }
    }

    /// <summary>
    /// The registry address of <paramref name="tag"/>'s manifest, laid out as Ollama lays it out (<c>qwen3.5:4b</c> →
    /// <c>https://registry.ollama.ai/v2/library/qwen3.5/manifests/4b</c>).
    /// </summary>
    public static Uri RegistryManifestAddress(string tag)
    {
        if (!AppModelStore.IsValidTag(tag))
        {
            throw new ArgumentException("Not an Ollama model tag.", nameof(tag));
        }

        var colon = tag.LastIndexOf(':');
        var name = colon > 0 ? tag[..colon] : tag;
        var version = colon > 0 ? tag[(colon + 1)..] : "latest";
        var parts = name.Split('/');
        var space = parts.Length > 1 ? parts[0] : "library";
        return new Uri("https://" + AppOllamaDefaults.RegistryHost + "/v2/" + Uri.EscapeDataString(space) + "/" + Uri.EscapeDataString(parts[^1]) + "/manifests/"
            + Uri.EscapeDataString(version));
    }

    /// <inheritdoc />
    public Task<bool> UnloadAsync(Uri root, string tag, CancellationToken cancellationToken) =>
        Ollama.UnloadAsync(root, string.Empty, tag, AppOllamaDefaults.Name, cancellationToken);

    /// <inheritdoc />
    public async Task<IReadOnlyList<string>> LoadedAsync(Uri root, CancellationToken cancellationToken) =>
        (await Ollama.LoadedAsync(root, string.Empty, cancellationToken).ConfigureAwait(false)).Select(m => m.Id).ToList();

    /// <inheritdoc />
    public void Dispose()
    {
        _pull.Dispose();
        Servers.Dispose();
        _http.Dispose();
    }
}
