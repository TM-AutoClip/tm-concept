namespace TMConcept.Core.Endpoints;

/// <summary>A model of a <see cref="LocalEndpoint"/> with the limits the user confirmed (a local server rarely reports them all).</summary>
public sealed class EndpointModel
{
    /// <summary>Model id sent to the server (as its <c>/models</c> list names it).</summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>Display name (empty = the id).</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Context window in tokens (the server's setting).</summary>
    public long ContextTokens { get; set; }

    /// <summary>Largest answer in tokens; a step that asks more is lowered to it.</summary>
    public int MaxOutputTokens { get; set; }

    /// <summary>The model reads images (offered for describing sample images).</summary>
    public bool Vision { get; set; }

    /// <summary>The server accepts <c>response_format: json_schema</c> for it (otherwise <c>json_object</c> is asked).</summary>
    public bool JsonSchema { get; set; }
}
