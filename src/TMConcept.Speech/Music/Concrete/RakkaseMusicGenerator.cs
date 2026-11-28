using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging;
using TMConcept.Core.Models;
using TMConcept.Core.Music;
using TMConcept.Core.Processes;

namespace TMConcept.Speech.Music;

/// <summary>
/// The local music model (0.1.46, C5): ACE-Step 1.5 through rakkase. A piece is one rakkase run (its README, read on 24 Nov 2026): a plan file
/// with one part (<c>ad</c> = the run's name, <c>caption</c> = the prompt, <c>sure</c> = seconds 10–600, <c>tohum</c> = seed, <c>bpm</c>
/// 30–300, <c>ton</c> = the key, empty = the model's choice, <c>olcu</c> = "4" as a string; all seven are required, see
/// <see cref="RakkasePlanRules"/>; no <c>lyrics</c> = without words), then <c>python -X utf8 rakkase.py uret --plan P --cikti O --ad tmc --panel-yok</c> with ACE-Step's own environment, in rakkase's
/// folder. The WAV it writes under <c>O</c> is the piece; rakkase's own archive copy is removed afterwards. Made for NVIDIA cards (measured on
/// a 4 GB GTX 1050 Ti, about 2.6 GB at its peak; 16 GB main memory); the caller holds the graphics card's lock. Never tried on Linux.
/// Words (lyrics) are not offered yet: the piece is always instrumental. A failed run writes its last lines to the log and its plan check's
/// findings into the error (<see cref="RakkaseOutput"/>). Progress (0.1.61): none — rakkase prints no step lines (it counts the DiT steps only
/// for its final record) and ACE-Step turns its own bars off when its output is not a terminal, so the screen shows an indeterminate bar with
/// the time so far, like an online model.
/// </summary>
public sealed class RakkaseMusicGenerator(RakkaseInstaller installer, RakkaseLayout layout, IProcessRunner runner, ILogger<RakkaseMusicGenerator> logger)
    : IMusicGenerator
{
    /// <summary>The catalog id of the model.</summary>
    public const string ModelId = "ace-step-1.5";

    /// <inheritdoc />
    public bool Handles(CatalogModel model)
    {
        ArgumentNullException.ThrowIfNull(model);
        return model.Provider == ModelProviders.Local && model.Id == ModelId;
    }

    /// <inheritdoc />
    public bool Ready(CatalogModel model) => Handles(model) && installer.EnvironmentReady && installer.WeightsReady;

    /// <summary>
    /// The plan of one piece (rakkase's plan file). Every field rakkase's check requires is written: a request without a tempo (the music page
    /// without a reference sound) asks for the medium tempo of the video's prompts.
    /// </summary>
    public static string Plan(MusicGenerationRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        var part = new JsonObject
        {
            ["ad"] = RakkasePins.RunName,
            ["caption"] = Caption(request.Prompt),
            ["sure"] = Math.Clamp(request.Seconds, MusicGenerationRules.MinSeconds, RakkasePlanRules.MaxSeconds),
            ["tohum"] = request.Seed,
            ["bpm"] = Math.Clamp(request.Bpm ?? MusicPromptBuilder.Bpm(MusicTags.Medium), RakkasePlanRules.MinBpm, RakkasePlanRules.MaxBpm),
            ["ton"] = RakkasePlanRules.AnyKey,
            ["olcu"] = RakkasePlanRules.Meter,
        };
        return new JsonObject { ["not"] = "TM Concept", ["parcalar"] = new JsonArray(part) }.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
    }

    /// <summary>
    /// The prompt as a plan's <c>caption</c>: at most <see cref="RakkasePlanRules.MaxCaptionCharacters"/>, cut at the last space before the
    /// limit (and without a trailing comma), so rakkase's 256-token check never stops a long Turkish prompt. The plan has no <c>lyrics</c>, so a
    /// cut-off "instrumental only" note changes nothing: the piece is without words anyway.
    /// </summary>
    public static string Caption(string prompt)
    {
        ArgumentNullException.ThrowIfNull(prompt);
        var text = prompt.Trim();
        if (text.Length <= RakkasePlanRules.MaxCaptionCharacters)
        {
            return text;
        }

        var cut = text[..RakkasePlanRules.MaxCaptionCharacters];
        var space = cut.LastIndexOf(' ');
        if (space >= RakkasePlanRules.MaxCaptionCharacters * RakkasePlanRules.WordCutShare)
        {
            cut = cut[..space];
        }

        return cut.TrimEnd(' ', ',', ';', '.');
    }

    /// <summary>The run's arguments.</summary>
    public static IReadOnlyList<string> Arguments(string script, string plan, string output) =>
        ["-X", "utf8", script, "uret", "--plan", plan, "--cikti", output, "--ad", RakkasePins.RunName, "--panel-yok"];

    /// <inheritdoc />
    public async Task<GeneratedMusic> GenerateAsync(MusicGenerationRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (!installer.EnvironmentReady || !installer.WeightsReady)
        {
            throw new MusicGenerationException("The local music model is not installed.") { Code = MusicFailure.Missing };
        }

        Directory.CreateDirectory(request.OutputFolder);
        var plan = Path.Combine(request.OutputFolder, "plan.json");
        await File.WriteAllTextAsync(plan, Plan(request), cancellationToken).ConfigureAwait(false);
        var output = Path.Combine(request.OutputFolder, "out");
        var before = Directory.Exists(layout.Productions) ? Directory.EnumerateDirectories(layout.Productions).ToHashSet(StringComparer.Ordinal) : [];
        Directory.CreateDirectory(layout.Home);
        var start = new ProcessStart(layout.Python, Arguments(layout.Script, plan, output), layout.RakkaseFolder)
        {
            Environment = new Dictionary<string, string>
            {
                ["HF_HOME"] = Path.Combine(layout.Home, "hf"),
                ["HF_HUB_OFFLINE"] = "1",
                ["PYTHONIOENCODING"] = "utf-8",
                ["PYTHONUNBUFFERED"] = "1",
                ["PYTHONPATH"] = string.Empty,
                ["PYTHONHOME"] = string.Empty,
            },
            Heartbeat = MusicGenerationRules.Heartbeat,
        };
        logger.LogInformation("rakkase: {Seconds} s, seed {Seed}", request.Seconds, request.Seed);
        var result = await runner.RunAsync(start, null, cancellationToken).ConfigureAwait(false);
        try
        {
            if (result.ExitCode != 0)
            {
                // The whole tail goes to the log (the plan check's lines say what was wrong); the error names the findings themselves.
                logger.LogWarning("rakkase ended with {ExitCode}; its output: {Lines}", result.ExitCode,
                    ChildOutput.Detail(result.LastLines, RakkasePlanRules.LogLines, RakkasePlanRules.LogCharacters));
                throw new MusicGenerationException($"rakkase ended with {result.ExitCode}: {RakkaseOutput.Reason(result.LastLines)}") { Code = MusicFailure.Process };
            }

            var wav = Directory.Exists(output)
                ? Directory.EnumerateFiles(output, "*.wav", SearchOption.AllDirectories).OrderByDescending(File.GetLastWriteTimeUtc).FirstOrDefault()
                : null;
            return wav is not null ? new GeneratedMusic(wav, 0, true) : throw new MusicGenerationException("rakkase wrote no piece.") { Code = MusicFailure.Missing };
        }
        finally
        {
            // rakkase keeps every piece in its own archive as well: the app keeps the piece with the video instead.
            if (Directory.Exists(layout.Productions))
            {
                foreach (var folder in Directory.EnumerateDirectories(layout.Productions).Where(f => !before.Contains(f)))
                {
                    try
                    {
                        Directory.Delete(folder, recursive: true);
                    }
                    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                    {
                        logger.LogWarning(ex, "rakkase's copy of the piece could not be removed");
                    }
                }
            }
        }
    }
}
