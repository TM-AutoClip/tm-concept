using System.Globalization;
using Microsoft.Extensions.Logging;
using TMConcept.Core.Music;
using TMConcept.Core.Paths;
using TMConcept.Core.Processes;
using TMConcept.Core.Tools;
using TMConcept.Engines.Engines;
using TMConcept.Engines.Tools;

namespace TMConcept.Engines.Music;

/// <summary>
/// <see cref="IMidiRenderer"/> with spessasynth_core (0.1.55, Apache-2.0, pure JavaScript): its pinned npm packages
/// (<c>Packages/music-synth</c>: package.json, package-lock.json and the app's <c>render.mjs</c>, installed with <c>npm ci --ignore-scripts</c>
/// like an engine's, <see cref="EnginePackage"/>) run with the app's Node.js and play the MIDI file with the MuseScore General SoundFont
/// (<see cref="ISoundFontStore"/>) on the processor: no browser, no network, no graphics card. A failed run names its reason.
/// </summary>
public sealed class SpessaSynthRenderer(IAppPaths paths, ToolInstaller tools, IProcessRunner runner, ISoundFontStore soundFonts, ILogger<SpessaSynthRenderer> logger)
    : IMidiRenderer
{
    /// <summary>Its npm packages and script.</summary>
    public EnginePackage Package { get; } = new(MusicSynthRules.PackageName, paths, tools, runner, logger);

    /// <summary>Whether its packages are installed for today's lock (Node.js not looked at).</summary>
    public bool PackagesReady => Package.Ready(MusicSynthRules.InstalledFile);

    /// <inheritdoc />
    public bool Ready => tools.Status(ToolKind.Node).Ready && PackagesReady && soundFonts.Ready;

    /// <summary>The script's arguments.</summary>
    public static IReadOnlyList<string> Arguments(string script, string soundFont, string midi, string wav) =>
    [
        script, "--soundfont", soundFont, "--midi", midi, "--out", wav,
        "--rate", MusicSynthRules.SampleRate.ToString(CultureInfo.InvariantCulture), "--tail", MusicSynthRules.TailSeconds,
    ];

    /// <inheritdoc />
    public async Task RenderAsync(string midiFile, string wavFile, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrEmpty(midiFile);
        ArgumentException.ThrowIfNullOrEmpty(wavFile);
        if (!Ready)
        {
            throw new MusicGenerationException("The synthesizer of the composed music is not installed.") { Code = MusicFailure.Missing };
        }

        ProcessResult result;
        try
        {
            result = await Package.RunNodeAsync(Arguments(Path.Combine(Package.Root, MusicSynthRules.ScriptName), soundFonts.File, midiFile, wavFile),
                line => logger.LogDebug("music-synth: {Line}", EnginePackage.Short(line)), cancellationToken).ConfigureAwait(false);
        }
        catch (ToolInstallException ex)
        {
            throw new MusicGenerationException(ex.Message, ex) { Code = MusicFailure.Missing };
        }

        if (result.ExitCode != 0)
        {
            logger.LogWarning("music-synth ended with {ExitCode}: {Lines}", result.ExitCode, ChildOutput.Detail(result.LastLines));
            throw new MusicGenerationException($"The synthesizer ended with {result.ExitCode}: {ChildOutput.Summary(result.LastLines)}") { Code = MusicFailure.Process };
        }

        if (!File.Exists(wavFile))
        {
            throw new MusicGenerationException("The synthesizer wrote no piece.") { Code = MusicFailure.Missing };
        }
    }
}
