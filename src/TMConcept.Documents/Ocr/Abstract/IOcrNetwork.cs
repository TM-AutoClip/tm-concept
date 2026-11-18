namespace TMConcept.Documents.Ocr;

/// <summary>
/// One network of the local OCR (0.1.25): the text line detector or the line recogniser, one input and one output. The app runs the ONNX
/// files with ONNX Runtime (<see cref="OnnxOcrNetwork"/>); the tests give a fake that answers what they need, without a model file.
/// </summary>
public interface IOcrNetwork : IDisposable
{
    /// <summary>The network's output for <paramref name="input"/>.</summary>
    OcrTensor Run(OcrTensor input);
}
