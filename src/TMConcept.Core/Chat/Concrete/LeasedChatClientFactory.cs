using TMConcept.Core.Models;

namespace TMConcept.Core.Chat;

/// <summary>
/// Every chat call of the app inside a graphics card lease (<see cref="IGpuCoordinator"/>): concepts, topics, the AI edit, image description,
/// image prompts and video code. An online model gets its lease at once; a model of a server on this computer waits until no other local
/// model is in use and the one on the card was unloaded. The lease covers one call (the answer's stream included), so the next step's model
/// can take the card right after.
/// </summary>
public sealed class LeasedChatClientFactory(IChatClientFactory inner, IGpuCoordinator gpu) : IChatClientFactory
{
    /// <inheritdoc />
    public IChatClient Create(CatalogModel model, string apiKey) => new Client(inner.Create(model, apiKey), gpu);

    private sealed class Client(IChatClient inner, IGpuCoordinator gpu) : IChatClient
    {
        public CatalogModel Model => inner.Model;

        public async Task<ChatResponse> SendAsync(ChatRequest request, CancellationToken cancellationToken)
        {
            var lease = await gpu.AcquireAsync(inner.Model, cancellationToken).ConfigureAwait(false);
            await using (lease.ConfigureAwait(false))
            {
                return await inner.SendAsync(request, cancellationToken).ConfigureAwait(false);
            }
        }
    }
}