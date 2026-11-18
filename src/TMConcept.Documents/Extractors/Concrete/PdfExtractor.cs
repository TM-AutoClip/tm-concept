using TMConcept.Core.Documents;
using UglyToad.PdfPig;
using UglyToad.PdfPig.Content;
using UglyToad.PdfPig.DocumentLayoutAnalysis.TextExtractor;

namespace TMConcept.Documents.Extractors;

/// <summary>
/// A PDF file, page by page (PdfPig, Apache-2.0, managed code; nothing in the file is run): each page's text layer in reading order. Since
/// 0.1.24 a PDF of any length up to <see cref="DocumentLimits.PdfBytesMax"/> is read from a stream of the file, one page at a time, opened
/// again every <see cref="DocumentLimits.PdfReopenPages"/> pages so the memory stays about the same whatever the page count; only the asked
/// page range is read; every page is reported and a cancel stops between pages; reading stops at <see cref="DocumentLimits.CharactersMax"/>
/// characters. A page without text (a scan) is counted, or, when a text-extraction model was chosen, its images
/// (<see cref="PdfPageImages"/>) are read by it; when that engine stops (<see cref="PageRecognitionStoppedException"/>: a wrong key, too many
/// failures in a row) the rest of the pages are still read for their text layer and the document keeps what was read, with the reason's code
/// (<see cref="ExtractedDocument.RecognitionStopped"/>: the provider's error code, or <c>error</c>; the details are in the log). The document's own title is used when it has one.
/// 0.1.25: with a <see cref="DocumentReadOptions.Memory"/>, a page read before is taken as kept (the PDF page is not even parsed), and every
/// page read now is kept: its text layer, or the engine's reading at once (also an empty one), so a cancel or a stop loses nothing that was
/// paid for or waited for. A scanned page whose images are all JPEG 2000, JBIG2 or CCITT fax is counted apart
/// (<see cref="ExtractedDocument.PagesUnsupported"/>).
/// </summary>
public sealed class PdfExtractor : DocumentExtractorBase
{
    /// <inheritdoc />
    public override DocumentKind Kind => DocumentKind.Pdf;

    /// <inheritdoc />
    public override IReadOnlyList<string> Extensions { get; } = [".pdf"];

    /// <inheritdoc />
    public override long MaxBytes => DocumentLimits.PdfBytesMax;

    /// <summary>At most <paramref name="most"/> page numbers spread evenly over <paramref name="pages"/> pages (the first and the last included).</summary>
    public static IReadOnlyList<int> Sample(int pages, int most)
    {
        if (pages <= 0 || most <= 0)
        {
            return [];
        }

        if (pages <= most)
        {
            return Enumerable.Range(1, pages).ToList();
        }

        return Enumerable.Range(0, most).Select(i => 1 + (int)Math.Round(i * (pages - 1) / (double)(most - 1))).Distinct().ToList();
    }

    /// <inheritdoc />
    protected override ExtractedDocument Read(string file, CancellationToken cancellationToken) =>
        ReadAsync(file, DocumentReadOptions.All, cancellationToken).GetAwaiter().GetResult();

    /// <inheritdoc />
    protected override async Task<ExtractedDocument> ReadAsync(string file, DocumentReadOptions options, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(options);
        var (total, title) = Pdf(() =>
        {
            using var stream = OpenFile(file);
            using var head = PdfDocument.Open(stream);
            return (head.NumberOfPages, head.Information.Title ?? string.Empty);
        });
        if (total == 0)
        {
            return new ExtractedDocument(title, [], 0, 0);
        }

        var range = options.Pages ?? new DocumentPageRange(1, total);
        if (range.From > total)
        {
            throw new InvalidDataException($"The PDF has {total} pages; page {range.From} is not in it.");
        }

        var to = Math.Min(range.To, total);
        var count = to - range.From + 1;
        var sections = new List<ExtractedSection>();
        var empty = 0;
        var unread = 0;
        var recognized = 0;
        var reused = 0;
        var unsupported = 0;
        var characters = 0L;
        var stopped = false;
        var halted = string.Empty;
        FileStream? stream = null;
        PdfDocument? pdf = null;
        try
        {
            for (var number = range.From; number <= to; number++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                options.Progress?.Report(new DocumentProgress(options.Name, DocumentStage.Extracting, number - range.From, count));
                if (characters >= DocumentLimits.CharactersMax)
                {
                    stopped = true;
                    break;
                }

                if (options.Memory?.Find(number) is { } kept)
                {
                    // Read before (its text layer, or an engine's reading): taken as it is, the PDF page is not even parsed.
                    reused++;
                    if (kept.Text.Length == 0)
                    {
                        empty++;
                        continue;
                    }

                    characters += kept.Text.Length;
                    sections.Add(new ExtractedSection(number, string.Empty, kept.Text));
                    continue;
                }

                if (pdf is null || (number - range.From) % DocumentLimits.PdfReopenPages == 0)
                {
                    // What PdfPig parsed so far is let go with the document; the next pages start from a fresh one.
                    pdf?.Dispose();
                    stream?.Dispose();
                    pdf = null;
                    stream = OpenFile(file);
                    var opened = stream;
                    pdf = Pdf(() => PdfDocument.Open(opened));
                }

                var current = pdf;
                var (text, images, cannot) = Pdf(() =>
                {
                    var page = current.GetPage(number);
                    var read = PageText(page);
                    var (parts, unsupportedImages) = read.Length == 0 && options.Recognizer is not null ? PdfPageImages.Read(page) : ([], false);
                    return (read, parts, unsupportedImages);
                });
                if (text.Length > 0 && options.Memory is { } layer)
                {
                    await layer.KeepAsync(number, text, PageTextSources.Layer, cancellationToken).ConfigureAwait(false);
                }

                if (text.Length == 0 && options.Recognizer is { } recognizer)
                {
                    if (halted.Length > 0 || images.Count == 0)
                    {
                        // No image that can be sent (a vector drawing, or an image format that cannot be handed on): the page stays unread.
                        unread++;
                        unsupported += halted.Length == 0 && cannot ? 1 : 0;
                    }
                    else
                    {
                        PageRecognition result;
                        try
                        {
                            result = await recognizer.RecognizeAsync(new PageImage(number, images), cancellationToken).ConfigureAwait(false);
                        }
                        catch (PageRecognitionStoppedException ex)
                        {
                            // The engine logged why; the pages after this one are read for their text layer only.
                            halted = ex.InnerException is Core.Providers.ProviderException provider ? provider.Code : "error";
                            result = PageRecognition.Failure;
                        }

                        unread += result.Failed ? 1 : 0;
                        if (!result.Failed && options.Memory is { } memory)
                        {
                            // Kept at once, also an empty page: what was paid for or waited for is never read again.
                            await memory.KeepAsync(number, result.Text, recognizer.Source, CancellationToken.None).ConfigureAwait(false);
                        }

                        if (!result.Failed && result.Text.Length > 0)
                        {
                            recognized++;
                            text = result.Text;
                        }
                    }
                }

                if (text.Length == 0)
                {
                    empty++;
                    continue;
                }

                characters += text.Length;
                sections.Add(new ExtractedSection(number, string.Empty, text));
            }
        }
        finally
        {
            pdf?.Dispose();
            stream?.Dispose();
            if (options.Memory is { } memory)
            {
                // A cancel or a failure keeps what was read up to it.
                await memory.FlushAsync(CancellationToken.None).ConfigureAwait(false);
            }
        }

        options.Progress?.Report(new DocumentProgress(options.Name, DocumentStage.Extracting, count, count));
        return new ExtractedDocument(title, sections, count, empty)
        {
            PagesRecognized = recognized,
            PagesUnread = unread,
            PagesReused = reused,
            PagesUnsupported = unsupported,
            StoppedAtLimit = stopped,
            RecognitionStopped = halted,
        };
    }

    /// <inheritdoc />
    protected override DocumentProbe Probe(string file, CancellationToken cancellationToken) => Pdf(() =>
    {
        using var stream = OpenFile(file);
        using var pdf = PdfDocument.Open(stream);
        var pages = pdf.NumberOfPages;
        var sample = Sample(pages, DocumentLimits.ProbePages);
        var without = 0;
        foreach (var number in sample)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (PageText(pdf.GetPage(number)).Length == 0)
            {
                without++;
            }
        }

        return new DocumentProbe(pages, pdf.Information.Title ?? string.Empty, sample.Count, without);
    });

    /// <summary>The page's text layer in reading order (empty when it has none).</summary>
    private static string PageText(Page page)
    {
        var text = ContentOrderTextExtractor.GetText(page);
        if (string.IsNullOrWhiteSpace(text))
        {
            text = page.Text;
        }

        return string.IsNullOrWhiteSpace(text) ? string.Empty : text;
    }

    /// <summary>A read-only stream of the file: PdfPig reads what it needs from it instead of taking the whole file into memory.</summary>
    private static FileStream OpenFile(string file) => new(file, FileMode.Open, FileAccess.Read, FileShare.Read, 64 * 1024, FileOptions.RandomAccess);

    /// <summary>Runs PdfPig code; its own exception types (a damaged or encrypted file) become one meaning for the library.</summary>
    private static T Pdf<T>(Func<T> read)
    {
        try
        {
            return read();
        }
        catch (Exception ex) when (ex is not OperationCanceledException and not InvalidDataException)
        {
            throw new InvalidDataException("The PDF could not be read: " + ex.Message, ex);
        }
    }
}
