using TMConcept.Providers.Common;
using TMConcept.Providers.OpenAICompatible;

namespace TMConcept.Providers.Images;

/// <summary>OpenAI image models (portrait 1024×1536, landscape 1536×1024).</summary>
public sealed class OpenAIImageEngine(OpenAIProvider provider, ProviderHttp http) : GenerationsImageEngineBase(provider, http)
{
    /// <inheritdoc />
    protected override string Size(int width, int height) => height > width ? "1024x1536" : width > height ? "1536x1024" : "1024x1024";
}
