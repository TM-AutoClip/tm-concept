using Microsoft.Extensions.DependencyInjection;
using TMConcept.Core.Chat;
using TMConcept.Core.Costs;
using TMConcept.Core.Models;
using TMConcept.Core.Providers;
using TMConcept.Providers.Anthropic;
using TMConcept.Providers.Catalog;
using TMConcept.Providers.Chat;
using TMConcept.Providers.Common;
using TMConcept.Providers.Fal;
using TMConcept.Providers.OpenAICompatible;

namespace TMConcept.Desktop.Composition;

/// <summary>Registers the online providers (one class each; a new provider = one more line here and its catalog file).</summary>
public static class ProviderServices
{
    /// <summary>Adds providers, the catalog, cost math and the step model resolver.</summary>
    public static void Add(IServiceCollection services)
    {
        services.AddSingleton(ProviderTransport.Network);
        services.AddSingleton<ProviderHttp>();
        services.AddSingleton<IModelCatalog, EmbeddedModelCatalog>();
        services.AddSingleton<ICostCalculator, CostCalculator>();
        services.AddSingleton<StepModelResolver>();
        services.AddSingleton<IChatClientFactory, ChatClientFactory>();
        AddChat<AnthropicProvider>(services);
        AddChat<OpenAIProvider>(services);
        AddChat<OpenRouterProvider>(services);
        AddChat<ZaiProvider>(services);
        AddChat<MistralProvider>(services);
        AddChat<DeepSeekProvider>(services);
        AddProvider<FalProvider>(services);
        services.AddSingleton<Providers.Embeddings.IEmbeddingTransport, Providers.Embeddings.OpenAIEmbeddings>();
        services.AddSingleton<Providers.Embeddings.IEmbeddingTransport, Providers.Embeddings.OpenRouterEmbeddings>();
        services.AddSingleton<Core.Duplicates.IEmbeddingService, Providers.Embeddings.EmbeddingService>();
    }

    /// <summary>A provider without chat.</summary>
    public static void AddProvider<T>(IServiceCollection services)
        where T : class, IProvider
    {
        services.AddSingleton<T>();
        services.AddSingleton<IProvider>(s => s.GetRequiredService<T>());
    }

    private static void AddChat<T>(IServiceCollection services)
        where T : class, IProvider, IChatTransport
    {
        AddProvider<T>(services);
        services.AddSingleton<IChatTransport>(s => s.GetRequiredService<T>());
    }
}
