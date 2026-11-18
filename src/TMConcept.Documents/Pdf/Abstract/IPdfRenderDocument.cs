namespace TMConcept.Documents.Pdf;

/// <summary>A PDF opened by an <see cref="IPdfPageRenderer"/> (0.1.27); disposing it closes the file.</summary>
public interface IPdfRenderDocument : IDisposable
{
    /// <summary>Its page count.</summary>
    int Pages { get; }

    /// <summary>
    /// Page <paramref name="page"/> (1 = the first) drawn on white at <paramref name="dpi"/>, made smaller when it would have more than
    /// <paramref name="pixelsMax"/> pixels; the page's own rotation is applied. <see cref="InvalidDataException"/> when the page cannot be drawn.
    /// </summary>
    RenderedPage Render(int page, int dpi, long pixelsMax);
}
