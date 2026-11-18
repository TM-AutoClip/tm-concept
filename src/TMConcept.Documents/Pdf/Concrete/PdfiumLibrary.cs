using System.Runtime.InteropServices;

namespace TMConcept.Documents.Pdf;

/// <summary>
/// The few PDFium calls the app needs (0.1.27), bound by hand: the library is loaded by its full path from the tools folder
/// (<see cref="NativeLibrary.Load(string)"/>) and each export is called through a function pointer, so there is no name lookup, no
/// reflection, nothing the trimmer could drop, and the single-file and folder builds behave the same. PDFium is not thread-safe: every call
/// goes through <see cref="Gate"/>; the library is set up once per process and never torn down while the app runs.
/// </summary>
internal sealed unsafe class PdfiumLibrary
{
    /// <summary>BGRx, four bytes a pixel (<c>FPDFBitmap_BGRx</c>).</summary>
    private const int BitmapBgrx = 3;

    /// <summary>Draw annotations as printed (<c>FPDF_ANNOT | FPDF_PRINTING</c>).</summary>
    private const int RenderFlags = 0x01 | 0x800;

    private static readonly Dictionary<string, PdfiumLibrary> Libraries = new(StringComparer.Ordinal);

    private readonly delegate* unmanaged<byte*, byte*, IntPtr> _loadDocument;
    private readonly delegate* unmanaged<CULong> _lastError;
    private readonly delegate* unmanaged<IntPtr, int> _pageCount;
    private readonly delegate* unmanaged<IntPtr, int, IntPtr> _loadPage;
    private readonly delegate* unmanaged<IntPtr, float> _pageWidth;
    private readonly delegate* unmanaged<IntPtr, float> _pageHeight;
    private readonly delegate* unmanaged<int, int, int, void*, int, IntPtr> _createBitmap;
    private readonly delegate* unmanaged<IntPtr, int, int, int, int, CULong, int> _fillRect;
    private readonly delegate* unmanaged<IntPtr, IntPtr, int, int, int, int, int, int, void> _renderPage;
    private readonly delegate* unmanaged<IntPtr, void> _destroyBitmap;
    private readonly delegate* unmanaged<IntPtr, void> _closePage;
    private readonly delegate* unmanaged<IntPtr, void> _closeDocument;

    private PdfiumLibrary(IntPtr library)
    {
        _loadDocument = (delegate* unmanaged<byte*, byte*, IntPtr>)NativeLibrary.GetExport(library, "FPDF_LoadDocument");
        _lastError = (delegate* unmanaged<CULong>)NativeLibrary.GetExport(library, "FPDF_GetLastError");
        _pageCount = (delegate* unmanaged<IntPtr, int>)NativeLibrary.GetExport(library, "FPDF_GetPageCount");
        _loadPage = (delegate* unmanaged<IntPtr, int, IntPtr>)NativeLibrary.GetExport(library, "FPDF_LoadPage");
        _pageWidth = (delegate* unmanaged<IntPtr, float>)NativeLibrary.GetExport(library, "FPDF_GetPageWidthF");
        _pageHeight = (delegate* unmanaged<IntPtr, float>)NativeLibrary.GetExport(library, "FPDF_GetPageHeightF");
        _createBitmap = (delegate* unmanaged<int, int, int, void*, int, IntPtr>)NativeLibrary.GetExport(library, "FPDFBitmap_CreateEx");
        _fillRect = (delegate* unmanaged<IntPtr, int, int, int, int, CULong, int>)NativeLibrary.GetExport(library, "FPDFBitmap_FillRect");
        _renderPage = (delegate* unmanaged<IntPtr, IntPtr, int, int, int, int, int, int, void>)NativeLibrary.GetExport(library, "FPDF_RenderPageBitmap");
        _destroyBitmap = (delegate* unmanaged<IntPtr, void>)NativeLibrary.GetExport(library, "FPDFBitmap_Destroy");
        _closePage = (delegate* unmanaged<IntPtr, void>)NativeLibrary.GetExport(library, "FPDF_ClosePage");
        _closeDocument = (delegate* unmanaged<IntPtr, void>)NativeLibrary.GetExport(library, "FPDF_CloseDocument");
        var init = (delegate* unmanaged<void>)NativeLibrary.GetExport(library, "FPDF_InitLibrary");
        init();
    }

    /// <summary>The one lock of every PDFium call in the process.</summary>
    public static Lock Gate { get; } = new();

    /// <summary>
    /// The library at <paramref name="file"/>, loaded and set up once (<see cref="DllNotFoundException"/> or <see cref="BadImageFormatException"/>
    /// when it cannot be loaded, <see cref="EntryPointNotFoundException"/> when it lacks a call).
    /// </summary>
    public static PdfiumLibrary Load(string file)
    {
        lock (Gate)
        {
            if (!Libraries.TryGetValue(file, out var library))
            {
                library = new PdfiumLibrary(NativeLibrary.Load(file));
                Libraries[file] = library;
            }

            return library;
        }
    }

    /// <summary>Opens a PDF (the path as UTF-8, as PDFium wants it); <see cref="InvalidDataException"/> with PDFium's reason when it cannot.</summary>
    public IntPtr OpenDocument(string file)
    {
        var path = Marshal.StringToCoTaskMemUTF8(file);
        try
        {
            lock (Gate)
            {
                var document = _loadDocument((byte*)path, null);
                if (document == IntPtr.Zero)
                {
                    throw new InvalidDataException("PDFium could not open the PDF (" + Reason(_lastError().Value) + ").");
                }

                return document;
            }
        }
        finally
        {
            Marshal.FreeCoTaskMem(path);
        }
    }

    /// <summary>The page count of an open document.</summary>
    public int PageCount(IntPtr document)
    {
        lock (Gate)
        {
            return _pageCount(document);
        }
    }

    /// <summary>Closes an open document.</summary>
    public void CloseDocument(IntPtr document)
    {
        lock (Gate)
        {
            _closeDocument(document);
        }
    }

    /// <summary>
    /// Draws page <paramref name="index"/> (0 = the first) on white at <paramref name="dpi"/>, at most <paramref name="pixelsMax"/> pixels and
    /// <paramref name="sideMax"/> on a side; <see cref="InvalidDataException"/> when the page cannot be loaded.
    /// </summary>
    public RenderedPage Render(IntPtr document, int index, int dpi, long pixelsMax, int sideMax)
    {
        lock (Gate)
        {
            var page = _loadPage(document, index);
            if (page == IntPtr.Zero)
            {
                throw new InvalidDataException("PDFium could not load page " + (index + 1) + " (" + Reason(_lastError().Value) + ").");
            }

            try
            {
                var (width, height) = PdfiumRenderer.Size(_pageWidth(page), _pageHeight(page), dpi, pixelsMax, sideMax);
                var pixels = new byte[checked(width * height * 4)];
                fixed (byte* first = pixels)
                {
                    var bitmap = _createBitmap(width, height, BitmapBgrx, first, width * 4);
                    if (bitmap == IntPtr.Zero)
                    {
                        throw new InvalidDataException($"PDFium could not make a {width} × {height} picture of page {index + 1}.");
                    }

                    try
                    {
                        _ = _fillRect(bitmap, 0, 0, width, height, new CULong(0xFFFFFFFF));
                        _renderPage(bitmap, page, 0, 0, width, height, 0, RenderFlags);
                    }
                    finally
                    {
                        _destroyBitmap(bitmap);
                    }
                }

                return new RenderedPage(width, height, pixels);
            }
            finally
            {
                _closePage(page);
            }
        }
    }

    private static string Reason(nuint code) => code switch
    {
        2 => "the file cannot be opened",
        3 => "not a PDF, or damaged",
        4 => "it needs a password",
        5 => "its protection is not supported",
        6 => "the page cannot be read",
        _ => "error " + code,
    };
}
