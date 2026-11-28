using Microsoft.Extensions.Logging;
using TMConcept.Core.Music;
using TMConcept.Core.Processes;
using TMConcept.Speech.Python;

namespace TMConcept.Speech.Music;

/// <summary>
/// MusicGen (28 Nov 2026; <see cref="MusicGenModels"/>): its script loads the model with transformers (bfloat16 on cards that have it,
/// otherwise float16 with the T5 text encoder kept in float32, which overflows in float16), makes the piece with sampling and the checkpoint's
/// own guidance, and writes a 32 kHz WAV (two channels for the stereo models). The melody models run from the text alone.
/// </summary>
public sealed class MusicGenGenerator(LocalMusicStore store, LocalMusicLayout layout, PythonInstaller python, PythonLayout pythonLayout, IProcessRunner runner,
    IMusicProgressParser progress, ILogger<MusicGenGenerator> logger) : LocalMusicGeneratorBase(LocalMusicFamilies.MusicGen, store, layout, python, pythonLayout, runner, progress, logger)
{
    /// <inheritdoc />
    protected override IReadOnlyList<string> FamilyArguments(LocalMusicVariant variant) => ["--guidance", LocalMusicRunRules.MusicGenGuidance];
}
