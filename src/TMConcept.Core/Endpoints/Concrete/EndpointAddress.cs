namespace TMConcept.Core.Endpoints;

/// <summary>
/// The base address of a local server as the user typed it, checked: an absolute <c>http</c> or <c>https</c> address without query or
/// fragment (a local server usually has no certificate, so plain http is allowed here, unlike the online providers' fixed https addresses),
/// and without a user name or password in it (<c>http://user:pass@host</c>): the address is kept with the settings, shown in plain text and
/// travels in a backup without API keys, so a secret belongs in the token, which is kept apart. The result ends with a slash, so
/// <c>new Uri(base, "models")</c> keeps the <c>/v1</c> or <c>/api/v1</c> part.
/// </summary>
public static class EndpointAddress
{
    /// <summary>The checked base address; false for anything else.</summary>
    public static bool TryParse(string? text, out Uri address)
    {
        address = null!;
        if (!TryCreate(text, out var parsed) || parsed.Scheme is not ("http" or "https") || parsed.Query.Length > 0 || parsed.Fragment.Length > 0
            || parsed.Host.Length == 0 || parsed.UserInfo.Length > 0)
        {
            return false;
        }

        address = parsed;
        return true;
    }

    /// <summary>Whether the text is an address with a user name or password in it (refused by <see cref="TryParse"/>, with its own reason).</summary>
    public static bool HasCredentials(string? text) => TryCreate(text, out var parsed) && parsed.UserInfo.Length > 0;

    private static bool TryCreate(string? text, out Uri parsed) =>
        Uri.TryCreate((text ?? string.Empty).Trim().TrimEnd('/') + "/", UriKind.Absolute, out parsed!);
}
