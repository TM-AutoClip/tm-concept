using System.Globalization;
using System.Text;
using System.Text.Json;
using TMConcept.Core.Speech;

namespace TMConcept.Speech.Speech;

/// <summary>
/// The speech worker: the app's own program started again as <c>TMConcept --speech-worker &lt;library folder&gt; &lt;threads&gt;</c> in the
/// model folder. It loads sherpa-onnx and the voice model on the first request, then reads one JSON request per line from standard input
/// (UTF-8) and answers one JSON line per request on standard output. It ends when its input closes (the app closed it or the app is gone).
/// The window process never loads the native speech libraries: their memory is the worker's and goes away with it, and a native failure
/// (sherpa-onnx ends the process on some errors) cannot take the window down.
/// </summary>
public static class SpeechWorker
{
    /// <summary>The first argument that starts the worker instead of the window.</summary>
    public const string Argument = "--speech-worker";

    /// <summary>Runs the worker; returns the exit code (0: input closed; 2: wrong arguments; 3: the libraries or the model could not be loaded).</summary>
    public static int Run(IReadOnlyList<string> arguments, Stream input, Stream output, TextWriter error)
    {
        ArgumentNullException.ThrowIfNull(arguments);
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(error);
        if (arguments.Count != 3 || arguments[0] != Argument || !int.TryParse(arguments[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out var threads))
        {
            error.WriteLine("usage: " + Argument + " <library folder> <threads>");
            return 2;
        }

        var utf8 = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
        using var reader = new StreamReader(input, utf8);
        using var writer = new StreamWriter(output, utf8) { AutoFlush = true, NewLine = "\n" };
        SherpaSynthesizer? synthesizer = null;
        try
        {
            while (reader.ReadLine() is { } line)
            {
                if (line.Length == 0)
                {
                    continue;
                }

                SpeechWorkerReply reply;
                try
                {
                    synthesizer ??= Load(arguments[1], threads);
                    reply = Speak(synthesizer, JsonSerializer.Deserialize(line, SpeechWorkerJsonContext.Default.SpeechWorkerRequest)
                        ?? throw new JsonException("Empty request."));
                }
                catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException or BadImageFormatException or FileNotFoundException
                    or InvalidOperationException)
                {
                    // The libraries or the model cannot be used: answer once with the reason and stop.
                    error.WriteLine("speech worker: " + ex.GetType().Name + ": " + ex.Message);
                    writer.WriteLine(JsonSerializer.Serialize(new SpeechWorkerReply(false, 0, "model", ex.Message), SpeechWorkerJsonContext.Default.SpeechWorkerReply));
                    return 3;
                }
                catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException or ArgumentException)
                {
                    error.WriteLine("speech worker: " + ex.GetType().Name + ": " + ex.Message);
                    reply = new SpeechWorkerReply(false, 0, "engine", ex.Message);
                }

                writer.WriteLine(JsonSerializer.Serialize(reply, SpeechWorkerJsonContext.Default.SpeechWorkerReply));
            }

            return 0;
        }
        finally
        {
            synthesizer?.Dispose();
        }
    }

    private static SherpaSynthesizer Load(string libraryFolder, int threads)
    {
        var missing = SherpaNative.Files.FirstOrDefault(f => !File.Exists(Path.Combine(libraryFolder, f)));
        if (missing is not null)
        {
            throw new FileNotFoundException("The speech library " + missing + " is missing.", missing);
        }

        SherpaNative.Use(libraryFolder);
        return new SherpaSynthesizer(Math.Clamp(threads, 1, 16));
    }

    private static SpeechWorkerReply Speak(SherpaSynthesizer synthesizer, SpeechWorkerRequest request)
    {
        var (samples, rate) = synthesizer.Speak(request.Text, request.Language, request.Speaker, request.Speed, request.Steps);

        // An engine that could not turn the text into sounds returns no samples: that is a failure, never a silent clip.
        if (rate <= 0 || samples.Length == 0)
        {
            return new SpeechWorkerReply(false, 0, "engine", "The voice produced no audio for the text.");
        }

        WaveFile.WritePcm16(request.OutputWav, samples, rate);
        return new SpeechWorkerReply(true, (long)Math.Round(samples.Length * 1000.0 / rate), string.Empty, string.Empty);
    }
}
