using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using TMConcept.Core.Documents;

namespace TMConcept.Documents.Web;

/// <summary>
/// A Wikipedia article in any language, by its address ("https://tr.wikipedia.org/wiki/Bizans_İmparatorluğu", the mobile one too) or by its
/// title in the chosen language: ONE request to Wikipedia's Action API (<c>action=query&amp;prop=extracts&amp;explaintext</c>, redirects
/// followed) gives the article's plain text with its section headings, kept as Markdown. A generic User-Agent names the app; nothing about
/// the user is sent.
/// </summary>
public sealed partial class WikipediaFetcher(DocumentTransport transport, ILogger<WikipediaFetcher> logger) : DocumentFetcherBase(transport, logger)
{
    /// <inheritdoc />
    public override DocumentKind Kind => DocumentKind.Wikipedia;

    /// <summary>The language and title of <paramref name="address"/> (an article address or a title in <paramref name="language"/>); null when it is neither.</summary>
    public static (string Language, string Title)? Article(string address, string language)
    {
        var text = (address ?? string.Empty).Trim();
        if (text.Length == 0)
        {
            return null;
        }

        if (WebAddress(text) is { } uri && (text.Contains("://", StringComparison.Ordinal) || text.Contains("wikipedia.org", StringComparison.OrdinalIgnoreCase)))
        {
            var host = Host().Match(uri.Host);
            const string wiki = "/wiki/";
            if (!host.Success || !uri.AbsolutePath.StartsWith(wiki, StringComparison.Ordinal))
            {
                return null;
            }

            var title = Uri.UnescapeDataString(uri.AbsolutePath[wiki.Length..]).Replace('_', ' ').Trim();
            return title.Length == 0 ? null : (host.Groups[1].Value.ToLowerInvariant(), title);
        }

        var code = (language ?? string.Empty).Trim().ToLowerInvariant();
        return Code().IsMatch(code) ? (code, text) : null;
    }

    /// <summary>The API address of an article.</summary>
    public static Uri ApiAddress(string language, string title) => new(string.Create(CultureInfo.InvariantCulture,
        $"https://{language}.wikipedia.org/w/api.php?action=query&format=json&formatversion=2&redirects=1&prop=extracts%7Cinfo&inprop=url&explaintext=1&exsectionformat=wiki&titles={Uri.EscapeDataString(title)}"));

    /// <summary>The article of an API answer: its title, address and text as Markdown; <see cref="DocumentRejectedException"/> "not_found" when there is none.</summary>
    public static (string Title, string Url, string Markdown) ParseAnswer(byte[] json, string language)
    {
        ArgumentNullException.ThrowIfNull(json);
        using var document = JsonDocument.Parse(json);
        if (!document.RootElement.TryGetProperty("query", out var query) || !query.TryGetProperty("pages", out var pages) || pages.ValueKind != JsonValueKind.Array
            || pages.GetArrayLength() == 0)
        {
            throw new DocumentRejectedException("not_found", "Wikipedia has no such article.", language);
        }

        var page = pages[0];
        if (page.TryGetProperty("missing", out _) || page.TryGetProperty("invalid", out _) || !page.TryGetProperty("extract", out var extract)
            || string.IsNullOrWhiteSpace(extract.GetString()))
        {
            throw new DocumentRejectedException("not_found", "Wikipedia has no such article.", page.TryGetProperty("title", out var t) ? t.GetString() ?? language : language);
        }

        var title = page.TryGetProperty("title", out var name) ? name.GetString() ?? string.Empty : string.Empty;
        var url = page.TryGetProperty("fullurl", out var full) ? full.GetString() ?? string.Empty : string.Empty;
        var markdown = new StringBuilder("# ").Append(title).Append("\n\n");
        foreach (var line in extract.GetString()!.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n'))
        {
            var heading = Section().Match(line);
            markdown.Append(heading.Success ? new string('#', Math.Min(6, heading.Groups[1].Value.Length)) + " " + heading.Groups[2].Value.Trim() : line).Append('\n');
        }

        return (title, url, markdown.ToString());
    }

    /// <inheritdoc />
    public override async Task<FetchedDocument> FetchAsync(string address, string language, CancellationToken cancellationToken)
    {
        var (code, title) = Article(address, language) ?? throw new DocumentRejectedException("address", "This is not a Wikipedia article or title.", address);
        var response = await GetAsync(ApiAddress(code, title), cancellationToken).ConfigureAwait(false);
        string name;
        string url;
        string markdown;
        try
        {
            (name, url, markdown) = ParseAnswer(response.Body, code);
        }
        catch (JsonException ex)
        {
            throw new DocumentRejectedException("not_text", "Wikipedia's answer could not be read.", ex.Message, ex);
        }

        return new FetchedDocument(DocumentKind.Wikipedia, name, url.Length > 0 ? url : $"https://{code}.wikipedia.org/wiki/{Uri.EscapeDataString(name.Replace(' ', '_'))}",
            code, Encoding.UTF8.GetBytes(markdown), ".md");
    }

    [GeneratedRegex(@"^([a-z]{2,3}(?:-[a-z]+)*)\.(?:m\.)?wikipedia\.org$", RegexOptions.IgnoreCase)]
    private static partial Regex Host();

    [GeneratedRegex(@"^[a-z]{2,3}(-[a-z]+)*$")]
    private static partial Regex Code();

    [GeneratedRegex(@"^(={2,7})\s*(.*?)\s*=+\s*$")]
    private static partial Regex Section();
}
