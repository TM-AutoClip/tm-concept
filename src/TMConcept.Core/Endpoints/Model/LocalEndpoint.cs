using System.Text.Json.Serialization;

namespace TMConcept.Core.Endpoints;

/// <summary>
/// A local server the user added that speaks OpenAI's <c>chat/completions</c> (Lemonade Server, llama.cpp llama-server, Ollama, LM Studio,
/// vLLM). Its models are offered in every text step like an online provider's, free and without an API key (provider code
/// <c>endpoint:&lt;id&gt;</c>, see <see cref="EndpointCodes"/>). Kept in the encrypted database as a settings row; the optional token is kept
/// apart (never in the endpoint's own JSON) so a backup without keys holds no token, and it is never logged.
/// </summary>
public sealed class LocalEndpoint
{
    /// <summary>Stable id (part of the provider code of its models, so step choices keep pointing at it).</summary>
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>The user's name for it ("Lemonade on this PC").</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Base address up to the API version (<c>http://localhost:13305/api/v1</c>); requests go to <c>{base}/chat/completions</c>. For Ollama and
    /// LM Studio (<see cref="Kind"/>) the server's own address without <c>/v1</c> (<c>http://127.0.0.1:11434</c>).
    /// </summary>
    public string BaseUrl { get; set; } = string.Empty;

    /// <summary>What the server is (found when its models are listed): a plain OpenAI-compatible one, Ollama or LM Studio.</summary>
    [JsonConverter(typeof(JsonStringEnumConverter<EndpointKind>))]
    public EndpointKind Kind { get; set; } = EndpointKind.Generic;

    /// <summary>Optional bearer token (empty = no Authorization header). Stored apart from the endpoint JSON.</summary>
    [JsonIgnore]
    public string Token { get; set; } = string.Empty;

    /// <summary>The models of the server the user picked (or typed).</summary>
    public List<EndpointModel> Models { get; set; } = [];

    /// <summary>Added (UTC).</summary>
    public DateTimeOffset CreatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
}
