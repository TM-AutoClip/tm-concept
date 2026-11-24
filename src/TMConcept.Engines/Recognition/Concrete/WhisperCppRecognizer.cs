using System.Globalization;
using Microsoft.Extensions.Logging;
using TMConcept.Core.Paths;
using TMConcept.Core.Processes;
using TMConcept.Core.Subtitles;
using TMConcept.Core.Tools;
using TMConcept.Engines.Tools;

namespace TMConcept.Engines.Recognition;

/// <summary>
/// <see cref="IWordRecognizer"/> with whisper.cpp's whisper-cli (0.1.48, S3). The narration clip is turned into 16 kHz mono WAV by the app's
/// ffmpeg, then heard with <c>--output-json --max-len 1 --split-on-word</c> (one entry per word with its time, <see cref="WhisperJson"/>) in
/// the narration's language. A heavy job: one file at a time, a below-normal process (<see cref="IProcessRunner"/>), on at most all but two of
/// the processor's threads; never the graphics card (the processor build). The work files are removed afterwards.
/// </summary>
public sealed class WhisperCppRecognizer(ToolInstaller tools, RecognizerModelStore store, IAppPaths paths, IProcessRunner runner,
    ILogger<WhisperCppRecognizer> logger) : IWordRecognizer
{
    private readonly SemaphoreSlim _one = new(1, 1);

    /// <inheritdoc />
    public bool Supported => tools.Archive(ToolKind.SpeechRecognizer) is not null;

    /// <inheritdoc />
    public bool Ready => tools.Status(ToolKind.SpeechRecognizer).Ready && store.Ready;

    /// <inheritdoc />
    public async Task<IReadOnlyList<RecognizedWord>> RecognizeAsync(string audioFile, string language, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrEmpty(audioFile);
        if (!Ready)
        {
            throw new InvalidOperationException("whisper.cpp is not installed.");
        }

        await _one.WaitAsync(cancellationToken).ConfigureAwait(false);
        var work = Path.Combine(paths.WorkDirectory, "whisper", Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(work);
            var wav = Path.Combine(work, "speech.wav");
            var ffmpeg = await runner.RunAsync(new ProcessStart(tools.Require(ToolKind.Ffmpeg),
                ["-hide_banner", "-loglevel", "error", "-y", "-i", audioFile, "-ar", "16000", "-ac", "1", "-c:a", "pcm_s16le", wav], work), null,
                cancellationToken).ConfigureAwait(false);
            if (ffmpeg.ExitCode != 0 || !File.Exists(wav))
            {
                throw new InvalidOperationException("ffmpeg could not prepare the narration for whisper.cpp (code " + ffmpeg.ExitCode + ").");
            }

            var output = Path.Combine(work, "words");
            var watch = System.Diagnostics.Stopwatch.StartNew();
            var result = await runner.RunAsync(new ProcessStart(tools.Require(ToolKind.SpeechRecognizer), Arguments(store.File, wav, output, language), work),
                null, cancellationToken).ConfigureAwait(false);
            var json = output + ".json";
            if (result.ExitCode != 0 || !File.Exists(json))
            {
                throw new InvalidOperationException("whisper.cpp ended with code " + result.ExitCode + ": " + ChildOutput.Summary(result.LastLines));
            }

            var words = WhisperJson.Words(await File.ReadAllTextAsync(json, cancellationToken).ConfigureAwait(false));
            logger.LogInformation("whisper.cpp heard {Count} words in {Seconds:0.0} s", words.Count, watch.Elapsed.TotalSeconds);
            return words;
        }
        catch (Exception ex) when (ex is ToolInstallException or System.ComponentModel.Win32Exception)
        {
            // A program removed since Ready was read, or one Windows will not start (blocked, a missing library): the subtitles keep their
            // estimated times like any other failure of the hearing (IWordRecognizer's contract), the video goes on.
            throw new InvalidOperationException("whisper.cpp could not be started: " + ex.Message, ex);
        }
        finally
        {
            _one.Release();
            try
            {
                if (Directory.Exists(work))
                {
                    Directory.Delete(work, recursive: true);
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                logger.LogInformation("The whisper.cpp work folder stays until the next clean-up ({Error})", ex.Message);
            }
        }
    }

    /// <summary>The arguments of whisper-cli for one file (<paramref name="output"/> without its ".json").</summary>
    public static IReadOnlyList<string> Arguments(string model, string wav, string output, string language) =>
    [
        "-m", model, "-f", wav, "-l", string.IsNullOrWhiteSpace(language) ? "auto" : language.ToLowerInvariant(),
        "-t", Math.Clamp(Environment.ProcessorCount - 2, 1, 8).ToString(CultureInfo.InvariantCulture),
        "--output-json", "--output-file", output, "--max-len", "1", "--split-on-word", "--no-prints",
    ];
}
