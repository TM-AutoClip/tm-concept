using SkiaSharp;
using TMConcept.Core.Chat;
using TMConcept.Core.Documents;
using TMConcept.Documents.Pdf;

namespace TMConcept.Desktop.Documents;

/// <summary>
/// Turns a PDF page drawn by PDFium into a JPEG for the local OCR or a vision model (0.1.27) with SkiaSharp, the image library the app
/// already ships with Avalonia. The quality starts at <see cref="Quality"/>; a page whose JPEG is larger than
/// <see cref="DocumentLimits.PageImageBytesMax"/> is encoded again at a lower quality, then smaller, until it fits.
/// </summary>
public sealed class SkiaPageImageEncoder : IPageImageEncoder
{
    /// <summary>The first JPEG quality tried.</summary>
    public const int Quality = 90;

    /// <summary>The lowest quality before the page is made smaller.</summary>
    private const int QualityMin = 60;

    /// <inheritdoc />
    public ImagePart Encode(RenderedPage page)
    {
        ArgumentNullException.ThrowIfNull(page);
        if (page.Width <= 0 || page.Height <= 0 || page.Bgrx.Length < (long)page.Width * page.Height * 4)
        {
            throw new InvalidDataException($"The drawn page ({page.Width} × {page.Height}) has too few pixels.");
        }

        var info = new SKImageInfo(page.Width, page.Height, SKColorType.Bgra8888, SKAlphaType.Opaque);
        using var bitmap = new SKBitmap();
        var handle = System.Runtime.InteropServices.GCHandle.Alloc(page.Bgrx, System.Runtime.InteropServices.GCHandleType.Pinned);
        try
        {
            if (!bitmap.InstallPixels(info, handle.AddrOfPinnedObject(), page.Width * 4))
            {
                throw new InvalidDataException("The drawn page could not be handed to the image library.");
            }

            var image = bitmap;
            SKBitmap? smaller = null;
            try
            {
                while (true)
                {
                    for (var quality = Quality; quality >= QualityMin; quality -= 15)
                    {
                        using var data = image.Encode(SKEncodedImageFormat.Jpeg, quality)
                            ?? throw new InvalidDataException("The drawn page could not be encoded as a JPEG.");
                        if (data.Size <= DocumentLimits.PageImageBytesMax)
                        {
                            return new ImagePart(data.ToArray(), "image/jpeg");
                        }
                    }

                    if (image.Width < 64 || image.Height < 64)
                    {
                        throw new InvalidDataException("The drawn page does not fit in an image of the allowed size.");
                    }

                    var next = image.Resize(new SKImageInfo(image.Width * 3 / 4, image.Height * 3 / 4, SKColorType.Bgra8888, SKAlphaType.Opaque),
                        new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.None)) ?? throw new InvalidDataException("The drawn page could not be made smaller.");
                    smaller?.Dispose();
                    smaller = next;
                    image = next;
                }
            }
            finally
            {
                smaller?.Dispose();
            }
        }
        finally
        {
            handle.Free();
        }
    }
}
