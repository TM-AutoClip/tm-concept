using TMConcept.Core.Chat;

namespace TMConcept.Documents.Ocr;

/// <summary>
/// Turns a scanned page's image (JPEG or PNG as stored in the PDF) into RGB pixels for a local OCR engine (0.1.24, skeleton). This project
/// has no image codec; the app implements it with the image library it already ships (Skia, through Avalonia), so no new package is needed.
/// </summary>
public interface IPageImageDecoder
{
    /// <summary>The pixels of <paramref name="image"/>; <see cref="InvalidDataException"/> when it cannot be decoded.</summary>
    DecodedImage Decode(ImagePart image);
}
