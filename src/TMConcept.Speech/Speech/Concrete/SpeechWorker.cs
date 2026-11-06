using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using TMConcept.Core.Speech;

namespace TMConcept.Speech.Speech;

/// <summary>
/// The speech worker: the app's own program started again as <c>TMConcept --speech-worker &lt;library folder&gt; &lt;threads&gt;</c> in the
/// model folder. It loads sherpa-onnx and the voice model on the first request, then reads one JSON request per line from standard input
/// (UTF-8) and answers one JSON line per request on standard output. It ends when its input closes (the app closed it or the app is gone).
/// The window process never loads the native speech libraries: their memory is the worker's and goes away with it, and a native failure
/// (sherpa-onnx ends the process on some errors) cannot take the window down. It writes its own log file (the app passes the folder in
/// <see cref="LogDirectoryVariable"/>): start, model load, each scene's length and time (never its text), failures and the end.
/// </summary>
public static class SpeechWorker
{
    /// <summary>The first argument that starts the worker instead of the window.</summary>
    public const string Argument = "--speech-worker";

    /// <summary>The environment variable that names the log folder of the worker (the app's own; empty: no log file).</summary>
    public const string LogDirectoryVariable = "TMCONCEPT_LOG_DIR";

    /// <summary>Runs the worker; returns the exit code (0: input closed; 2: wrong arguments; 3: the libraries or the model could not be loaded).</summary>
    public static int Run(IReadOnlyList<string> arguments, Stream input, Stream output, TextWriter error, ILogger? logger = null)
    {
        logger ??= NullLogger.Instance;
        ArgumentNullException.ThrowIfNull(arguments);
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(error);
        if (arguments.Count != 3 || arguments[0] != Argument || !int.TryParse(arguments[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out var threads))
        {
            error.WriteLine("usage: " + Argument + " <library folder> <threads>");
            logger.LogError("The speech worker was started with wrong arguments ({Count})", arguments.Count);
            return 2;
        }

        logger.LogInformation("Speech worker started ({Threads} threads)", threads);

        var utf8 = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
        using var reader = new StreamReader(input, utf8);
        using var writer = new StreamWriter(output, utf8) { AutoFlush = true, NewLine = "\n" };
        SherpaSynthesizer? synthesizer = null;
        Embeddings.OnnxEmbedder? embedder = null;
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
                    if (synthesizer is null && embedder is null)
                    {
                        var loading = Stopwatch.StartNew();
                        if (Embeddings.OnnxEmbedder.IsHere())
                        {
                            embedder = new Embeddings.OnnxEmbedder(arguments[1], Math.Clamp(threads, 1, 16));
                            logger.LogInformation("Embedding model loaded in {Ms} ms (ONNX Runtime {Version})", loading.ElapsedMilliseconds, embedder.RuntimeVersion);
                        }
                        else
                        {
                            synthesizer = Load(arguments[1], threads);
                            logger.LogInformation("Voice libraries loaded in {Ms} ms", loading.ElapsedMilliseconds);
                        }
                    }

                    var request = JsonSerializer.Deserialize(line, SpeechWorkerJsonContext.Default.SpeechWorkerRequest) ?? throw new JsonException("Empty request.");
                    var watch = Stopwatch.StartNew();
                    if (request.Texts is { } texts)
                    {
                        reply = embedder is null
                            ? new SpeechWorkerReply(false, 0, "engine", "This worker holds a voice, not the embedding model.")
                            : new SpeechWorkerReply(true, 0, string.Empty, string.Empty, embedder.Embed(texts));
                        logger.LogInformation("Embedded {Count} texts in {Ms} ms", texts.Count, watch.ElapsedMilliseconds);
                        writer.WriteLine(JsonSerializer.Serialize(reply, SpeechWorkerJsonContext.Default.SpeechWorkerReply));
                        continue;
                    }

                    reply = synthesizer is null
                        ? new SpeechWorkerReply(false, 0, "engine", "This worker holds the embedding model, not a voice.")
                        : Speak(synthesizer, request);
                    if (reply.Ok)
                    {
                        logger.LogInformation("Scene spoken ({Language}, {Characters} characters): {AudioMs} ms of audio in {Ms} ms", request.Language,
                            request.Text.Length, reply.DurationMs, watch.ElapsedMilliseconds);
                    }
                    else
                    {
                        logger.LogError("The voice could not speak a scene ({Code}): {Error}", reply.Code, reply.Error);
                    }
                }
                catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException or BadImageFormatException or FileNotFoundException or InvalidDataException
                    or InvalidOperationException)
                {
                    // The libraries or the model cannot be used: answer once with the reason and stop.
                    error.WriteLine("speech worker: " + ex.GetType().Name + ": " + ex.Message);
                    logger.LogError(ex, "The voice libraries or the model could not be loaded; the speech worker stops");
                    writer.WriteLine(JsonSerializer.Serialize(new SpeechWorkerReply(false, 0, "model", ex.Message), SpeechWorkerJsonContext.Default.SpeechWorkerReply));
                    return 3;
                }
                catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException or ArgumentException)
                {
                    error.WriteLine("speech worker: " + ex.GetType().Name + ": " + ex.Message);
                    logger.LogError(ex, "A speech request failed");
                    reply = new SpeechWorkerReply(false, 0, "engine", ex.Message);
                }

                writer.WriteLine(JsonSerializer.Serialize(reply, SpeechWorkerJsonContext.Default.SpeechWorkerReply));
            }

            logger.LogInformation("The speech worker's input closed; it ends");
            return 0;
        }
        finally
        {
            synthesizer?.Dispose();
            embedder?.Dispose();
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
