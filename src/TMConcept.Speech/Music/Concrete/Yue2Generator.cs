using Microsoft.Extensions.Logging;
using TMConcept.Core.Music;
using TMConcept.Core.Processes;
using TMConcept.Speech.Python;

namespace TMConcept.Speech.Music;

/// <summary>
/// YuE2 (28 Nov 2026; <see cref="YueModels"/>): its script imports YuE2 from the pinned source, loads the model and its VAE from their folders
/// offline (their own code files are pinned with them), makes the song with the full chain of thought, the prompt as its style and the user's
/// words (empty: instrumental), and writes the 48 kHz stereo audio as a WAV cut to the asked seconds. The low-memory entry runs with
/// <c>low_vram</c> and an 8 GiB budget (YuE2 keeps 2 GiB of it in reserve), the other with the 24 GiB budget its model card names (the
/// entries' own options, <see cref="LocalMusicVariant.Options"/>).
/// </summary>
public sealed class Yue2Generator(LocalMusicStore store, LocalMusicLayout layout, PythonInstaller python, PythonLayout pythonLayout, IProcessRunner runner,
    IMusicProgressParser progress, ILogger<Yue2Generator> logger) : LocalMusicGeneratorBase(LocalMusicFamilies.Yue2, store, layout, python, pythonLayout, runner, progress, logger)
{
    /// <inheritdoc />
    protected override IReadOnlyList<string> FamilyArguments(LocalMusicVariant variant)
    {
        ArgumentNullException.ThrowIfNull(variant);
        return ["--code", Layout.SourceFolder(Family), "--weights", Layout.SharedFolder(Family, variant) ?? string.Empty];
    }
}
