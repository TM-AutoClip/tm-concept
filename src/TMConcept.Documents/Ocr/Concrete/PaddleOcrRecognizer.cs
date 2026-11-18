using Microsoft.Extensions.Logging;
using TMConcept.Core.Documents;

namespace TMConcept.Documents.Ocr;

/// <summary>
/// The working local OCR (0.1.25): PP-OCR's line detector (<see cref="DbTextDetector"/>) and Latin recogniser (<see cref="CtcLineReader"/>)
/// on a page image decoded by <see cref="IPageImageDecoder"/>. The two networks are given (ONNX files through <see cref="LocalOcrEngine"/>, a
/// fake in the tests) and let go with the engine. Nothing leaves this computer; nothing is paid.
/// 0.1.27: the networks come page by page from an <see cref="IOcrNetworkSource"/> (the graphics card when it can be had, otherwise the
/// processor); when the card's networks fail on a page, the card is given up for this run and the same page is read again on the processor, so
/// the text is the same either way. The card's lease is released after every page, also on a failure or a cancel.
/// </summary>
public sealed class PaddleOcrRecognizer : OcrRecognizerBase, IDisposable
{
    private readonly IOcrNetworkSource _source;
    private readonly OcrDictionary _dictionary;
    private OcrPageNetworks? _page;

    /// <summary>The same two networks for every page.</summary>
    public PaddleOcrRecognizer(IPageImageDecoder decoder, IOcrNetwork detector, IOcrNetwork recognizer, OcrDictionary dictionary, ILogger logger)
        : this(decoder, new FixedOcrNetworkSource(detector, recognizer), dictionary, logger)
    {
    }

    /// <summary>The networks of each page from <paramref name="source"/>.</summary>
    public PaddleOcrRecognizer(IPageImageDecoder decoder, IOcrNetworkSource source, OcrDictionary dictionary, ILogger logger)
        : base(decoder, logger)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(dictionary);
        _source = source;
        _dictionary = dictionary;
    }

    /// <inheritdoc />
    public void Dispose() => _source.Dispose();

    /// <inheritdoc />
    protected override async Task<string> ReadAsync(PageImage page, CancellationToken cancellationToken)
    {
        var retried = false;
        while (true)
        {
            var networks = await _source.ForPageAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                _page = networks;
                return await base.ReadAsync(page, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex) when (networks.Lease is not null && !retried && ex is not (OperationCanceledException or InvalidDataException or OutOfMemoryException))
            {
                // The graphics card failed on this page: given up for this run, the page is read again on the processor.
                retried = true;
                _source.Failed(networks, ex);
            }
            finally
            {
                _page = null;
                if (networks.Lease is { } lease)
                {
                    await lease.DisposeAsync().ConfigureAwait(false);
                }
            }
        }
    }

    /// <inheritdoc />
    protected override IReadOnlyList<OcrLineBox> Detect(DecodedImage image)
    {
        ArgumentNullException.ThrowIfNull(image);
        var (input, scaleX, scaleY) = DbTextDetector.Prepare(image, LocalOcrModels.DetectorSideMax);
        return DbTextDetector.Boxes(Current.Detector.Run(input), scaleX, scaleY, image.Width, image.Height);
    }

    /// <inheritdoc />
    protected override string Recognize(DecodedImage image, OcrLineBox box) =>
        CtcLineReader.Decode(Current.Recognizer.Run(CtcLineReader.Prepare(image, box)), _dictionary);

    private OcrPageNetworks Current => _page ?? throw new InvalidOperationException("The OCR networks are used outside a page.");
}
