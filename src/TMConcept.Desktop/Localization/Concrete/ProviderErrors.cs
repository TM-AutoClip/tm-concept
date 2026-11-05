namespace TMConcept.Desktop.Localization;

/// <summary>Turns a provider failure code into text (<c>provider.fail.&lt;code&gt;</c>).</summary>
public static class ProviderErrors
{
    private static readonly HashSet<string> Known = new(StringComparer.Ordinal)
    {
        "auth", "billing", "forbidden", "rate", "busy", "model", "bad_request", "too_large", "network", "timeout", "truncated", "refusal", "incomplete",
    };

    /// <summary>The text of a code (a generic text for unknown codes).</summary>
    public static string Text(Localizer text, string code)
    {
        ArgumentNullException.ThrowIfNull(text);
        return text[Known.Contains(code) ? "provider.fail." + code : "provider.fail.http"];
    }
}
