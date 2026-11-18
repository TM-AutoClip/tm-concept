namespace TMConcept.Documents.Ocr;

/// <summary>
/// Scaling of page image pixels for the OCR networks: the mean colour of the source area a target pixel covers (a box filter, so thin strokes
/// survive a smaller image), or the nearest pixel when the area is smaller than one pixel (a larger image).
/// </summary>
public static class OcrPixels
{
    /// <summary>
    /// The mean red, green and blue (0–255) of the area from (<paramref name="left"/>, <paramref name="top"/>) to (<paramref name="right"/>,
    /// <paramref name="bottom"/>) of <paramref name="image"/> (pixels, right and bottom excluded, clamped to the image).
    /// </summary>
    public static (float Red, float Green, float Blue) Mean(DecodedImage image, double left, double top, double right, double bottom)
    {
        ArgumentNullException.ThrowIfNull(image);
        var x0 = Math.Clamp((int)Math.Floor(left), 0, image.Width - 1);
        var y0 = Math.Clamp((int)Math.Floor(top), 0, image.Height - 1);
        var x1 = Math.Clamp((int)Math.Ceiling(right), x0 + 1, image.Width);
        var y1 = Math.Clamp((int)Math.Ceiling(bottom), y0 + 1, image.Height);
        long red = 0, green = 0, blue = 0;
        for (var y = y0; y < y1; y++)
        {
            var at = ((y * image.Width) + x0) * 3;
            for (var x = x0; x < x1; x++, at += 3)
            {
                red += image.Rgb[at];
                green += image.Rgb[at + 1];
                blue += image.Rgb[at + 2];
            }
        }

        float count = (x1 - x0) * (y1 - y0);
        return (red / count, green / count, blue / count);
    }
}
