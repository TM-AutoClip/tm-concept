using SkiaSharp;
using TMConcept.Core.Chat;
using TMConcept.Documents.Ocr;

namespace TMConcept.Desktop.Documents;

/// <summary>
/// Decodes a scanned page's image (the JPEG or PNG the PDF reader hands on) for the local OCR (0.1.25) with SkiaSharp, the image library the
/// app already ships with Avalonia (Windows and Linux; no new native library). Memory stays bounded: an image larger than
/// <see cref="PixelsMax"/> pixels is decoded smaller (a JPEG by the codec itself, anything else scaled after decoding), and one whose sides
/// are absurd is refused before decoding. A transparent image is laid on white. <see cref="InvalidDataException"/> when Skia cannot decode it.
/// </summary>
public sealed class SkiaPageImageDecoder : IPageImageDecoder
{
    /// <summary>Most pixels of a decoded page (16 million: a 300 dpi A3 scan, about 48 MB as RGB).</summary>
    public const long PixelsMax = 16_000_000;

    /// <summary>Longest side an image may declare.</summary>
    public const int SideMax = 30_000;

    /// <inheritdoc />
    public DecodedImage Decode(ImagePart image)
    {
        ArgumentNullException.ThrowIfNull(image);
        using var data = SKData.CreateCopy(image.Data);
        using var codec = SKCodec.Create(data) ?? throw new InvalidDataException("The image (" + image.MimeType + ") is not one this app can decode.");
        var info = codec.Info;
        if (info.Width <= 0 || info.Height <= 0 || info.Width > SideMax || info.Height > SideMax)
        {
            throw new InvalidDataException($"The image is {info.Width} × {info.Height} pixels: too large to read.");
        }

        var pixels = (long)info.Width * info.Height;
        var size = pixels > PixelsMax ? codec.GetScaledDimensions((float)Math.Sqrt(PixelsMax / (double)pixels)) : info.Size;
        var target = new SKImageInfo(size.Width, size.Height, SKColorType.Rgba8888, SKAlphaType.Unpremul);
        var bitmap = new SKBitmap(target);
        try
        {
            var result = codec.GetPixels(target, bitmap.GetPixels());
            if (result is not (SKCodecResult.Success or SKCodecResult.IncompleteInput))
            {
                throw new InvalidDataException("The image could not be decoded (" + result + ").");
            }

            if ((long)bitmap.Width * bitmap.Height > PixelsMax)
            {
                var factor = Math.Sqrt(PixelsMax / ((double)bitmap.Width * bitmap.Height));
                var smaller = bitmap.Resize(new SKImageInfo(Math.Max(1, (int)(bitmap.Width * factor)), Math.Max(1, (int)(bitmap.Height * factor)),
                    SKColorType.Rgba8888, SKAlphaType.Unpremul), new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.None))
                    ?? throw new InvalidDataException("The image could not be made smaller.");
                bitmap.Dispose();
                bitmap = smaller;
            }

            return Rgb(bitmap);
        }
        finally
        {
            bitmap.Dispose();
        }
    }

    private static DecodedImage Rgb(SKBitmap bitmap)
    {
        var source = bitmap.GetPixelSpan();
        var rgb = new byte[bitmap.Width * bitmap.Height * 3];
        for (var y = 0; y < bitmap.Height; y++)
        {
            var row = source.Slice(y * bitmap.RowBytes, bitmap.Width * 4);
            var at = y * bitmap.Width * 3;
            for (var x = 0; x < bitmap.Width; x++)
            {
                var alpha = row[(x * 4) + 3];
                for (var c = 0; c < 3; c++)
                {
                    // Transparent parts are paper: laid on white.
                    rgb[at + (x * 3) + c] = (byte)(((row[(x * 4) + c] * alpha) + (255 * (255 - alpha)) + 127) / 255);
                }
            }
        }

        return new DecodedImage(bitmap.Width, bitmap.Height, rgb);
    }
}
