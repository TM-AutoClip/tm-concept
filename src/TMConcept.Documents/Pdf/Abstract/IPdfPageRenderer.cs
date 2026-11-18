namespace TMConcept.Documents.Pdf;

/// <summary>
/// Draws PDF pages as pictures (0.1.27) for the OCR or a vision model: any page a PDF viewer can show, also the scans PdfPig cannot hand on
/// (JPEG 2000, JBIG2, CCITT fax, 1-bit masks, tiles), text drawn as shapes and turned pages. The app uses PDFium, a component downloaded after
/// a confirmation (<see cref="PdfiumRenderer"/>); the tests give a fake. PdfPig still reads the text layer.
/// </summary>
public interface IPdfPageRenderer
{
    /// <summary>Whether pages can be drawn now (the component is installed and supported here).</summary>
    bool Ready { get; }

    /// <summary>
    /// Opens <paramref name="file"/> for drawing (keep it open while its pages are drawn, dispose it after). <see cref="InvalidDataException"/>
    /// when it cannot be opened; <see cref="InvalidOperationException"/> when the renderer is not ready.
    /// </summary>
    IPdfRenderDocument Open(string file);
}
