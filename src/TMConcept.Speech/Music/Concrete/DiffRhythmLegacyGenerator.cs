using Microsoft.Extensions.Logging;
using TMConcept.Core.Music;
using TMConcept.Core.Processes;
using TMConcept.Speech.Python;

namespace TMConcept.Speech.Music;

/// <summary>DiffRhythm 1.0 (28 Nov 2026; <see cref="DiffRhythmGeneratorBase"/>, with DiffRhythm's code before 1.2).</summary>
public sealed class DiffRhythmLegacyGenerator(LocalMusicStore store, LocalMusicLayout layout, PythonInstaller python, PythonLayout pythonLayout, IProcessRunner runner,
    IMusicProgressParser progress, ILogger<DiffRhythmLegacyGenerator> logger)
    : DiffRhythmGeneratorBase(LocalMusicFamilies.DiffRhythmLegacy, DiffRhythmPins.LegacyVersion, store, layout, python, pythonLayout, runner, progress, logger);
