using Microsoft.ML.OnnxRuntime;
using TMConcept.Core.Text;

namespace TMConcept.Documents.Embeddings;

/// <summary>
/// A sentence encoder in this process (ONNX Runtime, processor only, one thread): the tokenizer's ids, the inputs the model declares
/// (<c>input_ids</c>, <c>attention_mask</c>, <c>token_type_ids</c>), mean pooling over the tokens of the first output and L2 normalization.
/// One text per run, so there is no padding to leave out of the mean.
/// </summary>
public sealed class OnnxTextEncoder : IDisposable
{
    private readonly InferenceSession _session;
    private readonly UnigramTokenizer _tokenizer;

    /// <summary>Loads <paramref name="modelFile"/> and <paramref name="tokenizerFile"/>; texts are cut at <paramref name="maxTokens"/>.</summary>
    public OnnxTextEncoder(string modelFile, string tokenizerFile, int maxTokens)
    {
        _tokenizer = UnigramTokenizer.Load(tokenizerFile, maxTokens);
        using var options = new SessionOptions
        {
            IntraOpNumThreads = 1,
            InterOpNumThreads = 1,
            ExecutionMode = ExecutionMode.ORT_SEQUENTIAL,
            GraphOptimizationLevel = GraphOptimizationLevel.ORT_ENABLE_ALL,
        };
        _session = new InferenceSession(modelFile, options);
    }

    /// <summary>The version of the ONNX Runtime library (for the log; also proves the native library loads).</summary>
    public static string RuntimeVersion => OrtEnv.Instance().GetVersionString();

    /// <summary>The normalized vector of <paramref name="text"/>.</summary>
    public float[] Encode(string text)
    {
        var ids = _tokenizer.Encode(text);
        long[] shape = [1, ids.Length];
        var inputs = new Dictionary<string, long[]>(StringComparer.Ordinal)
        {
            ["input_ids"] = ids,
            ["attention_mask"] = Enumerable.Repeat(1L, ids.Length).ToArray(),
            ["token_type_ids"] = new long[ids.Length],
        };
        var names = new List<string>();
        var values = new List<OrtValue>();
        try
        {
            foreach (var name in _session.InputNames)
            {
                if (!inputs.TryGetValue(name, out var data))
                {
                    throw new InvalidOperationException("The meaning model asks for an input this app does not know: " + name);
                }

                names.Add(name);
                values.Add(OrtValue.CreateTensorValueFromMemory(data, shape));
            }

            using var run = new RunOptions();
            using var outputs = _session.Run(run, names, values, [_session.OutputNames[0]]);
            var output = outputs[0];
            return Pool(output.GetTensorDataAsSpan<float>(), output.GetTensorTypeAndShape().Shape);
        }
        finally
        {
            foreach (var value in values)
            {
                value.Dispose();
            }
        }
    }

    /// <inheritdoc />
    public void Dispose() => _session.Dispose();

    /// <summary>Mean of the token vectors ([1, tokens, size]; [1, size] is taken as it is), then L2-normalized.</summary>
    public static float[] Pool(ReadOnlySpan<float> values, long[] shape)
    {
        ArgumentNullException.ThrowIfNull(shape);
        float[] vector;
        if (shape.Length == 3)
        {
            var tokens = (int)shape[1];
            var size = (int)shape[2];
            vector = new float[size];
            for (var t = 0; t < tokens; t++)
            {
                for (var i = 0; i < size; i++)
                {
                    vector[i] += values[(t * size) + i];
                }
            }

            for (var i = 0; i < size; i++)
            {
                vector[i] /= Math.Max(1, tokens);
            }
        }
        else if (shape.Length == 2)
        {
            vector = values.ToArray();
        }
        else
        {
            throw new InvalidOperationException("The meaning model gave an output of an unexpected shape.");
        }

        var norm = Math.Sqrt(vector.Sum(v => (double)v * v));
        if (norm > 0)
        {
            for (var i = 0; i < vector.Length; i++)
            {
                vector[i] = (float)(vector[i] / norm);
            }
        }

        return vector;
    }
}
