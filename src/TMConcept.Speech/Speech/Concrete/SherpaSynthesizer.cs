using System.Runtime.InteropServices;
using System.Text.Json;

namespace TMConcept.Speech.Speech;

/// <summary>
/// One loaded voice model in the speech worker (processor only): Supertonic 3 or Kokoro, told apart by the files of the current folder. The
/// model files are named relative to the current folder (the worker starts in the model folder), so no path with the user's folder names
/// crosses into the native library: its file functions take narrow strings, which on Windows would mangle a non-ASCII user name. Kokoro reads
/// English words from its dictionary (sherpa-onnx 2.x has no espeak-ng; the engine spells numbers out before).
/// </summary>
internal sealed class SherpaSynthesizer : IDisposable
{
    /// <summary>The files of the Supertonic model, in the order of the configuration.</summary>
    public static readonly IReadOnlyList<string> ModelFiles =
    [
        "duration_predictor.int8.onnx", "text_encoder.int8.onnx", "vector_estimator.int8.onnx", "vocoder.int8.onnx", "tts.json", "unicode_indexer.bin",
        "voice.bin",
    ];

    /// <summary>The files of the Kokoro model: model, voices, tokens, the American English dictionary.</summary>
    public static readonly IReadOnlyList<string> KokoroFiles = ["model.int8.onnx", "voices.bin", "tokens.txt", "lexicon-us-en.txt"];

    private IntPtr _tts;

    /// <summary>Loads the model of the current folder with <paramref name="threads"/> processor threads.</summary>
    public SherpaSynthesizer(int threads)
    {
        var kokoro = File.Exists(KokoroFiles[1]) && !File.Exists(ModelFiles[4]);
        var files = kokoro ? KokoroFiles : ModelFiles;
        var missing = files.FirstOrDefault(f => !File.Exists(f));
        if (missing is not null)
        {
            throw new FileNotFoundException("The voice model is incomplete: " + missing + " is missing.", missing);
        }

        var strings = new List<IntPtr>();
        IntPtr Text(string value)
        {
            var pointer = Marshal.StringToCoTaskMemUTF8(value);
            strings.Add(pointer);
            return pointer;
        }

        try
        {
            var config = new SherpaTtsConfig { MaxNumSentences = 1, SilenceScale = 0.2f };
            config.Model.NumThreads = threads;
            config.Model.Debug = 0;
            config.Model.Provider = Text("cpu");
            if (kokoro)
            {
                config.Model.Kokoro = new SherpaKokoroConfig
                {
                    Model = Text(KokoroFiles[0]),
                    Voices = Text(KokoroFiles[1]),
                    Tokens = Text(KokoroFiles[2]),
                    DataDir = Text(string.Empty),
                    LengthScale = 1.0f,
                    DictDir = Text(string.Empty),
                    Lexicon = Text(KokoroFiles[3]),
                    Lang = Text(string.Empty),
                };
            }
            else
            {
                config.Model.Supertonic = new SherpaSupertonicConfig
                {
                    DurationPredictor = Text(ModelFiles[0]),
                    TextEncoder = Text(ModelFiles[1]),
                    VectorEstimator = Text(ModelFiles[2]),
                    Vocoder = Text(ModelFiles[3]),
                    TtsJson = Text(ModelFiles[4]),
                    UnicodeIndexer = Text(ModelFiles[5]),
                    VoiceStyle = Text(ModelFiles[6]),
                };
            }

            config.RuleFsts = Text(string.Empty);
            config.RuleFars = Text(string.Empty);
            _tts = SherpaNative.SherpaOnnxCreateOfflineTts(in config);
        }
        finally
        {
            strings.ForEach(Marshal.FreeCoTaskMem);
        }

        if (_tts == IntPtr.Zero)
        {
            throw new InvalidOperationException("sherpa-onnx could not load the voice model (its own message is in the worker's error output).");
        }
    }

    /// <summary>Speaks <paramref name="text"/>; returns the samples and their sample rate (no samples = the engine produced nothing).</summary>
    public (float[] Samples, int SampleRate) Speak(string text, string language, int speaker, float speed, int steps)
    {
        ObjectDisposedException.ThrowIf(_tts == IntPtr.Zero, this);
        var extra = Marshal.StringToCoTaskMemUTF8(JsonSerializer.Serialize(new SpeechWorkerExtra(language), SpeechWorkerJsonContext.Default.SpeechWorkerExtra));
        try
        {
            var generation = new SherpaGenerationConfig { SilenceScale = 0.2f, Speed = speed, Sid = speaker, NumSteps = steps, Extra = extra };
            var audio = SherpaNative.SherpaOnnxOfflineTtsGenerateWithConfig(_tts, text, in generation, IntPtr.Zero, IntPtr.Zero);
            if (audio == IntPtr.Zero)
            {
                return ([], 0);
            }

            try
            {
                var result = Marshal.PtrToStructure<SherpaGeneratedAudio>(audio);
                var samples = new float[Math.Max(result.Count, 0)];
                if (samples.Length > 0)
                {
                    Marshal.Copy(result.Samples, samples, 0, samples.Length);
                }

                return (samples, result.SampleRate);
            }
            finally
            {
                SherpaNative.SherpaOnnxDestroyOfflineTtsGeneratedAudio(audio);
            }
        }
        finally
        {
            Marshal.FreeCoTaskMem(extra);
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_tts != IntPtr.Zero)
        {
            SherpaNative.SherpaOnnxDestroyOfflineTts(_tts);
            _tts = IntPtr.Zero;
        }
    }
}
