using System.Text.Json;
using Microsoft.Extensions.Logging;
using TMConcept.Core.Documents;
using TMConcept.Core.Research;
using TMConcept.Documents.Web;

namespace TMConcept.Documents.Research;

/// <summary>
/// A research backend that asks a public HTTP API itself: every request goes through the guarded <see cref="SafeHttp"/> (public addresses
/// only, size and time limits, a generic User-Agent, keys never logged) and an answer that is not the expected JSON is refused with a reason.
/// </summary>
public abstract class HttpResearchBackendBase(DocumentTransport transport, ILogger logger) : ResearchBackendBase
{
    /// <summary>The guarded HTTP.</summary>
    protected SafeHttp Http { get; } = new(transport, logger);

    /// <summary>The backend's log.</summary>
    protected ILogger Logger { get; } = logger;

    /// <summary>Reads <paramref name="body"/> as JSON; <see cref="DocumentRejectedException"/> "unreadable" when it is not.</summary>
    protected JsonDocument Json(byte[] body)
    {
        ArgumentNullException.ThrowIfNull(body);
        try
        {
            return JsonDocument.Parse(body);
        }
        catch (JsonException ex)
        {
            Logger.LogWarning("Research: {Backend} answered something that is not JSON", Id);
            throw new DocumentRejectedException("unreadable", "The answer could not be read.", ex.Message, ex);
        }
    }

    /// <summary>The string property <paramref name="name"/> of <paramref name="element"/>, or empty.</summary>
    protected static string Text(JsonElement element, string name) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() ?? string.Empty : string.Empty;
}
