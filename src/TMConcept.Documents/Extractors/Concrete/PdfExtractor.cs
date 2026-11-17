using TMConcept.Core.Documents;
using UglyToad.PdfPig;
using UglyToad.PdfPig.DocumentLayoutAnalysis.TextExtractor;

namespace TMConcept.Documents.Extractors;

/// <summary>
/// A PDF file, page by page (PdfPig, Apache-2.0, managed code; nothing in the file is run): each page's text layer in reading order. A page
/// without text (a scan) is counted; when no page has text the document is kept as "no text" (no OCR). The document's own title is used
/// when it has one.
/// </summary>
public sealed class PdfExtractor : DocumentExtractorBase
{
    /// <inheritdoc />
    public override DocumentKind Kind => DocumentKind.Pdf;

    /// <inheritdoc />
    public override IReadOnlyList<string> Extensions { get; } = [".pdf"];

    /// <inheritdoc />
    protected override ExtractedDocument Read(string file, CancellationToken cancellationToken)
    {
        try
        {
            using var pdf = PdfDocument.Open(file);
            var sections = new List<ExtractedSection>();
            var empty = 0;
            foreach (var page in pdf.GetPages())
            {
                cancellationToken.ThrowIfCancellationRequested();
                var text = ContentOrderTextExtractor.GetText(page);
                if (string.IsNullOrWhiteSpace(text))
                {
                    text = page.Text;
                }

                if (string.IsNullOrWhiteSpace(text))
                {
                    empty++;
                    continue;
                }

                sections.Add(new ExtractedSection(page.Number, string.Empty, text));
            }

            return new ExtractedDocument(pdf.Information.Title ?? string.Empty, sections, pdf.NumberOfPages, empty);
        }
        catch (Exception ex) when (ex is not OperationCanceledException and not InvalidDataException)
        {
            // PdfPig reports a damaged or encrypted file with its own exception types: one meaning for the library.
            throw new InvalidDataException("The PDF could not be read: " + ex.Message, ex);
        }
    }
}
