using System.Globalization;
using Microsoft.Extensions.Logging;
using TMConcept.Core.Music;
using TMConcept.Core.Processes;
using TMConcept.Speech.Python;

namespace TMConcept.Speech.Music;

/// <summary>
/// DiffRhythm (28 Nov 2026; <see cref="DiffRhythmModels"/>): its script imports DiffRhythm's model code and helpers from the family's pinned
/// source, encodes the prompt with MuQ-MuLan on the processor (on the card it would hold 2.65 GB) and frees it, builds the transformer in 16
/// bits on the processor before it moves to the card, makes the latents without words (so DiffRhythm's phonemizer, which needs espeak-ng, is
/// never built), frees the transformer before the VAE decodes in chunks, and writes a 44.1 kHz stereo WAV cut to the asked seconds. The two
/// code versions differ in the sampler's arguments (<see cref="CodeVersion"/>).
/// </summary>
public abstract class DiffRhythmGeneratorBase(LocalMusicFamily family, string codeVersion, LocalMusicStore store, LocalMusicLayout layout, PythonInstaller python,
    PythonLayout pythonLayout, IProcessRunner runner, IMusicProgressParser progress, ILogger logger) : LocalMusicGeneratorBase(family, store, layout, python, pythonLayout, runner, progress, logger)
{
    /// <summary>The DiffRhythm code version the script follows (<see cref="DiffRhythmPins.CurrentVersion"/> or <see cref="DiffRhythmPins.LegacyVersion"/>).</summary>
    public string CodeVersion { get; } = codeVersion;

    /// <summary>
    /// The frames a model's piece is made with: <see cref="DiffRhythmModels.BaseFrames"/> for a 95 s model, otherwise
    /// <see cref="DiffRhythmModels.FullFrames"/>.
    /// </summary>
    public static int Frames(LocalMusicVariant variant)
    {
        ArgumentNullException.ThrowIfNull(variant);
        return variant.MaxSeconds <= DiffRhythmModels.BaseSeconds ? DiffRhythmModels.BaseFrames : DiffRhythmModels.FullFrames;
    }

    /// <inheritdoc />
    protected override IReadOnlyList<string> FamilyArguments(LocalMusicVariant variant)
    {
        ArgumentNullException.ThrowIfNull(variant);
        return
        [
            "--code", Layout.SourceFolder(Family), "--shared", Layout.SharedFolder(Family, variant) ?? string.Empty,
            "--frames", Frames(variant).ToString(CultureInfo.InvariantCulture), "--code-version", CodeVersion,
        ];
    }
}
