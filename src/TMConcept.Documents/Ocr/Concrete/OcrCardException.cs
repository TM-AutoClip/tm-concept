namespace TMConcept.Documents.Ocr;

/// <summary>
/// The graphics card's side of the local OCR failed (0.1.27): the DirectML runtime could not be loaded, a network could not be opened on the
/// card or ONNX Runtime reported an error while it ran one. Only this failure gives the card up for the run and reads the page again on the
/// processor (<see cref="PaddleOcrRecognizer"/>); a failure of the app's own code around the networks fails the page as it would on the
/// processor.
/// </summary>
public sealed class OcrCardException : InvalidOperationException
{
    /// <summary>Creates it.</summary>
    public OcrCardException()
        : this("Text recognition on the graphics card failed.")
    {
    }

    /// <summary>Creates it with a message.</summary>
    public OcrCardException(string message)
        : base(message)
    {
    }

    /// <summary>Creates it with a message and the cause.</summary>
    public OcrCardException(string message, Exception inner)
        : base(message, inner)
    {
    }
}
