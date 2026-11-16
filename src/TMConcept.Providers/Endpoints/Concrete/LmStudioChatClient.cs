using TMConcept.Core.Chat;
using TMConcept.Core.Endpoints;
using TMConcept.Core.Models;

namespace TMConcept.Providers.Endpoints;

/// <summary>
/// A chat with an LM Studio model: before each call the model is made sure to be loaded with the context the user confirmed for it
/// (<see cref="LmStudioServer.EnsureLoadedAsync"/>; a model loaded on the first chat request would get LM Studio's default context), then
/// the call goes to LM Studio's OpenAI-compatible <c>/v1/chat/completions</c> (<paramref name="inner"/>: a schema as <c>json_schema</c>,
/// thinking blocks removed, a 503 retried while it loads).
/// </summary>
public sealed class LmStudioChatClient(IChatClient inner, LocalEndpoint endpoint, string token, LmStudioServer server) : IChatClient
{
    /// <inheritdoc />
    public CatalogModel Model => inner.Model;

    /// <inheritdoc />
    public async Task<ChatResponse> SendAsync(ChatRequest request, CancellationToken cancellationToken)
    {
        var context = Model.ContextTokens > 0 ? Model.ContextTokens : EndpointDefaults.ContextTokens;
        await server.EnsureLoadedAsync(LocalServerAddress.Root(endpoint), token, Model.Id, context,
            endpoint.Name.Length > 0 ? endpoint.Name : LmStudioServer.DefaultName, cancellationToken).ConfigureAwait(false);
        return await inner.SendAsync(request, cancellationToken).ConfigureAwait(false);
    }
}
