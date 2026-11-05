using System.Text;
using System.Text.Json.Nodes;
using TMConcept.Core.Duplicates;
using TMConcept.Core.Models;
using TMConcept.Core.Providers;
using TMConcept.Providers.Common;
using TMConcept.Providers.OpenAICompatible;

namespace TMConcept.Providers.Embeddings;

/// <summary>
/// Embeddings over the OpenAI shape (<c>POST embeddings</c> under the provider's address, batches of up to 100 texts). OpenAI and OpenRouter
/// both serve it; only the provider (address, key header) differs.
/// </summary>
public abstract class OpenAICompatibleEmbeddingsBase(OpenAICompatibleProviderBase provider, ProviderHttp http) : IEmbeddingTransport
{
    /// <inheritdoc />
    public string Code => provider.Code;

    /// <inheritdoc />
    public async Task<EmbeddingResult> EmbedAsync(CatalogModel model, string apiKey, IReadOnlyList<string> texts, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(texts);
        var vectors = new List<float[]>();
        long tokens = 0;
        foreach (var chunk in texts.Chunk(100))
        {
            var body = new JsonObject { ["model"] = model.Id, ["input"] = new JsonArray(chunk.Select(t => (JsonNode?)JsonValue.Create(t)).ToArray()) }.ToJsonString();
            var reply = await http.SendAsync(() =>
            {
                var message = new HttpRequestMessage(HttpMethod.Post, new Uri(provider.Address, "embeddings"))
                {
                    Content = new StringContent(body, Encoding.UTF8, "application/json"),
                };
                provider.Authorize(message, apiKey);
                return message;
            }, provider.Name, apiKey, cancellationToken).ConfigureAwait(false);
            var node = JsonNode.Parse(reply.Text);
            if (node?["error"] is not null)
            {
                // OpenRouter reports an upstream failure as a 200 answer with only an error object.
                throw http.Failure(provider.Name, 502, reply.Text, reply.RequestId, apiKey);
            }

            var data = node?["data"] as JsonArray ?? throw new ProviderException("The embedding answer has no data.") { Code = "incomplete" };
            foreach (var item in data.OrderBy(d => d?["index"]?.GetValue<int>() ?? 0))
            {
                vectors.Add((item?["embedding"] as JsonArray)?.Select(v => v!.GetValue<float>()).ToArray() ?? []);
            }

            tokens += Common.JsonRead.Long(node?["usage"]?["total_tokens"] ?? node?["usage"]?["prompt_tokens"]);
        }

        if (vectors.Count != texts.Count)
        {
            throw new ProviderException("The embedding answer has the wrong number of vectors.") { Code = "incomplete" };
        }

        return new EmbeddingResult(vectors, tokens);
    }
}
