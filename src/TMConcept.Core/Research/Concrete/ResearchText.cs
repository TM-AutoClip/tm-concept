using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace TMConcept.Core.Research;

/// <summary>
/// Text helpers of "Araştırmadan": fetched text is data, so the characters that hide or reorder text (zero-width spaces, bidi overrides and
/// isolates, the byte-order mark, soft hyphens, other invisible format characters) are taken out before it is stored, and a page that says
/// "ignore the rules" in hidden text says it in plain sight inside the data block instead. Also the key that groups one address's versions,
/// and the check of a language code.
/// </summary>
public static partial class ResearchText
{
    /// <summary>Tracking parameters dropped from an address before it is compared.</summary>
    private static readonly string[] Tracking = ["utm_", "fbclid", "gclid", "mc_cid", "mc_eid", "ref", "ref_src", "igshid", "spm"];

    /// <summary>
    /// <paramref name="text"/> without invisible format characters (Unicode category Cf) except the zero-width joiner and non-joiner, which
    /// Persian, Arabic and Indic scripts need inside words.
    /// </summary>
    public static string Clean(string? text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return string.Empty;
        }

        var builder = new StringBuilder(text.Length);
        foreach (var rune in text.EnumerateRunes())
        {
            if (Rune.GetUnicodeCategory(rune) == UnicodeCategory.Format && rune.Value is not (0x200C or 0x200D))
            {
                continue;
            }

            builder.Append(rune.ToString());
        }

        return builder.ToString();
    }

    /// <summary>
    /// The key one page's addresses share: the host without "www." and "m." in lower case, the path without a trailing slash, and the query
    /// without tracking parameters (sorted); no scheme, no fragment. Empty when <paramref name="url"/> is not an http(s) address.
    /// </summary>
    public static string UrlKey(string? url)
    {
        if (!Uri.TryCreate((url ?? string.Empty).Trim(), UriKind.Absolute, out var uri) || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            return string.Empty;
        }

        var host = uri.Host.ToLowerInvariant();
        host = host.StartsWith("www.", StringComparison.Ordinal) ? host[4..] : host;
        host = host.Replace(".m.wikipedia.org", ".wikipedia.org", StringComparison.Ordinal);
        var path = Uri.UnescapeDataString(uri.AbsolutePath).TrimEnd('/');
        var query = uri.Query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries)
            .Where(p => !Tracking.Any(t => p.StartsWith(t, StringComparison.OrdinalIgnoreCase) && (t.EndsWith('_') || p.Length == t.Length || p[t.Length] == '=')))
            .Order(StringComparer.Ordinal)
            .ToList();
        return host + path + (query.Count > 0 ? "?" + string.Join('&', query) : string.Empty);
    }

    /// <summary>The language code in lower case ("tr", "en", "zh-yue"); null when <paramref name="code"/> is not one.</summary>
    public static string? Language(string? code)
    {
        var text = (code ?? string.Empty).Trim().ToLowerInvariant();
        return Code().IsMatch(text) ? text : null;
    }

    /// <summary>The language codes in <paramref name="text"/> ("fr, de el"), each once, in order; anything else is left out.</summary>
    public static IReadOnlyList<string> Languages(string? text) =>
        (text ?? string.Empty).Split([',', ';', ' ', '\n', '\t'], StringSplitOptions.RemoveEmptyEntries).Select(Language).OfType<string>().Distinct(StringComparer.Ordinal).ToList();

    [GeneratedRegex(@"^[a-z]{2,3}(-[a-z]{2,8})*$")]
    private static partial Regex Code();
}
