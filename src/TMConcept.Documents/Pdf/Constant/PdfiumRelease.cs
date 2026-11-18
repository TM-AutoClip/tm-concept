using TMConcept.Core.Tools;

namespace TMConcept.Documents.Pdf;

/// <summary>
/// The pinned PDFium build (0.1.27): bblanchon/pdfium-binaries release <c>chromium/8138</c> (PDFium 159.0.8138.0, published 4 Nov 2026;
/// checked on 18 Nov 2026). Built without its JavaScript engine and without XFA, one self-contained library: the Windows DLL needs only system
/// libraries (no Visual C++ runtime), the Linux one glibc 2.17 at most. PDFium is BSD-3-Clause (The PDFium Authors); the build scripts are MIT;
/// the parts compiled in (FreeType, libjpeg-turbo, libpng, zlib, OpenJPEG, Little CMS, HarfBuzz, ICU, Abseil and others) are under permissive
/// licences whose texts come in the archive's <c>licenses</c> folder and are kept next to the library.
/// </summary>
public static class PdfiumRelease
{
    /// <summary>The version (its folder under <c>&lt;tools&gt;/pdfium/</c>).</summary>
    public const string Version = "159.0.8138";

    /// <summary>The display name.</summary>
    public const string Name = "PDFium (PDF page renderer)";

    /// <summary>The licence of the library.</summary>
    public const string License = "BSD-3-Clause";

    private const string Release = "https://github.com/bblanchon/pdfium-binaries/releases/download/chromium%2F8138/";

    /// <summary>The archives per runtime id; <see cref="ToolArchive.ExecutablePath"/> is the library inside the archive.</summary>
    public static readonly IReadOnlyList<ToolArchive> Archives =
    [
        new(ToolKind.PdfRenderer, Version, "win-x64", new Uri(Release + "pdfium-win-x64.tgz"),
            "8d48973e3eb472dda0f5f2435e595ac043f9f2219dd71883392f549445afa8a5", string.Empty, "bin/pdfium.dll", 4_031_929), // SHA-256 of the public file
        new(ToolKind.PdfRenderer, Version, "linux-x64", new Uri(Release + "pdfium-linux-x64.tgz"),
            "d6b1e28950a0392590d79bbdbb7eb19686872b688f8a36f170acb19e41473113", string.Empty, "lib/libpdfium.so", 3_945_444), // SHA-256 of the public file
        new(ToolKind.PdfRenderer, Version, "linux-arm64", new Uri(Release + "pdfium-linux-arm64.tgz"),
            "c70b1ce4d13257f741af19e0aeee575890875995eb81370a65be942e601a1862", string.Empty, "lib/libpdfium.so", 3_811_232), // SHA-256 of the public file
    ];
}
