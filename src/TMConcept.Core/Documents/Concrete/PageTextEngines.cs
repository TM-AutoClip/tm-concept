using TMConcept.Core.Models;

namespace TMConcept.Core.Documents;

/// <summary>
/// The engines that read the text of a scanned page (0.1.25), as the "Text from scanned pages" step lists them: the app's local OCR (a catalog
/// model of the <c>local</c> provider that reads text only; offered only on that step), a local vision model of the app's Ollama or a server
/// of the user, or an online vision model. Each is chosen apart from the model that analyses the documents or writes the concepts, so any mix
/// works. Also the rough speed of the local OCR on this computer's processor (the bundled ONNX Runtime runs on the processor only).
/// </summary>
public static class PageTextEngines
{
    /// <summary>The catalog id of the local OCR engine (provider <see cref="ModelProviders.Local"/>).</summary>
    public const string LocalOcrId = "pp-ocr-latin";

    /// <summary>The component of the local OCR engine's two model files.</summary>
    public const string LocalOcrComponent = "ocr.pp-ocr-latin";

    /// <summary>The component that draws PDF pages as pictures for the OCR or a vision model (PDFium, 0.1.27).</summary>
    public const string PdfRendererComponent = "pdf.pdfium";

    /// <summary>The component that runs the local OCR on the graphics card (ONNX Runtime with DirectML, Windows, 0.1.27).</summary>
    public const string OcrGpuComponent = "ocr.directml";

    /// <summary>Seconds one dense scanned page takes on ONE processor thread (detection of a page of about 1,600 pixels and some 40 lines).</summary>
    public const double OcrSecondsPerPageOneThread = 6;

    /// <summary>Whether <paramref name="model"/> is the app's local OCR engine (reads text only: never offered to describe an image).</summary>
    public static bool IsLocalOcr(CatalogModel? model) => model is not null && model.Provider == ModelProviders.Local && model.Id == LocalOcrId;

    /// <summary>Processor threads the local OCR uses: a quarter of the processor's, 1 to 4, so the computer stays usable during a long book.</summary>
    public static int OcrThreads(int processors) => Math.Clamp(processors / 4, 1, 4);

    /// <summary>
    /// The rough time of reading <paramref name="pages"/> scanned pages with the local OCR on a processor of <paramref name="processors"/>
    /// threads: from half (a light page) to twice (a dense two-column page) the usual seconds a page; free.
    /// </summary>
    public static WorkEstimate OcrEstimate(int pages, int processors)
    {
        if (pages <= 0)
        {
            return WorkEstimate.None;
        }

        var perPage = OcrSecondsPerPageOneThread / Math.Pow(OcrThreads(processors), 0.8);
        return new WorkEstimate(pages, Costs.CostEstimate.Zero, TimeSpan.FromSeconds(Math.Ceiling(pages * perPage * 0.5)),
            TimeSpan.FromSeconds(Math.Ceiling(pages * perPage * 2)), true);
    }
}
