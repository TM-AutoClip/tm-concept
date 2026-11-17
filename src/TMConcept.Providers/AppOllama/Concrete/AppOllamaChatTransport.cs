using Microsoft.Extensions.Logging;
using TMConcept.Core.AppOllama;
using TMConcept.Core.Chat;
using TMConcept.Core.Models;
using TMConcept.Providers.Chat;

namespace TMConcept.Providers.AppOllama;

/// <summary>
/// The chat transport of the app's own Ollama copy (provider <see cref="ModelProviders.AppOllama"/>): no key, one client per call
/// (<see cref="AppOllamaChatClient"/>). Every chat still goes through the graphics card lease of the chat client factory, so the model
/// takes the card like any local model.
/// </summary>
public sealed class AppOllamaChatTransport(AppOllamaApi api, IAppOllamaServer server, AppModelRegistry registry, ILoggerFactory loggers) : IChatTransport
{
    private readonly ILogger _logger = loggers.CreateLogger<AppOllamaChatTransport>();

    /// <inheritdoc />
    public string Code => ModelProviders.AppOllama;

    /// <inheritdoc />
    public bool KeyRequired => false;

    /// <inheritdoc />
    public IChatClient CreateChat(CatalogModel model, string apiKey)
    {
        ArgumentNullException.ThrowIfNull(model);
        return new AppOllamaChatClient(model, registry.Info(model.Id) ?? AppModelAdvisor.Find(model.Id), server, api, _logger);
    }
}
