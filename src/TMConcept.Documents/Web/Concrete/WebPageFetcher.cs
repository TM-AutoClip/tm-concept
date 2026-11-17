using System.Text;
using Microsoft.Extensions.Logging;
using TMConcept.Core.Documents;

namespace TMConcept.Documents.Web;

/// <summary>
/// A web page by its address: an HTML page becomes its readable text (<see cref="ReadableText"/>, kept as Markdown); a plain text page is
/// kept as it is; an address that gives a PDF keeps the PDF. Anything else (an image, a program) is refused. No script ever runs.
/// </summary>
public sealed class WebPageFetcher(DocumentTransport transport, ILogger<WebPageFetcher> logger) : DocumentFetcherBase(transport, logger)
{
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
                var (title, text) = ReadableText.FromHtml(ReadableText.Decode(response.Body, response.Charset));
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
}
