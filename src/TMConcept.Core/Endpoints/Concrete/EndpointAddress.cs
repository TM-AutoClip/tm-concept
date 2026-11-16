namespace TMConcept.Core.Endpoints;

/// <summary>
/// The base address of a local server as the user typed it, checked: an absolute <c>http</c> or <c>https</c> address without query or
/// fragment (a local server usually has no certificate, so plain http is allowed here, unlike the online providers' fixed https addresses).
/// The result ends with a slash, so <c>new Uri(base, "models")</c> keeps the <c>/v1</c> or <c>/api/v1</c> part.
/// </summary>
public static class EndpointAddress
{
    /// <summary>The checked base address; false for anything else.</summary>
    public static bool TryParse(string? text, out Uri address)
    {
        address = null!;
        var trimmed = (text ?? string.Empty).Trim().TrimEnd('/');
        if (!Uri.TryCreate(trimmed + "/", UriKind.Absolute, out var parsed) || parsed.Scheme is not ("http" or "https")
            || parsed.Query.Length > 0 || parsed.Fragment.Length > 0 || parsed.Host.Length == 0)
        {
            return false;
        }

        address = parsed;
        return true;
    }
}
