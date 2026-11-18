namespace TMConcept.Documents.Pdf;

/// <summary>A PDF opened by PDFium (0.1.27); every call goes through the library's one lock.</summary>
internal sealed class PdfiumDocument(PdfiumLibrary library, IntPtr handle) : IPdfRenderDocument
{
    private IntPtr _handle = handle;

    /// <inheritdoc />
    public int Pages => library.PageCount(Handle);

    /// <inheritdoc />
    public RenderedPage Render(int page, int dpi, long pixelsMax)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(page, 1);
        return library.Render(Handle, page - 1, dpi, pixelsMax, PdfiumRenderer.SideMax);
    }

    /// <inheritdoc />
    public void Dispose()
    {
        var handle = Interlocked.Exchange(ref _handle, IntPtr.Zero);
        if (handle != IntPtr.Zero)
        {
            library.CloseDocument(handle);
        }
    }

    private IntPtr Handle => _handle != IntPtr.Zero ? _handle : throw new ObjectDisposedException(nameof(PdfiumDocument));
}
