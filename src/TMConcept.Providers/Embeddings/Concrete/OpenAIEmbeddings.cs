using TMConcept.Providers.Common;
using TMConcept.Providers.OpenAICompatible;

namespace TMConcept.Providers.Embeddings;

/// <summary>OpenAI embeddings.</summary>
public sealed class OpenAIEmbeddings(OpenAIProvider provider, ProviderHttp http) : OpenAICompatibleEmbeddingsBase(provider, http);
