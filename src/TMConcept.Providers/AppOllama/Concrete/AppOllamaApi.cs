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
