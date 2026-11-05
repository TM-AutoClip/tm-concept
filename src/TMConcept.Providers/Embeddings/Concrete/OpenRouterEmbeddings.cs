using TMConcept.Providers.Common;
using TMConcept.Providers.OpenAICompatible;

namespace TMConcept.Providers.Embeddings;

/// <summary>OpenRouter embeddings (its <c>/api/v1/embeddings</c> speaks the OpenAI shape; model ids like <c>openai/text-embedding-3-small</c>).</summary>
public sealed class OpenRouterEmbeddings(OpenRouterProvider provider, ProviderHttp http) : OpenAICompatibleEmbeddingsBase(provider, http);
