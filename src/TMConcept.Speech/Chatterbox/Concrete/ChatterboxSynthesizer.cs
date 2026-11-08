using System.Security.Cryptography;
using TMConcept.Speech.Embeddings;

namespace TMConcept.Speech.Chatterbox;

/// <summary>
/// Chatterbox Multilingual V3 in the speech worker (the files of the current folder, <see cref="ChatterboxModel"/>), on the processor with the
/// ONNX Runtime of the sherpa-onnx libraries. Per voice sample (24 kHz; the graph uses its first 6 s) the speech encoder gives the
/// conditioning once (kept while the same sample is used). Per part of a scene: tokens (<see cref="ChatterboxTokenizer"/>) → embeddings with
/// the exaggeration → conditioning in front → the 4-bit language model step by step with its key/value cache (handed from step to step as
/// ONNX Runtime values) and <see cref="ChatterboxSampler"/> until the stop token → the sample's speech tokens in front → the decoder (flow
/// matching + vocoder) → the last token's 960 samples dropped (upstream V3 does it: that token decodes into noise). Parts are joined with
/// <see cref="ChatterboxModel.PartGapMs"/> of silence. Mirrors the onnx-community example script; no cfg (the graphs have no unconditional
/// pass, and upstream advises cfg 0 when the sample's language differs from the text's).
/// </summary>
internal sealed class ChatterboxSynthesizer : IDisposable
{
    private readonly OnnxRuntime _runtime;
    private readonly OnnxRuntime.Session _encoder;
    private readonly OnnxRuntime.Session _embed;
    private readonly OnnxRuntime.Session _language;
    private readonly OnnxRuntime.Session _decoder;
    private readonly ChatterboxTokenizer _tokenizer;
    private readonly string[] _pastNames;
    private readonly string[] _outputNames;
    private Conditioning? _conditioning;

    /// <summary>Loads the model of the current folder.</summary>
    public ChatterboxSynthesizer(string libraryFolder, int threads)
    {
        var missing = ChatterboxModel.Files.Select(f => f.ExecutablePath).FirstOrDefault(f => !File.Exists(f));
        if (missing is not null)
        {
            throw new FileNotFoundException("The Chatterbox model is incomplete: " + missing + " is missing.", missing);
        }

        _tokenizer = ChatterboxTokenizer.Load(ChatterboxModel.TokenizerFile);
        _pastNames = Enumerable.Range(0, ChatterboxModel.Layers)
            .SelectMany(l => new[] { $"past_key_values.{l}.key", $"past_key_values.{l}.value" }).ToArray();
        _outputNames = new[] { "logits" }.Concat(Enumerable.Range(0, ChatterboxModel.Layers)
            .SelectMany(l => new[] { $"present.{l}.key", $"present.{l}.value" })).ToArray();
        _runtime = new OnnxRuntime(libraryFolder);
        var opened = new List<OnnxRuntime.Session>();
        try
        {
            _encoder = Open(ChatterboxModel.SpeechEncoderFile);
            _embed = Open(ChatterboxModel.EmbedFile);
            _language = Open(ChatterboxModel.LanguageModelFile);
            _decoder = Open(ChatterboxModel.DecoderFile);
        }
        catch
        {
            opened.ForEach(s => s.Dispose());
            _runtime.Dispose();
            throw;
        }

        OnnxRuntime.Session Open(string file)
        {
            // Measured on 8 Nov 2026: the decoder takes about 81 s to open (the other graphs 4 s together); a lower graph optimization
            // level made it slower (BASIC: 100 s), so the library's default stays.
            var watch = System.Diagnostics.Stopwatch.StartNew();
            var session = _runtime.Open(file, threads);
            opened.Add(session);
            LoadTimes += Path.GetFileNameWithoutExtension(file) + " " + watch.ElapsedMilliseconds + " ms; ";
            return session;
        }
    }

    /// <summary>Whether the current folder holds the Chatterbox model (the worker loads it instead of a sherpa-onnx voice).</summary>
    public static bool IsHere() => File.Exists(ChatterboxModel.DecoderFile) && File.Exists(ChatterboxModel.LanguageModelFile);

    /// <summary>How long each graph took to open, for the log.</summary>
    public string LoadTimes { get; private set; } = string.Empty;

    /// <summary>The library version, for the log.</summary>
    public string RuntimeVersion => _runtime.Version;

    /// <summary>Speech tokens of the last call (for the log: steps of the language model).</summary>
    public int LastTokens { get; private set; }

    /// <summary>Milliseconds of the last call per stage (voice sample, language model, decoder), for the log.</summary>
    public (long Sample, long Language, long Decoder) LastTimes { get; private set; }

    /// <summary>Speaks <paramref name="text"/> in <paramref name="languageCode"/> with the voice of <paramref name="reference"/> (24 kHz mono).</summary>
    public float[] Speak(string text, string languageCode, float[] reference)
    {
        ArgumentNullException.ThrowIfNull(reference);
        var watch = System.Diagnostics.Stopwatch.StartNew();
        var conditioning = Condition(reference);
        var sample = watch.ElapsedMilliseconds;
        long language = 0;
        long decoder = 0;
        var gap = new float[ChatterboxModel.SampleRate * ChatterboxModel.PartGapMs / 1000];
        var audio = new List<float>();
        LastTokens = 0;
        foreach (var part in ChatterboxText.Parts(text, languageCode))
        {
            watch.Restart();
            var tokens = Generate(_tokenizer.Encode(part, languageCode), conditioning);
            language += watch.ElapsedMilliseconds;
            LastTokens += tokens.Length;
            if (tokens.Length == 0)
            {
                continue;
            }

            if (audio.Count > 0)
            {
                audio.AddRange(gap);
            }

            watch.Restart();
            audio.AddRange(Decode(tokens, conditioning));
            decoder += watch.ElapsedMilliseconds;
        }

        LastTimes = (sample, language, decoder);
        return audio.ToArray();
    }

    /// <inheritdoc />
    public void Dispose()
    {
        _encoder.Dispose();
        _embed.Dispose();
        _language.Dispose();
        _decoder.Dispose();
        _runtime.Dispose();
    }

    private Conditioning Condition(float[] reference)
    {
        var key = Convert.ToHexString(SHA256.HashData(System.Runtime.InteropServices.MemoryMarshal.AsBytes(reference.AsSpan())));
        if (_conditioning is { } kept && kept.Key == key)
        {
            return kept;
        }

        using var audio = _runtime.Tensor(reference, 1, reference.Length);
        var outputs = _runtime.Run(_encoder, [("audio_values", audio)], ["audio_features", "audio_tokens", "speaker_embeddings", "speaker_features"]);
        try
        {
            var features = outputs[0].Shape();
            var speakerFeatures = outputs[3].Shape();
            _conditioning = new Conditioning(key, outputs[0].Floats().ToArray(), (int)features[1], outputs[1].Longs(), outputs[2].Floats().ToArray(),
                outputs[3].Floats().ToArray(), speakerFeatures);
            return _conditioning;
        }
        finally
        {
            foreach (var output in outputs)
            {
                output.Dispose();
            }
        }
    }

    /// <summary>The speech tokens of one part (without the start and stop tokens and anything that is not a sound).</summary>
    private long[] Generate(long[] ids, Conditioning conditioning)
    {
        const int width = 1024;
        var positions = new long[ids.Length];
        for (var i = 0; i < ids.Length; i++)
        {
            // Text tokens count from 0 after the exaggeration token; the special speech tokens sit at 0 (the example script's rule).
            positions[i] = ids[i] >= ChatterboxModel.StartSpeech ? 0 : i - 1;
        }

        float[] first;
        using (var input = _runtime.Tensor(ids, 1, ids.Length))
        using (var position = _runtime.Tensor(positions, 1, positions.Length))
        using (var exaggeration = _runtime.Tensor(new[] { ChatterboxModel.Exaggeration }, 1))
        {
            var embedded = _runtime.Run(_embed, [("input_ids", input), ("position_ids", position), ("exaggeration", exaggeration)], ["inputs_embeds"])[0];
            using (embedded)
            {
                var text = embedded.Floats();
                first = new float[conditioning.Features.Length + text.Length];
                conditioning.Features.CopyTo(first, 0);
                text.CopyTo(first.AsSpan(conditioning.Features.Length));
            }
        }

        var length = conditioning.FeatureTokens + ids.Length;
        var sampler = ChatterboxSampler.Default();
        sampler.Saw(ChatterboxModel.StartSpeech);
        var generated = new List<long>();
        var past = _pastNames.Select(_ => _runtime.Tensor(new float[1].AsSpan(0, 0).ToArray(), 1, ChatterboxModel.Heads, 0, ChatterboxModel.HeadSize))
            .ToArray();
        var embeds = _runtime.Tensor(first, 1, length, width);
        try
        {
            for (var step = 0; step < ChatterboxModel.MaxNewTokens; step++)
            {
                var mask = new long[length];
                Array.Fill(mask, 1L);
                OnnxRuntime.Value[] outputs;
                using (var attention = _runtime.Tensor(mask, 1, length))
                {
                    var inputs = new List<(string, OnnxRuntime.Value)>(2 + past.Length) { ("inputs_embeds", embeds), ("attention_mask", attention) };
                    for (var i = 0; i < past.Length; i++)
                    {
                        inputs.Add((_pastNames[i], past[i]));
                    }

                    outputs = _runtime.Run(_language, inputs, _outputNames);
                }

                long next;
                using (var logits = outputs[0])
                {
                    var all = logits.Floats();
                    var vocabulary = (int)logits.Shape()[2];
                    next = sampler.Next(all[^vocabulary..]);
                }

                foreach (var old in past)
                {
                    old.Dispose();
                }

                past = outputs[1..];
                if (next == ChatterboxModel.StopSpeech)
                {
                    break;
                }

                generated.Add(next);
                embeds.Dispose();
                using var token = _runtime.Tensor(new[] { next }, 1, 1);
                using var at = _runtime.Tensor(new[] { (long)step + 1 }, 1, 1);
                using var exaggeration = _runtime.Tensor(new[] { ChatterboxModel.Exaggeration }, 1);
                embeds = _runtime.Run(_embed, [("input_ids", token), ("position_ids", at), ("exaggeration", exaggeration)], ["inputs_embeds"])[0];
                length++;
            }
        }
        finally
        {
            embeds.Dispose();
            foreach (var value in past)
            {
                value.Dispose();
            }
        }

        // drop_invalid_tokens: only sound tokens reach the decoder.
        return generated.Where(t => t < ChatterboxModel.StartSpeech).ToArray();
    }

    private float[] Decode(long[] tokens, Conditioning conditioning)
    {
        var all = conditioning.PromptTokens.Concat(tokens).ToArray();
        using var speech = _runtime.Tensor(all, 1, all.Length);
        using var embeddings = _runtime.Tensor(conditioning.SpeakerEmbedding, 1, conditioning.SpeakerEmbedding.Length);
        using var features = _runtime.Tensor(conditioning.SpeakerFeatures, conditioning.SpeakerFeaturesShape);
        using var waveform = _runtime.Run(_decoder, [("speech_tokens", speech), ("speaker_embeddings", embeddings), ("speaker_features", features)],
            ["waveform"])[0];
        var samples = waveform.Floats();

        // Upstream V3: wav[:(n - 1) * 960] — the final token's audio is noise.
        var keep = Math.Min(samples.Length, Math.Max(1, tokens.Length - 1) * ChatterboxModel.SamplesPerToken);
        return samples[..keep].ToArray();
    }

    /// <summary>What the speech encoder gives for one voice sample.</summary>
    private sealed record Conditioning(string Key, float[] Features, int FeatureTokens, long[] PromptTokens, float[] SpeakerEmbedding,
        float[] SpeakerFeatures, long[] SpeakerFeaturesShape);
}
