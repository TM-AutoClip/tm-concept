using Microsoft.Extensions.Logging;
using TMConcept.Core.Processes;
using TMConcept.Speech.Python;

namespace TMConcept.Speech.Music;

/// <summary>
/// YuE 1 (28 Nov 2026; <see cref="YueModels"/>): its script runs YuE-exllamav2's three stages from the pinned source as child processes of the
/// environment's Python (stage 1 writes the vocal and accompaniment tokens, stage 2 refines them, the post-processing decodes them with the
/// codec and its vocoders), two segments, the prompt as its genre tags and the user's words in sections (empty: an instrumental structure).
/// Each entry names its stage folders (<see cref="LocalMusicVariant.Options"/>). The quantised entry runs both stages through exllamav2 with the quantised caches of the 6 GB measurement (stage 1 Q4, stage 2 Q8); the 16
/// bit ones through transformers. The post-processing writes WAV wherever the code asks for MP3 (no FFmpeg needed); the mix is written as a
/// WAV cut to the asked seconds.
/// </summary>
public sealed class YueGenerator(LocalMusicStore store, LocalMusicLayout layout, PythonInstaller python, PythonLayout pythonLayout, IProcessRunner runner,
    ILogger<YueGenerator> logger) : LocalMusicGeneratorBase(LocalMusicFamilies.Yue, store, layout, python, pythonLayout, runner, logger)
{
    /// <inheritdoc />
    protected override IReadOnlyList<string> FamilyArguments(LocalMusicVariant variant)
    {
        ArgumentNullException.ThrowIfNull(variant);
        return ["--code", Layout.SourceFolder(Family), "--xcodec", Path.Combine(Layout.SharedFolder(Family, variant) ?? string.Empty, YueModels.CodecFolder)];
    }
}
