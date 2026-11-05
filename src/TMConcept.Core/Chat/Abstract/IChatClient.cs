using TMConcept.Core.Models;

namespace TMConcept.Core.Chat;

/// <summary>A chat model bound to a key.</summary>
public interface IChatClient
{
    /// <summary>The model.</summary>
    CatalogModel Model { get; }

    /// <summary>Sends; <see cref="Providers.ProviderException"/> when the call fails or the answer is cut off or refused.</summary>
    Task<ChatResponse> SendAsync(ChatRequest request, CancellationToken cancellationToken);
}
