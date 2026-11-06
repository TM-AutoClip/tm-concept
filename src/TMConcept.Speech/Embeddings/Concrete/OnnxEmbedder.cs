namespace TMConcept.Speech.Embeddings;

/// <summary>
/// The local embedding model in the worker process: the files of the current folder (<see cref="LocalEmbeddingModels.ModelFile"/> and
/// <see cref="LocalEmbeddingModels.TokenizerFile"/>), ONNX Runtime on the processor, mean pooling over the tokens and L2 normalization (how
/// the sentence-transformers model is meant to be used). One text at a time.
/// </summary>
internal sealed class OnnxEmbedder : IDisposable
{
    private readonly OnnxRuntime _runtime;
    private readonly OnnxRuntime.Session _session;
    private readonly UnigramTokenizer _tokenizer;

    /// <summary>Loads the model of the current folder.</summary>
    public OnnxEmbedder(string libraryFolder, int threads)
    {
        foreach (var file in new[] { LocalEmbeddingModels.ModelFile, LocalEmbeddingModels.TokenizerFile })
        {
            if (!File.Exists(file))
            {
                throw new FileNotFoundException("The embedding model is incomplete: " + file + " is missing.", file);
            }
        }

        _tokenizer = UnigramTokenizer.Load(LocalEmbeddingModels.TokenizerFile);
        _runtime = new OnnxRuntime(libraryFolder);
        try
        {
            _session = _runtime.Open(LocalEmbeddingModels.ModelFile, threads);
        }
        catch
        {
            _runtime.Dispose();
            throw;
        }
    }

    /// <summary>Whether the current folder holds the embedding model (the worker loads it instead of a voice).</summary>
    public static bool IsHere() => File.Exists(LocalEmbeddingModels.TokenizerFile) && File.Exists(LocalEmbeddingModels.ModelFile);

    /// <summary>The library version, for the log.</summary>
    public string RuntimeVersion => _runtime.Version;

    /// <summary>One normalized vector per text.</summary>
    public List<float[]> Embed(IReadOnlyList<string> texts)
    {
        ArgumentNullException.ThrowIfNull(texts);
        var vectors = new List<float[]>(texts.Count);
        foreach (var text in texts)
        {
            var ids = _tokenizer.Encode(text);
            var mask = Enumerable.Repeat(1L, ids.Length).ToArray();
            var (values, shape) = _runtime.Run(_session, new Dictionary<string, long[]>(StringComparer.Ordinal)
            {
                ["input_ids"] = ids,
                ["attention_mask"] = mask,
                ["token_type_ids"] = new long[ids.Length],
            });
            vectors.Add(Pool(values, shape));
        }

        return vectors;
    }

    /// <inheritdoc />
    public void Dispose()
    {
        _session.Dispose();
        _runtime.Dispose();
    }

    /// <summary>Mean of the token vectors ([1, tokens, size]; a [1, size] output is taken as it is), then L2-normalized.</summary>
    private static float[] Pool(float[] values, long[] shape)
    {
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
                vector[i] /= tokens;
            }
        }
        else if (shape.Length == 2)
        {
            vector = values;
        }
        else
        {
            throw new InvalidOperationException("The embedding model gave an output of an unexpected shape.");
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
