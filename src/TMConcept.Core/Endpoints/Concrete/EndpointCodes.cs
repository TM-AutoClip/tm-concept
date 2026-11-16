using System.Globalization;
using TMConcept.Core.Models;

namespace TMConcept.Core.Endpoints;

/// <summary>
/// Provider codes of local servers: <c>endpoint:&lt;id as 32 hex digits&gt;</c>. The part before the colon is the kind the chat client
/// factory looks up (<see cref="Kind"/>), so one transport serves every saved server and no online provider's code ever contains a colon.
/// </summary>
public static class EndpointCodes
{
    private const string Prefix = ModelProviders.Endpoint + ":";

    /// <summary>The provider code of the models of endpoint <paramref name="id"/>.</summary>
    public static string For(Guid id) => Prefix + id.ToString("N", CultureInfo.InvariantCulture);

    /// <summary>Whether <paramref name="provider"/> is the code of a local server's models.</summary>
    public static bool IsEndpoint(string? provider) => provider is not null && provider.StartsWith(Prefix, StringComparison.Ordinal);

    /// <summary>The endpoint id in a provider code; false when it is not an endpoint code.</summary>
    public static bool TryId(string? provider, out Guid id)
    {
        id = Guid.Empty;
        return IsEndpoint(provider) && Guid.TryParseExact(provider![Prefix.Length..], "N", out id);
    }

    /// <summary>The kind of a provider code: <see cref="ModelProviders.Endpoint"/> for a local server's models, otherwise the code itself.</summary>
    public static string Kind(string provider)
    {
        ArgumentNullException.ThrowIfNull(provider);
        return IsEndpoint(provider) ? ModelProviders.Endpoint : provider;
    }

    /// <summary>
    /// The catalog entries of an endpoint's models: free (price 0), chat (+ vision, + schema by the user's flags), no effort levels, the
    /// display name "model · server name" so a picker shows which server runs it.
    /// </summary>
    public static IReadOnlyList<CatalogModel> Catalog(LocalEndpoint endpoint)
    {
        ArgumentNullException.ThrowIfNull(endpoint);
        var provider = For(endpoint.Id);
        return endpoint.Models.Where(m => m.Id.Length > 0).Select(m => new CatalogModel
        {
            Provider = provider,
            Id = m.Id,
            Name = (m.Name.Length > 0 ? m.Name : m.Id) + " · " + endpoint.Name,
            Capabilities = ModelCapability.Chat | (m.Vision ? ModelCapability.Vision : 0) | (m.JsonSchema ? ModelCapability.Schema : 0),
            ContextTokens = m.ContextTokens > 0 ? m.ContextTokens : EndpointDefaults.ContextTokens,
            MaxOutputTokens = m.MaxOutputTokens > 0 ? m.MaxOutputTokens : DefaultMaxOutput(m.ContextTokens),
        }).ToList();
    }

    /// <summary>
    /// The default answer limit of a model with <paramref name="contextTokens"/> of context: the context minus <see cref="EndpointDefaults.PromptReserveTokens"/>,
    /// but at least <see cref="EndpointDefaults.MaxOutputTokens"/> or half the context (whichever is less) and at most the video code's
    /// <see cref="Videos.VideoCodeBudget.MaxOutputTokens"/> (the longest answer any step asks for). 8 192 → 4 096,
    /// 32 768 → 24 576, 131 072 → 64 000. A call is lowered further to what the context leaves next to its prompt.
    /// </summary>
    public static int DefaultMaxOutput(long contextTokens)
    {
        var context = contextTokens > 0 ? contextTokens : EndpointDefaults.ContextTokens;
        var floor = Math.Max(256, Math.Min(EndpointDefaults.MaxOutputTokens, context / 2));
        return (int)Math.Min(Videos.VideoCodeBudget.MaxOutputTokens, Math.Max(floor, context - EndpointDefaults.PromptReserveTokens));
    }
}
