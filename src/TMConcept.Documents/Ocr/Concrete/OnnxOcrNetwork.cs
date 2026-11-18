using Microsoft.ML.OnnxRuntime;

namespace TMConcept.Documents.Ocr;

/// <summary>
/// A network of the local OCR run in this process by ONNX Runtime on the processor (the bundled package has no graphics card backend):
/// <see cref="Core.Documents.PageTextEngines.OcrThreads"/> threads that do not spin while they wait, so a long book leaves the computer usable.
/// The model's first input and first output are used (PaddleOCR's exports name them <c>x</c> and <c>fetch_name_0</c>).
/// </summary>
public sealed class OnnxOcrNetwork : IOcrNetwork
{
    private readonly InferenceSession _session;
    private readonly string _input;
    private readonly string _output;

    /// <summary>Loads <paramref name="file"/> (<see cref="InvalidDataException"/> when ONNX Runtime cannot load it).</summary>
    public OnnxOcrNetwork(string file)
    {
        using var options = new SessionOptions
        {
            IntraOpNumThreads = Core.Documents.PageTextEngines.OcrThreads(Environment.ProcessorCount),
            InterOpNumThreads = 1,
            ExecutionMode = ExecutionMode.ORT_SEQUENTIAL,
            GraphOptimizationLevel = GraphOptimizationLevel.ORT_ENABLE_ALL,
        };
        options.AddSessionConfigEntry("session.intra_op.allow_spinning", "0");
        try
        {
            _session = new InferenceSession(file, options);
        }
        catch (OnnxRuntimeException ex)
        {
            throw new InvalidDataException("The OCR model " + Path.GetFileName(file) + " could not be loaded: " + ex.Message, ex);
        }

        _input = _session.InputNames[0];
        _output = _session.OutputNames[0];
    }

    /// <inheritdoc />
    public OcrTensor Run(OcrTensor input)
    {
        ArgumentNullException.ThrowIfNull(input);
        using var value = OrtValue.CreateTensorValueFromMemory(input.Data, input.Shape.Select(s => (long)s).ToArray());
        using var run = new RunOptions();
        using var outputs = _session.Run(run, [_input], [value], [_output]);
        var output = outputs[0];
        var shape = output.GetTensorTypeAndShape().Shape.Select(s => (int)s).ToArray();
        return new OcrTensor(output.GetTensorDataAsSpan<float>().ToArray(), shape);
    }

    /// <inheritdoc />
    public void Dispose() => _session.Dispose();
}
