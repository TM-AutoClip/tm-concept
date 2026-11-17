using System.Collections.Concurrent;
using System.Globalization;
using System.Text;
using Microsoft.Extensions.Logging;
using TMConcept.Core.Documents;

namespace TMConcept.Documents.Web;

/// <summary>
/// A web page by its address: an HTML page becomes its readable text (<see cref="ReadableText"/>, kept as Markdown); a plain text page is
/// kept as it is; an address that gives a PDF keeps the PDF. Anything else (an image, a program) is refused. No script ever runs. A page
/// the app found itself (<see cref="FetchAutomaticAsync"/>: a web search's or an MCP server's hit) is fetched only when the site's
/// robots.txt allows it (<see cref="RobotsTxt"/>, read once per site and kept for <see cref="RobotsKeep"/>); an address the user typed is
/// fetched as asked.
/// </summary>
public sealed class WebPageFetcher(DocumentTransport transport, ILogger<WebPageFetcher> logger) : DocumentFetcherBase(transport, logger)
{
    /// <summary>How long a site's robots.txt is kept (a site whose robots.txt could not be reached is asked again sooner).</summary>
    public static readonly TimeSpan RobotsKeep = TimeSpan.FromHours(1);

    private static readonly TimeSpan RobotsRetry = TimeSpan.FromMinutes(5);

    private readonly ConcurrentDictionary<string, (DateTimeOffset Until, RobotsTxt Rules)> _robots = new(StringComparer.Ordinal);

    /// <inheritdoc />
    public override DocumentKind Kind => DocumentKind.Web;

    /// <inheritdoc />
    public override async Task<FetchedDocument> FetchAsync(string address, string language, CancellationToken cancellationToken)
    {
        var uri = WebAddress(address) ?? throw new DocumentRejectedException("address", "This is not a web address.", address);
        var response = await GetAsync(uri, cancellationToken).ConfigureAwait(false);
        var url = response.Address.ToString();
        switch (response.MediaType)
        {
            case "application/pdf":
                return new FetchedDocument(DocumentKind.Web, Path.GetFileNameWithoutExtension(response.Address.AbsolutePath), url, string.Empty, response.Body, ".pdf");
            case "text/html" or "application/xhtml+xml" or "":
                var (title, text) = Readable(response, url);
                if (text.Length == 0)
                {
                    throw new DocumentRejectedException("not_text", "The page has no readable text.", url);
                }

                var markdown = (title.Length > 0 && !text.StartsWith("# ", StringComparison.Ordinal) ? "# " + title + "\n\n" : string.Empty) + text;
                return new FetchedDocument(DocumentKind.Web, title.Length > 0 ? title : response.Address.Host, url, string.Empty, Encoding.UTF8.GetBytes(markdown), ".md");
            case "text/plain" or "text/markdown":
                return new FetchedDocument(DocumentKind.Web, Path.GetFileNameWithoutExtension(response.Address.AbsolutePath) is { Length: > 0 } name ? name : response.Address.Host,
                    url, string.Empty, Encoding.UTF8.GetBytes(ReadableText.Decode(response.Body, response.Charset)), ".md");
            default:
                throw new DocumentRejectedException("not_text", "The address holds no page, text or PDF.", response.MediaType);
        }
    }

    /// <summary>
    /// A page the app found itself: fetched like <see cref="FetchAsync"/> only when the site's robots.txt allows the app; otherwise
    /// <see cref="DocumentRejectedException"/> "robots". A robots.txt the site does not have (4xx) allows everything; one that could not be
    /// reached (a server error, no answer, a rate limit) refuses everything for a few minutes (RFC 9309).
    /// </summary>
    public async Task<FetchedDocument> FetchAutomaticAsync(string address, string language, CancellationToken cancellationToken)
    {
        var uri = WebAddress(address) ?? throw new DocumentRejectedException("address", "This is not a web address.", address);
        RefuseLocal(uri);
        var rules = await RobotsAsync(uri, cancellationToken).ConfigureAwait(false);
        if (!rules.Allows(uri))
        {
            Logger.LogInformation("The robots.txt of {Host} does not allow the page", uri.Host);
            throw new DocumentRejectedException("robots", "The site's robots.txt does not allow fetching this page.", uri.Host);
        }

        return await FetchAsync(uri.ToString(), language, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>The robots.txt rules of <paramref name="page"/>'s site (kept per site).</summary>
    private async Task<RobotsTxt> RobotsAsync(Uri page, CancellationToken cancellationToken)
    {
        var site = page.GetLeftPart(UriPartial.Authority).ToLowerInvariant();
        if (_robots.TryGetValue(site, out var kept) && kept.Until > DateTimeOffset.UtcNow)
        {
            return kept.Rules;
        }

        RobotsTxt rules;
        TimeSpan keep;
        try
        {
            var response = await GetAsync(new Uri(page, "/robots.txt"), cancellationToken).ConfigureAwait(false);
            rules = RobotsTxt.Parse(ReadableText.Decode(response.Body, response.Charset));
            keep = RobotsKeep;
        }
        catch (DocumentRejectedException ex) when (ex.Code is "not_found" or "size"
            || (ex.Code == "network" && int.TryParse(ex.Detail, NumberStyles.None, CultureInfo.InvariantCulture, out var status) && status is >= 400 and < 500))
        {
            Logger.LogInformation("{Host} has no robots.txt for the app ({Code})", page.Host, ex.Code);
            rules = RobotsTxt.AllowAll;
            keep = RobotsKeep;
        }
        catch (DocumentRejectedException ex) when (ex.Code is "network" or "rate")
        {
            Logger.LogWarning("The robots.txt of {Host} could not be read ({Code}): its pages are not fetched for now", page.Host, ex.Code);
            rules = RobotsTxt.DisallowAll;
            keep = RobotsRetry;
        }

        _robots[site] = (DateTimeOffset.UtcNow + keep, rules);
        return rules;
    }

    /// <summary>
    /// The readable text of an HTML answer. A page so broken or hostile that a pattern runs out of its time limit is refused like a page without
    /// text (its reason is logged), never left to hang or escape.
    /// </summary>
    private (string Title, string Text) Readable(WebResponse response, string url)
    {
        try
        {
            return ReadableText.FromHtml(ReadableText.Decode(response.Body, response.Charset));
        }
        catch (System.Text.RegularExpressions.RegexMatchTimeoutException ex)
        {
            Logger.LogWarning(ex, "The page at {Host} could not be read in time", response.Address.Host);
            throw new DocumentRejectedException("not_text", "The page could not be read.", url, ex);
        }
    }
}
