using Microsoft.Extensions.Logging;
using TMConcept.Core.Processes;
using TMConcept.Speech.Python;

namespace TMConcept.Speech.Music;

/// <summary>
/// SongGeneration (28 Nov 2026; <see cref="SongGenerationModels"/>): its script works in the shared runtime folder (the code reads
/// <c>ckpt/</c> and <c>third_party/</c> from where it runs), imports Tencent's code from the pinned source, turns the user's words into
/// SongGeneration's sections (without words it makes pure music, its <c>bgm</c> type), always in the low-memory mode (SongGeneration 2
/// medium would otherwise load whole onto the card), without flash-attn (no Windows build), with the prompt as its description and no prompt
/// audio, its code version from the model's own option (<see cref="LocalMusicVariant.Options"/>), seeds every random source (upstream seeds from the clock), and writes the 48 kHz piece it makes as a WAV cut to the asked seconds.
/// </summary>
public sealed class SongGenerationGenerator(LocalMusicStore store, LocalMusicLayout layout, PythonInstaller python, PythonLayout pythonLayout, IProcessRunner runner,
    ILogger<SongGenerationGenerator> logger) : LocalMusicGeneratorBase(LocalMusicFamilies.SongGeneration, store, layout, python, pythonLayout, runner, logger)
{
    /// <inheritdoc />
    protected override IReadOnlyList<string> FamilyArguments(LocalMusicVariant variant)
    {
        ArgumentNullException.ThrowIfNull(variant);
        return ["--code", Layout.SourceFolder(Family), "--runtime", Layout.SharedFolder(Family, variant) ?? string.Empty];
    }
}
