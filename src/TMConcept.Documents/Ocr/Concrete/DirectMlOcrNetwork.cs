namespace TMConcept.Documents.Ocr;

/// <summary>
/// A network of the local OCR on the graphics card (0.1.27, Windows, DirectML; <see cref="DirectMlRuntime"/>). DirectML prepares its work for
/// each new input size, so with <c>widthStep</c> the input's width (its last dimension) is padded to a multiple of it, the way PaddleOCR pads a
/// line (the recogniser then sees at most ten widths instead of every one). Disposing it frees its graphics memory.
/// </summary>
internal sealed class DirectMlOcrNetwork : IOcrNetwork
{
    private readonly DirectMlRuntime _runtime;
    private readonly string _input;
    private readonly string _output;
    private readonly int _widthStep;
    private IntPtr _session;

    /// <summary>Opens <paramref name="file"/> on the graphics card (<see cref="InvalidOperationException"/> when it cannot be).</summary>
    public DirectMlOcrNetwork(DirectMlRuntime runtime, string file, int widthStep)
    {
        _runtime = runtime;
        _widthStep = widthStep;
        (_session, _input, _output) = runtime.Open(file);
    }

    /// <inheritdoc />
    public OcrTensor Run(OcrTensor input)
    {
        ArgumentNullException.ThrowIfNull(input);
        ObjectDisposedException.ThrowIf(_session == IntPtr.Zero, this);
        var padded = _widthStep > 0 ? Pad(input, _widthStep) : input;
        var (data, shape) = _runtime.Run(_session, _input, _output, padded.Data, padded.Shape);
        return new OcrTensor(data, shape);
    }

    /// <summary><paramref name="input"/> with its last dimension padded with zeros to a multiple of <paramref name="step"/>.</summary>
    public static OcrTensor Pad(OcrTensor input, int step)
    {
        ArgumentNullException.ThrowIfNull(input);
        var width = input.Shape[^1];
        var wide = (width + step - 1) / step * step;
        if (wide == width)
        {
            return input;
        }

        var rows = input.Data.Length / width;
        var data = new float[rows * wide];
        for (var row = 0; row < rows; row++)
        {
            Array.Copy(input.Data, row * width, data, row * wide, width);
        }

        var shape = input.Shape.ToArray();
        shape[^1] = wide;
        return new OcrTensor(data, shape);
    }

    /// <inheritdoc />
    public void Dispose()
    {
        var session = Interlocked.Exchange(ref _session, IntPtr.Zero);
        if (session != IntPtr.Zero)
        {
            _runtime.Close(session);
        }
    }
}
