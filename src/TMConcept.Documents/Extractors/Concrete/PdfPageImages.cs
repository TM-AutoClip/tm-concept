using TMConcept.Core.Chat;
using TMConcept.Core.Documents;
using UglyToad.PdfPig.Content;

namespace TMConcept.Documents.Extractors;

/// <summary>
/// The images of a scanned PDF page that a text-extraction model can read (0.1.24): the largest ones first, at most
/// <see cref="DocumentLimits.PageImagesMax"/>; masks and images smaller than <see cref="DocumentLimits.PageImagePixelsMin"/> pixels on a side
/// (icons, rules) are left out. A JPEG is handed on as it is stored (no decoding); any other image PdfPig can turn into a PNG (Flate and
/// uncompressed images) goes as a PNG. JPEG 2000, JBIG2 and CCITT fax images cannot be handed on, and an image larger than
/// <see cref="DocumentLimits.PageImageBytesMax"/> is not sent: such a page stays unread.
/// </summary>
public static class PdfPageImages
{
    /// <summary>The images of <paramref name="page"/> that can be sent (empty = none).</summary>
    public static IReadOnlyList<ImagePart> Of(Page page)
    {
        ArgumentNullException.ThrowIfNull(page);
        var parts = new List<ImagePart>();
        var images = page.GetImages()
            .Where(i => !i.IsImageMask && i.WidthInSamples >= DocumentLimits.PageImagePixelsMin && i.HeightInSamples >= DocumentLimits.PageImagePixelsMin)
            .OrderByDescending(i => (long)i.WidthInSamples * i.HeightInSamples);
        foreach (var image in images)
        {
            if (parts.Count >= DocumentLimits.PageImagesMax)
            {
                break;
            }

            if (Part(image) is { } part && part.Data.Length <= DocumentLimits.PageImageBytesMax)
            {
                parts.Add(part);
            }
        }

        return parts;
    }

    /// <summary>Whether <paramref name="bytes"/> start like a JPEG file.</summary>
    public static bool IsJpeg(ReadOnlySpan<byte> bytes) => bytes.Length > 3 && bytes[0] == 0xFF && bytes[1] == 0xD8 && bytes[2] == 0xFF;

    private static ImagePart? Part(IPdfImage image)
    {
        var raw = image.RawMemory;
        if (IsJpeg(raw.Span))
        {
            return new ImagePart(raw.ToArray(), "image/jpeg");
        }

        return image.TryGetPng(out var png) && png is { Length: > 0 } ? new ImagePart(png, "image/png") : null;
    }
}
