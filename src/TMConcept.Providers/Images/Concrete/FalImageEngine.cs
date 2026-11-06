using System.Text;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging;
using TMConcept.Core.Images;
using TMConcept.Core.Providers;
using TMConcept.Providers.Common;
using TMConcept.Providers.Fal;

namespace TMConcept.Providers.Images;

/// <summary>
/// fal image models through the queue API: submit (<c>POST queue.fal.run/&lt;model&gt;</c>), poll the status address, read the result and
/// download the image. The key goes only to queue.fal.run; the image itself is downloaded without the key, only from fal's own hosts.
/// </summary>
public sealed class FalImageEngine(FalProvider provider, ProviderHttp http, ProviderTransport transport, ILogger<FalImageEngine> logger) : IImageEngine
{
    private static readonly string[] ImageHosts = ["fal.media", "v3.fal.media", "v3b.fal.media", "storage.googleapis.com"];

    /// <inheritdoc />
    public string Code => provider.Code;

    /// <summary>Longest wait for one image.</summary>
    public static TimeSpan MaxWait { get; } = TimeSpan.FromMinutes(5);

    /// <inheritdoc />
    public async Task<ImageResult> GenerateAsync(ImageRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var key = request.Model.Key.Secret;
        var body = new JsonObject
        {
            ["prompt"] = request.Prompt,
            ["image_size"] = new JsonObject { ["width"] = request.Width, ["height"] = request.Height },
            ["num_images"] = 1,
            ["enable_safety_checker"] = true,
        }.ToJsonString();
        var submitted = JsonNode.Parse((await SendAsync(HttpMethod.Post, new Uri(provider.Address, request.Model.Model.Id), body, key, poll: false, cancellationToken)
            .ConfigureAwait(false)).Text);
        var status = Own(submitted?["status_url"]?.ToString());
        var result = Own(submitted?["response_url"]?.ToString());
        var started = DateTimeOffset.UtcNow;
        var polls = 0;
        while (true)
        {
            polls++;
            var state = JsonNode.Parse((await SendAsync(HttpMethod.Get, status, null, key, poll: true, cancellationToken).ConfigureAwait(false)).Text)?["status"]?.ToString();
            if (state == "COMPLETED")
            {
                break;
            }

            if (state is not ("IN_QUEUE" or "IN_PROGRESS") || DateTimeOffset.UtcNow - started > MaxWait)
            {
                logger.LogWarning("fal image {Model} did not finish ({State}) after {Polls} status checks in {Seconds:0} s", request.Model.Model.Id,
                    state ?? "unknown", polls, (DateTimeOffset.UtcNow - started).TotalSeconds);
                throw new ProviderException($"fal: the image did not finish ({state ?? "unknown"}).") { Code = state is null ? "incomplete" : "timeout" };
            }

            await transport.Delay(TimeSpan.FromSeconds(1), cancellationToken).ConfigureAwait(false);
        }

        var answer = JsonNode.Parse((await SendAsync(HttpMethod.Get, result, null, key, poll: false, cancellationToken).ConfigureAwait(false)).Text);
        var url = answer?["images"]?[0]?["url"]?.ToString() ?? throw new ProviderException("fal: the answer has no image.") { Code = "incomplete" };
        if (!Uri.TryCreate(url, UriKind.Absolute, out var address) || address.Scheme != Uri.UriSchemeHttps
            || !ImageHosts.Any(h => address.Host == h || address.Host.EndsWith("." + h, StringComparison.OrdinalIgnoreCase)))
        {
            throw new ProviderException("fal: the image address is not one of fal's hosts.") { Code = "bad_request" };
        }

        var image = await http.SendAsync(() => new HttpRequestMessage(HttpMethod.Get, address), provider.Name, key, cancellationToken).ConfigureAwait(false);
        Directory.CreateDirectory(Path.GetDirectoryName(request.Output)!);
        await File.WriteAllBytesAsync(request.Output, image.Body, cancellationToken).ConfigureAwait(false);
        logger.LogInformation("fal image {Model} done in {Seconds:0} s ({Polls} status checks)", request.Model.Model.Id,
            (DateTimeOffset.UtcNow - started).TotalSeconds, polls);
        return new ImageResult(request.Output, request.Model.Model.ImageUsd);
    }

    private Uri Own(string? address)
    {
        if (address is null || !Uri.TryCreate(address, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps || uri.Host != provider.Address.Host)
        {
            throw new ProviderException("fal: an address outside queue.fal.run was returned.") { Code = "bad_request" };
        }

        return uri;
    }

    private Task<ProviderReply> SendAsync(HttpMethod method, Uri address, string? body, string key, bool poll, CancellationToken cancellationToken)
    {
        Func<HttpRequestMessage> create = () =>
        {
            var message = new HttpRequestMessage(method, address);
            if (body is not null)
            {
                message.Content = new StringContent(body, Encoding.UTF8, "application/json");
            }

            provider.Authorize(message, key);
            return message;
        };
        return poll ? http.PollAsync(create, provider.Name, key, cancellationToken) : http.SendAsync(create, provider.Name, key, cancellationToken);
    }
}
