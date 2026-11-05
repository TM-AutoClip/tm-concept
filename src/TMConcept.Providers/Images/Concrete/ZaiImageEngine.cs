using TMConcept.Providers.Common;
using TMConcept.Providers.OpenAICompatible;

namespace TMConcept.Providers.Images;

/// <summary>Z.ai image models (portrait 768×1344, landscape 1344×768).</summary>
public sealed class ZaiImageEngine(ZaiProvider provider, ProviderHttp http) : GenerationsImageEngineBase(provider, http)
{
    /// <inheritdoc />
    protected override string Size(int width, int height) => height > width ? Text(768, 1344) : width > height ? Text(1344, 768) : Text(1024, 1024);
}
