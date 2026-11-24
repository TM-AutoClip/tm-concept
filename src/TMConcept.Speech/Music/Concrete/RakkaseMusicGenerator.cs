using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging;
using TMConcept.Core.Models;
using TMConcept.Core.Music;
using TMConcept.Core.Processes;

namespace TMConcept.Speech.Music;

/// <summary>
/// The local music model (0.1.46, C5): ACE-Step 1.5 through rakkase. A piece is one rakkase run (its README, read on 24 Nov 2026): a plan file
/// with one part (<c>caption</c> = the prompt, <c>sure</c> = seconds 10–600, <c>bpm</c>, <c>tohum</c> = seed; no <c>lyrics</c> = without
/// words), then <c>python -X utf8 rakkase.py uret --plan P --cikti O --ad tmc --panel-yok</c> with ACE-Step's own environment, in rakkase's
/// folder. The WAV it writes under <c>O</c> is the piece; rakkase's own archive copy is removed afterwards. Made for NVIDIA cards (measured on
/// a 4 GB GTX 1050 Ti, about 2.6 GB at its peak; 16 GB main memory); the caller holds the graphics card's lock. Never tried on Linux.
/// Words (lyrics) are not offered yet: the piece is always instrumental.
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

    /// <summary>The plan of one piece (rakkase's plan file).</summary>
    public static string Plan(MusicGenerationRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        var part = new JsonObject
        {
            ["ad"] = RakkasePins.RunName,
            ["caption"] = request.Prompt,
            ["sure"] = Math.Clamp(request.Seconds, MusicGenerationRules.MinSeconds, 600),
            ["tohum"] = request.Seed,
        };
        if (request.Bpm is { } bpm)
        {
            part["bpm"] = bpm;
        }

        return new JsonObject { ["not"] = "TM Concept", ["parcalar"] = new JsonArray(part) }.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
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
            throw new MusicGenerationException("The local music model is not installed.") { Code = "missing" };
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
                ["PYTHONPATH"] = string.Empty,
                ["PYTHONHOME"] = string.Empty,
            },
        };
        logger.LogInformation("rakkase: {Seconds} s, seed {Seed}", request.Seconds, request.Seed);
        var result = await runner.RunAsync(start, null, cancellationToken).ConfigureAwait(false);
        try
        {
            if (result.ExitCode != 0)
            {
                throw new MusicGenerationException($"rakkase ended with {result.ExitCode}: {ChildOutput.Summary(result.LastLines)}") { Code = "process" };
            }

            var wav = Directory.Exists(output)
                ? Directory.EnumerateFiles(output, "*.wav", SearchOption.AllDirectories).OrderByDescending(File.GetLastWriteTimeUtc).FirstOrDefault()
                : null;
            return wav is not null ? new GeneratedMusic(wav, 0, true) : throw new MusicGenerationException("rakkase wrote no piece.") { Code = "missing" };
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
