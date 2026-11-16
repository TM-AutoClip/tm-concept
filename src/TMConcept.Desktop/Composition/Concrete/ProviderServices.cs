using System.Diagnostics.CodeAnalysis;
using Microsoft.Extensions.DependencyInjection;
using TMConcept.Core.Chat;
using TMConcept.Core.Costs;
using TMConcept.Core.Endpoints;
using TMConcept.Core.Models;
using TMConcept.Core.Providers;
using TMConcept.Providers.Anthropic;
using TMConcept.Providers.Catalog;
using TMConcept.Providers.Chat;
using TMConcept.Providers.Common;
using TMConcept.Providers.Endpoints;
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
        services.AddSingleton<Providers.Pricing.PriceUpdater>();
        services.AddSingleton<ICostCalculator, CostCalculator>();
        services.AddSingleton<StepModelResolver>();
        // Every chat call goes through a graphics card lease (one local model on the card at a time, 0.1.17).
        services.AddSingleton<ChatClientFactory>();
        services.AddSingleton<IChatClientFactory>(s => new LeasedChatClientFactory(s.GetRequiredService<ChatClientFactory>(), s.GetRequiredService<IGpuCoordinator>()));
        AddChat<AnthropicProvider>(services);
        AddChat<OpenAIProvider>(services);
        AddChat<OpenRouterProvider>(services);
        AddChat<ZaiProvider>(services);
        AddChat<MistralProvider>(services);
        AddChat<DeepSeekProvider>(services);
        AddProvider<FalProvider>(services);
        services.AddSingleton<Providers.Embeddings.IEmbeddingTransport, Providers.Embeddings.OpenAIEmbeddings>();
        services.AddSingleton<Providers.Embeddings.IEmbeddingTransport, Providers.Embeddings.OpenRouterEmbeddings>();
        services.AddSingleton<Providers.Embeddings.EmbeddingService>();
        services.AddSingleton<Core.Duplicates.IEmbeddingService>(s => new Core.Duplicates.LeasedEmbeddingService(
            s.GetRequiredService<Providers.Embeddings.EmbeddingService>(), s.GetRequiredService<IGpuCoordinator>()));

        // Local servers (package B): user-added OpenAI-compatible endpoints. The catalog is the embedded one plus their models; one chat
        // transport serves every server (codes endpoint:<id>) and also tests a server's address (GET models).
        services.AddSingleton(EndpointTransport.Network);
        services.AddSingleton<EndpointRegistry>();
        services.AddSingleton<EmbeddedModelCatalog>();
        services.AddSingleton<IModelCatalog, MergedModelCatalog>();
        services.AddSingleton<EndpointChatTransport>();
        services.AddSingleton<IChatTransport>(s => s.GetRequiredService<EndpointChatTransport>());
        services.AddSingleton<IEndpointProbe>(s => s.GetRequiredService<EndpointChatTransport>());
        services.AddSingleton<ILocalModelSource, EndpointModelSource>();

        // Ollama and LM Studio (0.1.17): their models leave the graphics card when another local model needs it.
        services.AddSingleton<IEndpointModelControl, OllamaModelControl>();
        services.AddSingleton<IEndpointModelControl, LmStudioModelControl>();

        // What they still hold from before (an earlier run, LM Studio's untimed load) is freed before the first graphics card lease.
        services.AddSingleton<IResidentModelSweep, EndpointResidentSweep>();
    }

    /// <summary>A provider without chat.</summary>
    public static void AddProvider<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] T>(IServiceCollection services)
        where T : class, IProvider
    {
        services.AddSingleton<T>();
        services.AddSingleton<IProvider>(s => s.GetRequiredService<T>());
    }

    private static void AddChat<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] T>(IServiceCollection services)
        where T : class, IProvider, IChatTransport
    {
        AddProvider<T>(services);
        services.AddSingleton<IChatTransport>(s => s.GetRequiredService<T>());
    }
}
