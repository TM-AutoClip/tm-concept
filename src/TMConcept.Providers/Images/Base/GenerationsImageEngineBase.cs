using System.Globalization;
using System.Text;
using System.Text.Json.Nodes;
using TMConcept.Core.Images;
using TMConcept.Core.Providers;
using TMConcept.Providers.Common;

namespace TMConcept.Providers.Images;

/// <summary>
/// The OpenAI-style <c>POST images/generations</c> endpoint (OpenAI, Z.ai): the answer has <c>data[0].b64_json</c> or <c>data[0].url</c>.
/// A url is downloaded without the key and only over HTTPS.
/// </summary>
public abstract class GenerationsImageEngineBase(ProviderBase provider, ProviderHttp http) : IImageEngine
{
    /// <inheritdoc />
    public string Code => provider.Code;

    /// <summary>The size text the provider accepts for a wanted size.</summary>
    protected abstract string Size(int width, int height);

    /// <inheritdoc />
    public async Task<ImageResult> GenerateAsync(ImageRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var key = request.Model.Key.Secret;
        var body = new JsonObject { ["model"] = request.Model.Model.Id, ["prompt"] = request.Prompt, ["size"] = Size(request.Width, request.Height), ["n"] = 1 }
            .ToJsonString();
        var reply = await http.SendAsync(() =>
        {
            var message = new HttpRequestMessage(HttpMethod.Post, new Uri(provider.Address, "images/generations"))
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json"),
            };
            provider.Authorize(message, key);
            return message;
        }, provider.Name, key, cancellationToken).ConfigureAwait(false);
        var item = JsonNode.Parse(reply.Text)?["data"]?[0];
        byte[] bytes;
        if (item?["b64_json"]?.ToString() is { Length: > 0 } base64)
        {
            bytes = Convert.FromBase64String(base64);
        }
        else if (item?["url"]?.ToString() is { } url && Uri.TryCreate(url, UriKind.Absolute, out var address) && address.Scheme == Uri.UriSchemeHttps)
        {
            bytes = (await http.SendAsync(() => new HttpRequestMessage(HttpMethod.Get, address), provider.Name, key, cancellationToken).ConfigureAwait(false)).Body;
        }
        else
        {
            throw new ProviderException($"{provider.Name}: the answer has no image.") { Code = "incomplete" };
        }

        Directory.CreateDirectory(Path.GetDirectoryName(request.Output)!);
        await File.WriteAllBytesAsync(request.Output, bytes, cancellationToken).ConfigureAwait(false);
        return new ImageResult(request.Output, request.Model.Model.ImageUsd);
    }

    /// <summary>"WxH".</summary>
    protected static string Text(int width, int height) => string.Create(CultureInfo.InvariantCulture, $"{width}x{height}");
}
