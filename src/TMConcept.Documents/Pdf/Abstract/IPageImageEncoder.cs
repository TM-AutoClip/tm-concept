using TMConcept.Core.Chat;

namespace TMConcept.Documents.Pdf;

/// <summary>
/// Turns a drawn PDF page into a picture the OCR or a vision model takes (0.1.27): a JPEG of at most
/// <see cref="Core.Documents.DocumentLimits.PageImageBytesMax"/> bytes. This project has no image codec; the app implements it with Skia, which
/// it already ships with Avalonia.
/// </summary>
public interface IPageImageEncoder
{
    /// <summary>The page as one image; <see cref="InvalidDataException"/> when it cannot be encoded small enough.</summary>
    ImagePart Encode(RenderedPage page);
}
