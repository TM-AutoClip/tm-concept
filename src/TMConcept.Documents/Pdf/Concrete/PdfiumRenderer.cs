namespace TMConcept.Documents.Pdf;

/// <summary>
/// The app's PDF page renderer (0.1.27): PDFium from the tools folder (<see cref="PdfiumStore"/>), loaded the first time a page is drawn.
/// Ready only when the component is installed; a library that cannot be loaded is an <see cref="InvalidOperationException"/> with the reason
/// (the reading then goes on with what PdfPig can hand on).
/// </summary>
public sealed class PdfiumRenderer(PdfiumStore store) : IPdfPageRenderer
{
    /// <summary>The longest side of a drawn page.</summary>
    public const int SideMax = 30_000;

    /// <inheritdoc />
    public bool Ready => store.Ready;

    /// <inheritdoc />
    public IPdfRenderDocument Open(string file)
    {
        ArgumentException.ThrowIfNullOrEmpty(file);
        if (!store.Ready)
        {
            throw new InvalidOperationException("PDFium is not installed.");
        }

        PdfiumLibrary library;
        try
        {
            library = PdfiumLibrary.Load(store.LibraryFile);
        }
        catch (Exception ex) when (ex is DllNotFoundException or BadImageFormatException or EntryPointNotFoundException)
        {
            throw new InvalidOperationException("PDFium could not be loaded: " + ex.Message, ex);
        }

        return new PdfiumDocument(library, library.OpenDocument(file));
    }

    /// <summary>
    /// The size in pixels of a page of <paramref name="widthPoints"/> × <paramref name="heightPoints"/> points drawn at <paramref name="dpi"/>,
    /// made smaller (the same shape) when it would exceed <paramref name="pixelsMax"/> pixels or <paramref name="sideMax"/> on a side; at least
    /// one pixel a side. <see cref="InvalidDataException"/> for a page without a size.
    /// </summary>
    public static (int Width, int Height) Size(double widthPoints, double heightPoints, int dpi, long pixelsMax, int sideMax)
    {
        if (!(widthPoints > 0) || !(heightPoints > 0) || double.IsInfinity(widthPoints) || double.IsInfinity(heightPoints))
        {
            throw new InvalidDataException($"The page has no usable size ({widthPoints} × {heightPoints} points).");
        }

        var scale = dpi / 72.0;
        var width = widthPoints * scale;
        var height = heightPoints * scale;
        if (width * height > pixelsMax)
        {
            var factor = Math.Sqrt(pixelsMax / (width * height));
            width *= factor;
            height *= factor;
        }

        var side = Math.Max(width, height);
        if (side > sideMax)
        {
            width *= sideMax / side;
            height *= sideMax / side;
        }

        return (Math.Max(1, (int)Math.Floor(width)), Math.Max(1, (int)Math.Floor(height)));
    }
}
