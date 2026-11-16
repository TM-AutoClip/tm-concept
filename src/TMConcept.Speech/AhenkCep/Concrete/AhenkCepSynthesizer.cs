using TMConcept.Speech.Embeddings;

namespace TMConcept.Speech.AhenkCep;

/// <summary>
/// Ahenk Cep in the speech worker (the files of the current folder, <see cref="AhenkCepModel"/>), on the processor with the ONNX Runtime of
/// the sherpa-onnx libraries. A C# port of the model's own sampling loop (site/js/engine.js of github.com/ozguradmin/ahenk-turkish-tts,
/// Apache-2.0, which mirrors tts/small/ahenk_cep.py of EMA Lightning). Per piece (<see cref="AhenkCepText"/>): the text graph gives the
/// hidden text and a length for each character; the lengths divided by the speed give each word's frames (rounded half to even, 1 to 250,
/// at most 3000 frames); the condition graph spreads the text over the frames; 16 Euler steps of the flow with guidance 2 turn seeded noise
/// (<see cref="AhenkCepNoise"/>) into 64-value latent frames; the decoder turns them into sound 100 frames at a time with 8 frames of context
/// on each side, 1920 samples per frame (48 kHz). The pause after a piece is silence.
/// </summary>
internal sealed class AhenkCepSynthesizer : IDisposable
{
    private readonly OnnxRuntime _runtime;
    private readonly OnnxRuntime.Session _text;
    private readonly OnnxRuntime.Session _condition;
    private readonly OnnxRuntime.Session _step;
    private readonly OnnxRuntime.Session _decoder;

    /// <summary>Loads the model of <paramref name="modelFolder"/> (empty: the current folder, as the worker runs).</summary>
    public AhenkCepSynthesizer(string libraryFolder, int threads, string modelFolder = "")
    {
        var missing = AhenkCepModel.Files.Select(f => Path.Combine(modelFolder, f.ExecutablePath)).FirstOrDefault(f => !File.Exists(f));
        if (missing is not null)
        {
            throw new FileNotFoundException("The Ahenk Cep model is incomplete: " + missing + " is missing.", missing);
        }

        _runtime = new OnnxRuntime(libraryFolder);
        var opened = new List<OnnxRuntime.Session>();
        try
        {
            _text = Open(AhenkCepModel.TextFile);
            _condition = Open(AhenkCepModel.ConditionFile);
            _step = Open(AhenkCepModel.StepFile);
            _decoder = Open(AhenkCepModel.DecoderFile);
        }
        catch
        {
            opened.ForEach(s => s.Dispose());
            _runtime.Dispose();
            throw;
        }

        OnnxRuntime.Session Open(string file)
        {
            var session = _runtime.Open(Path.Combine(modelFolder, file), threads);
            opened.Add(session);
            return session;
        }
    }

    /// <summary>Whether the current folder holds the Ahenk Cep model (the worker loads it instead of a sherpa-onnx voice).</summary>
    public static bool IsHere() => File.Exists(AhenkCepModel.StepFile) && File.Exists(AhenkCepModel.DecoderFile);

    /// <summary>The library version, for the log.</summary>
    public string RuntimeVersion => _runtime.Version;

    /// <summary>Pieces and latent frames of the last call, for the log.</summary>
    public (int Pieces, int Frames) Last { get; private set; }

    /// <summary>
    /// Speaks <paramref name="text"/> (its spoken form) in style <paramref name="style"/> (an index of <see cref="AhenkCepModel.Styles"/>) at
    /// <paramref name="speed"/>; mono samples at <see cref="AhenkCepModel.SampleRate"/>.
    /// </summary>
    public float[] Speak(string text, int style, double speed, ulong seed = 0)
    {
        var pieces = AhenkCepText.Pieces(text, speed);
        var audio = new List<float>();
        var frames = 0;
        for (var i = 0; i < pieces.Count; i++)
        {
            var piece = pieces[i];
            var (latents, count) = Latents(piece, Math.Clamp(style, 0, AhenkCepModel.Styles.Count - 1), (float)speed, AhenkCepNoise.PieceSeed(seed, i));
            frames += count;
            audio.AddRange(Decode(latents, count));
            if (piece.Pause > 0)
            {
                audio.AddRange(new float[(int)Math.Round(piece.Pause * AhenkCepModel.SampleRate)]);
            }
        }

        Last = (pieces.Count, frames);
        return audio.ToArray();
    }

    /// <summary>
    /// The frames of each word: lengths divided by the speed, summed per word, rounded half to even, kept within 1 and
    /// <see cref="AhenkCepModel.MaxWordFrames"/>; at most <see cref="AhenkCepModel.MaxFrames"/> in all. Returns the divided lengths, the word
    /// of every frame and the position of every frame inside its word (0 to 1).
    /// </summary>
    public static (float[] Durations, long[] FrameWords, float[] FramePositions) Plan(ReadOnlySpan<float> durations, long[] words, float speed)
    {
        ArgumentNullException.ThrowIfNull(words);
        var divided = new float[words.Length];
        for (var i = 0; i < words.Length; i++)
        {
            divided[i] = durations[i] / speed;
        }

        var count = (int)words[^1] + 1;
        var sums = new float[count];
        for (var i = 0; i < words.Length; i++)
        {
            sums[words[i]] += divided[i];
        }

        var perWord = new int[count];
        var total = 0;
        for (var w = 0; w < count; w++)
        {
            perWord[w] = Math.Min(AhenkCepModel.MaxWordFrames, Math.Max(1, (int)Math.Round(sums[w], MidpointRounding.ToEven)));
            total += perWord[w];
        }

        var frames = Math.Min(total, AhenkCepModel.MaxFrames);
        var frameWords = new long[frames];
        var positions = new float[frames];
        var k = 0;
        for (var w = 0; w < count && k < frames; w++)
        {
            for (var j = 0; j < perWord[w] && k < frames; j++, k++)
            {
                frameWords[k] = w;
                positions[k] = (float)j / perWord[w];
            }
        }

        return (divided, frameWords, positions);
    }

    /// <inheritdoc />
    public void Dispose()
    {
        _text.Dispose();
        _condition.Dispose();
        _step.Dispose();
        _decoder.Dispose();
        _runtime.Dispose();
    }

    private (float[] Latents, int Frames) Latents(AhenkCepPiece piece, int style, float speed, ulong seed)
    {
        var length = piece.Ids.Length;
        using var ids = _runtime.Tensor(piece.Ids, 1, length);
        using var words = _runtime.Tensor(piece.Words, 1, length);
        using var starts = _runtime.Tensor(piece.WordStarts, 1, length);
        using var styleValue = _runtime.Tensor(new[] { (long)style }, 1);
        var text = _runtime.Run(_text, [("ids", ids), ("style", styleValue)], ["h", "dur"]);
        OnnxRuntime.Value condition;
        int frames;
        try
        {
            var (durations, frameWords, positions) = Plan(text[1].Floats(), piece.Words, speed);
            frames = frameWords.Length;
            using var duration = _runtime.Tensor(durations, 1, length);
            using var frameWord = _runtime.Tensor(frameWords, 1, frames);
            using var framePosition = _runtime.Tensor(positions, 1, frames);
            condition = _runtime.Run(_condition,
                [("h", text[0]), ("dur", duration), ("cw", words), ("wstart", starts), ("fw", frameWord), ("fp", framePosition)], ["cond"])[0];
        }
        finally
        {
            foreach (var value in text)
            {
                value.Dispose();
            }
        }

        using (condition)
        {
            using var step = _runtime.Tensor(new[] { 1f / AhenkCepModel.Steps }, 1);
            using var guidance = _runtime.Tensor(new[] { AhenkCepModel.Guidance }, 1);
            var x = _runtime.Tensor(AhenkCepNoise.Normal(seed, frames * AhenkCepModel.LatentSize), 1, frames, AhenkCepModel.LatentSize);
            try
            {
                for (var k = 0; k < AhenkCepModel.Steps; k++)
                {
                    using var time = _runtime.Tensor(new[] { (float)k / AhenkCepModel.Steps }, 1);
                    var next = _runtime.Run(_step, [("x", x), ("cond", condition), ("t", time), ("dt", step), ("cfg", guidance)], ["x_next"])[0];
                    x.Dispose();
                    x = next;
                }

                return (x.Floats().ToArray(), frames);
            }
            finally
            {
                x.Dispose();
            }
        }
    }

    private List<float> Decode(float[] latents, int frames)
    {
        var audio = new List<float>(frames * AhenkCepModel.Hop);
        for (var start = 0; start < frames; start += AhenkCepModel.Window)
        {
            var end = Math.Min(frames, start + AhenkCepModel.Window);
            var from = Math.Max(0, start - AhenkCepModel.Context);
            var to = Math.Min(frames, end + AhenkCepModel.Context);
            var length = to - from;
            var z = new float[AhenkCepModel.LatentSize * length];
            for (var t = 0; t < length; t++)
            {
                for (var c = 0; c < AhenkCepModel.LatentSize; c++)
                {
                    z[(c * length) + t] = latents[((from + t) * AhenkCepModel.LatentSize) + c];
                }
            }

            using var input = _runtime.Tensor(z, 1, AhenkCepModel.LatentSize, length);
            using var output = _runtime.Run(_decoder, [("z", input)], ["audio"])[0];
            var samples = output.Floats();
            var first = (start - from) * AhenkCepModel.Hop;
            var last = Math.Min(samples.Length, (end - from) * AhenkCepModel.Hop);
            if (last > first)
            {
                audio.AddRange(samples[first..last].ToArray());
            }
        }

        return audio;
    }
}
