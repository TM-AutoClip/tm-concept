using Microsoft.Extensions.Logging;
using TMConcept.Core.Processes;
using TMConcept.Speech.Python;

namespace TMConcept.Speech.Music;

/// <summary>DiffRhythm 1.2 (28 Nov 2026; <see cref="DiffRhythmGeneratorBase"/>, with DiffRhythm's newest code).</summary>
public sealed class DiffRhythmGenerator(LocalMusicStore store, LocalMusicLayout layout, PythonInstaller python, PythonLayout pythonLayout, IProcessRunner runner,
    ILogger<DiffRhythmGenerator> logger)
    : DiffRhythmGeneratorBase(LocalMusicFamilies.DiffRhythm, DiffRhythmPins.CurrentVersion, store, layout, python, pythonLayout, runner, logger);
