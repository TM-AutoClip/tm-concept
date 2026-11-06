using System.Text.RegularExpressions;

namespace TMConcept.Core.Security;

/// <summary>
/// Safety net for logs and error texts: masks anything that looks like an API key, a bearer header, a secret query value or a key field in
/// JSON. Code never logs secrets on purpose; this catches what a provider echoes back in an error body. Known keys can also be masked by
/// value (<see cref="Redact(string?, IEnumerable{string})"/>).
/// </summary>
public static partial class Redactor
{
    /// <summary>The text with secrets replaced by <c>***</c>.</summary>
    public static string Redact(string? text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return text ?? string.Empty;
        }

        var result = KeyLike().Replace(text, "***");
        result = QueryValue().Replace(result, m => m.Groups[1].Value + "=***");
        result = Bearer().Replace(result, m => m.Groups[1].Value + " ***");
        result = HeaderValue().Replace(result, m => m.Groups[1].Value + "***");
        result = JsonValue().Replace(result, m => m.Groups[1].Value + "\"***\"");
        return result;
    }

    /// <summary>The text with the given secret values masked too (values shorter than 8 characters are ignored).</summary>
    public static string Redact(string? text, IEnumerable<string> secrets)
    {
        ArgumentNullException.ThrowIfNull(secrets);
        var result = text ?? string.Empty;
        foreach (var secret in secrets.Where(s => !string.IsNullOrEmpty(s) && s.Length >= 8))
        {
            result = result.Replace(secret, "***", StringComparison.Ordinal);
        }

        return Redact(result);
    }

    /// <summary>Masks a key for display: only the first four and the last four characters (<c>sk-a…wxyz</c>).</summary>
    public static string Mask(string? key)
    {
        if (string.IsNullOrEmpty(key))
        {
            return string.Empty;
        }

        return key.Length <= 12 ? new string('•', key.Length) : key[..4] + "…" + key[^4..];
    }

    // Provider keys (OpenAI / Anthropic / OpenRouter / DeepSeek "sk-…", Google "AIza…", fal "uuid:hex", Z.ai "hex.secret"), GitHub tokens,
    // AWS access key ids, Slack tokens and JSON web tokens.
    [GeneratedRegex(@"(?<![A-Za-z0-9_-])(?:sk-(?:ant-|proj-|or-v1-)?[A-Za-z0-9_-]{16,}|AIza[0-9A-Za-z_-]{30,}|[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}:[0-9a-f]{32}|[0-9a-f]{32}\.[A-Za-z0-9]{16}|(?:gh[pousr]_|github_pat_)[A-Za-z0-9_]{20,}|AKIA[0-9A-Z]{16}|xox[abprs]-[A-Za-z0-9-]{10,}|eyJ[A-Za-z0-9_-]{8,}\.eyJ[A-Za-z0-9_-]{8,}\.[A-Za-z0-9_-]{8,})")]
    private static partial Regex KeyLike();

    [GeneratedRegex(@"\b((?:api_key|apikey|key|access_token|token|password|secret)\s*)=[^&\s""']+", RegexOptions.IgnoreCase)]
    private static partial Regex QueryValue();

    [GeneratedRegex(@"\b(Bearer|Key|Basic)\s+[A-Za-z0-9._~+/=:-]{8,}", RegexOptions.IgnoreCase)]
    private static partial Regex Bearer();

    [GeneratedRegex(@"((?:x-api-key|x-goog-api-key|authorization)\s*[:=]\s*)[^\s,;""']{8,}", RegexOptions.IgnoreCase)]
    private static partial Regex HeaderValue();

    [GeneratedRegex(@"(""(?:api_key|apiKey|key|access_token|token|password|secret)""\s*:\s*)""[^""]*""", RegexOptions.IgnoreCase)]
    private static partial Regex JsonValue();
}
