using Microsoft.Extensions.Logging;
using TMConcept.Core.Processes;
using TMConcept.Speech.Python;

namespace TMConcept.Speech.Music;

/// <summary>
/// AudioLDM 2 (28 Nov 2026; <see cref="AudioLdm2Models"/>): its script loads the diffusers pipeline in float16 from the model's folder, moves
/// it whole onto a card of 8 GB or more and otherwise lets accelerate keep only the working part there (the model offload: about 2-3 GB at
/// its peak), switches to the DPM-Solver++ scheduler, and writes a 16 kHz mono WAV.
/// </summary>
public sealed class AudioLdm2Generator(LocalMusicStore store, LocalMusicLayout layout, PythonInstaller python, PythonLayout pythonLayout, IProcessRunner runner,
    ILogger<AudioLdm2Generator> logger) : LocalMusicGeneratorBase(LocalMusicFamilies.AudioLdm2, store, layout, python, pythonLayout, runner, logger)
{
    /// <inheritdoc />
    protected override IReadOnlyList<string> FamilyArguments(LocalMusicVariant variant) =>
        ["--steps", LocalMusicRunRules.AudioLdm2Steps, "--guidance", LocalMusicRunRules.AudioLdm2Guidance];
}
