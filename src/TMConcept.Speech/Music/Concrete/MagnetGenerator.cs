using Microsoft.Extensions.Logging;
using TMConcept.Core.Music;
using TMConcept.Core.Processes;
using TMConcept.Speech.Python;

namespace TMConcept.Speech.Music;

/// <summary>
/// MAGNeT (28 Nov 2026; <see cref="MagnetModels"/>): its script puts the pinned audiocraft source first on its module path, points audiocraft's
/// T5 names (<c>t5-base</c>, <c>t5-large</c>) at the downloaded text encoder folder, loads the checkpoint from its folder on the card (audiocraft's
/// own float16 language model with xformers attention), makes the checkpoint's fixed length with its default decoding, cuts it to the asked
/// seconds, normalises its loudness and writes a WAV (32 kHz for music, 16 kHz for sound effects).
/// </summary>
public sealed class MagnetGenerator(LocalMusicStore store, LocalMusicLayout layout, PythonInstaller python, PythonLayout pythonLayout, IProcessRunner runner,
    IMusicProgressParser progress, ILogger<MagnetGenerator> logger) : LocalMusicGeneratorBase(LocalMusicFamilies.Magnet, store, layout, python, pythonLayout, runner, progress, logger)
{
    /// <inheritdoc />
    protected override IReadOnlyList<string> FamilyArguments(LocalMusicVariant variant)
    {
        ArgumentNullException.ThrowIfNull(variant);
        return ["--code", Layout.SourceFolder(Family), "--t5-name", variant.SharedFolder, "--t5", Layout.SharedFolder(Family, variant) ?? string.Empty];
    }
}
