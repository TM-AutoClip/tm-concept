using Microsoft.Extensions.Logging;

namespace TMConcept.Documents.Ocr;

/// <summary>
/// The working local OCR (0.1.25): PP-OCR's line detector (<see cref="DbTextDetector"/>) and Latin recogniser (<see cref="CtcLineReader"/>)
/// on a page image decoded by <see cref="IPageImageDecoder"/>. The two networks are given (ONNX files through <see cref="LocalOcrEngine"/>, a
/// fake in the tests) and let go with the engine. Nothing leaves this computer; nothing is paid.
/// </summary>
public sealed class PaddleOcrRecognizer(IPageImageDecoder decoder, IOcrNetwork detector, IOcrNetwork recognizer, OcrDictionary dictionary, ILogger logger)
    : OcrRecognizerBase(decoder, logger), IDisposable
{
    /// <inheritdoc />
    public void Dispose()
    {
        detector.Dispose();
        recognizer.Dispose();
    }

    /// <inheritdoc />
    protected override IReadOnlyList<OcrLineBox> Detect(DecodedImage image)
    {
        ArgumentNullException.ThrowIfNull(image);
        var (input, scaleX, scaleY) = DbTextDetector.Prepare(image, LocalOcrModels.DetectorSideMax);
        return DbTextDetector.Boxes(detector.Run(input), scaleX, scaleY, image.Width, image.Height);
    }

    /// <inheritdoc />
    protected override string Recognize(DecodedImage image, OcrLineBox box) =>
        CtcLineReader.Decode(recognizer.Run(CtcLineReader.Prepare(image, box)), dictionary);
}
