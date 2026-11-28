using System.Globalization;
using Microsoft.Extensions.Logging;
using TMConcept.Core.Models;
using TMConcept.Core.Music;
using TMConcept.Core.Processes;
using TMConcept.Speech.Python;

namespace TMConcept.Speech.Music;

/// <summary>
/// A local music family's generator (28 Nov 2026: MusicGen, MAGNeT, AudioLDM 2, DiffRhythm, SongGeneration, YuE): one piece is one run of the
/// family's script, shipped with the app, with the family's own environment, in the piece's folder: <c>python -X utf8 -I &lt;script&gt; --model
/// &lt;model folder&gt; --prompt-file &lt;prompt.txt&gt; --seconds S --seed N --out &lt;piece.wav&gt;</c>, for a family that sings
/// (<see cref="LocalMusicFamily.Lyrics"/>) <c>--lyrics-file &lt;lyrics.txt&gt;</c> (empty for an instrumental piece), and what the family adds
/// (<see cref="FamilyArguments"/>). The prompt and the words go in UTF-8 files, never on the command line. Offline: Hugging Face and transformers may not reach the network, the libraries write under the
/// app's folder. The length is the model's longest at most (<see cref="LocalMusicVariant.MaxSeconds"/>; a longer video loops the piece). The
/// script's stage lines go to the log as they come; a failed run writes its whole tail to the log and its own failure line into the error
/// (<see cref="LocalMusicOutput"/>; a card out of memory is <see cref="MusicFailure.Memory"/>). The caller holds the graphics card's lock
/// (<see cref="IMusicPieceRunner"/>). Never tried on a real card by the app's tests (no GPU work in tests).
/// </summary>
public abstract class LocalMusicGeneratorBase(LocalMusicFamily family, LocalMusicStore store, LocalMusicLayout layout, PythonInstaller python,
    PythonLayout pythonLayout, IProcessRunner runner, ILogger logger) : IMusicGenerator
{
    /// <summary>The family.</summary>
    public LocalMusicFamily Family { get; } = family;

    /// <summary>Where the family's files are.</summary>
    protected LocalMusicLayout Layout { get; } = layout;

    /// <inheritdoc />
    public bool Handles(CatalogModel model)
    {
        ArgumentNullException.ThrowIfNull(model);
        return model.Provider == ModelProviders.Local && Variant(model.Id) is not null;
    }

    /// <inheritdoc />
    public bool Ready(CatalogModel model)
    {
        ArgumentNullException.ThrowIfNull(model);
        return Variant(model.Id) is { } variant && model.Provider == ModelProviders.Local && python.EnvironmentReady(Family.Environment)
            && store.SourceReady(Family) && store.ModelReady(Family, variant);
    }

    /// <summary>The family's model of the catalog id <paramref name="id"/>; null when it has none.</summary>
    public LocalMusicVariant? Variant(string id) => Family.Variants.FirstOrDefault(v => v.Id == id);

    /// <summary>The seconds a piece of <paramref name="variant"/> is made with (1 to its longest).</summary>
    public static int Seconds(LocalMusicVariant variant, int asked)
    {
        ArgumentNullException.ThrowIfNull(variant);
        return Math.Clamp(asked, 1, variant.MaxSeconds);
    }

    /// <summary>The run's arguments (the words' file when the family sings, then the family's own ones, then the model's own ones last).</summary>
    public IReadOnlyList<string> Arguments(LocalMusicVariant variant, string script, string prompt, int seconds, int seed, string output, string? lyrics = null)
    {
        ArgumentNullException.ThrowIfNull(variant);
        return
        [
            "-X", "utf8", "-I", script, "--model", Layout.ModelFolder(Family, variant), "--prompt-file", prompt,
            "--seconds", seconds.ToString(CultureInfo.InvariantCulture), "--seed", seed.ToString(CultureInfo.InvariantCulture), "--out", output,
            .. Family.Lyrics && lyrics is not null ? new[] { "--lyrics-file", lyrics } : [],
            .. FamilyArguments(variant),
            .. Layout.OptionArguments(Family, variant),
        ];
    }

    /// <summary>
    /// The variables of a run: offline (Hugging Face, transformers), the libraries' caches under the app's folder, no progress bars (their
    /// carriage returns would fill the log's tail), PyTorch's expandable memory segments (less fragmentation on a small card), the user's own
    /// Python variables emptied.
    /// </summary>
    public IReadOnlyDictionary<string, string> RunEnvironment() => new Dictionary<string, string>
    {
        ["HF_HOME"] = Path.Combine(Layout.Home, "hf"),
        ["HF_HUB_OFFLINE"] = "1",
        ["TRANSFORMERS_OFFLINE"] = "1",
        ["HF_HUB_DISABLE_TELEMETRY"] = "1",
        ["TORCH_HOME"] = Path.Combine(Layout.Home, "torch"),
        ["NUMBA_CACHE_DIR"] = Path.Combine(Layout.Home, "numba"),
        ["TQDM_DISABLE"] = "1",
        ["PYTORCH_CUDA_ALLOC_CONF"] = "expandable_segments:True",
        ["PYTHONIOENCODING"] = "utf-8",
        ["PYTHONPATH"] = string.Empty,
        ["PYTHONHOME"] = string.Empty,
    };

    /// <inheritdoc />
    public async Task<GeneratedMusic> GenerateAsync(MusicGenerationRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (Variant(request.Model.Model.Id) is not { } variant || !Ready(request.Model.Model))
        {
            throw new MusicGenerationException($"The local music model {request.Model.Model.Name} is not installed.") { Code = MusicFailure.Missing };
        }

        Directory.CreateDirectory(request.OutputFolder);
        var prompt = Path.Combine(request.OutputFolder, LocalMusicRunRules.PromptFile);
        await File.WriteAllTextAsync(prompt, request.Prompt.Trim(), cancellationToken).ConfigureAwait(false);
        string? lyrics = null;
        if (Family.Lyrics)
        {
            // An instrumental piece gets an empty file: the script then makes its own instrumental structure.
            lyrics = Path.Combine(request.OutputFolder, LocalMusicRunRules.LyricsFile);
            await File.WriteAllTextAsync(lyrics, request.Instrumental ? string.Empty : request.Lyrics.Trim(), cancellationToken).ConfigureAwait(false);
        }

        var output = Path.Combine(request.OutputFolder, LocalMusicRunRules.PieceFile);
        if (File.Exists(output))
        {
            File.Delete(output);
        }

        await PythonFiles.WriteScriptsAsync(pythonLayout.ScriptsFolder, cancellationToken).ConfigureAwait(false);
        Directory.CreateDirectory(Layout.Home);
        var seconds = Seconds(variant, request.Seconds);
        var start = new ProcessStart(pythonLayout.EnvironmentPython(Family.Environment.Key),
            Arguments(variant, Path.Combine(pythonLayout.ScriptsFolder, Family.Script), prompt, seconds, request.Seed, output, lyrics), request.OutputFolder)
        {
            Environment = RunEnvironment(),
        };
        logger.LogInformation("{Family}: {Model}, {Seconds} s, seed {Seed}, {Words}", Family.Name, variant.Id, seconds, request.Seed,
            lyrics is null ? "no words" : request.Instrumental || request.Lyrics.Trim().Length == 0 ? "instrumental" : "with words");
        var result = await runner.RunAsync(start, line =>
        {
            if (line.StartsWith(LocalMusicRunRules.StageMark, StringComparison.Ordinal))
            {
                logger.LogInformation("{Family}: {Stage}", Family.Name, line[LocalMusicRunRules.StageMark.Length..].Trim());
            }
        }, cancellationToken).ConfigureAwait(false);
        if (result.ExitCode != 0)
        {
            // The whole tail goes to the log (a Python traceback names the package and the line); the error names the script's own reason.
            logger.LogWarning("{Family} ended with {ExitCode}; its output: {Lines}", Family.Name, result.ExitCode,
                ChildOutput.Detail(result.LastLines, LocalMusicRunRules.LogLines, LocalMusicRunRules.LogCharacters));
            throw new MusicGenerationException($"{Family.Name} ended with {result.ExitCode}: {LocalMusicOutput.Reason(result.LastLines)}")
            {
                Code = LocalMusicOutput.OutOfMemory(result.LastLines) ? MusicFailure.Memory : MusicFailure.Process,
            };
        }

        return File.Exists(output) && new FileInfo(output).Length > 0
            ? new GeneratedMusic(output, 0, true)
            : throw new MusicGenerationException($"{Family.Name} wrote no piece.") { Code = MusicFailure.Missing };
    }

    /// <summary>The family's own arguments of a run of <paramref name="variant"/> (after the shared ones).</summary>
    protected abstract IReadOnlyList<string> FamilyArguments(LocalMusicVariant variant);
}
