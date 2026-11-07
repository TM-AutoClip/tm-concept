using System.Globalization;
using Microsoft.Extensions.Logging;
using TMConcept.Core.Profiles;
using TMConcept.Core.Speech;
using TMConcept.Core.Tools;

namespace TMConcept.Speech.Speech;

/// <summary>
/// Pocket TTS (Kyutai, about 100 M parameters) through sherpa-onnx: English only (the sherpa-onnx package is the English model), on the
/// processor, 24 kHz, and it speaks with a voice sample: the voices are the user's own samples (<see cref="VoiceSampleLibrary"/>; none =
/// nothing to choose, the step says where to add one). The sample is written to a work file for each scene and removed after it. The model
/// has no speed setting (the speed of the step is not used). Weights CC-BY-4.0: attribution in THIRD-PARTY-NOTICES and the step's note.
/// Numbers are left as digits (its tokenizer reads them).
/// </summary>
public sealed class PocketSpeechEngine(SpeechModelInstaller installer, IToolLocator tools, SpeechWorkerClient worker, VoiceSampleLibrary samples,
    ILogger<PocketSpeechEngine> logger)
    : SherpaSpeechEngineBase(installer, tools, worker, SpeechModels.Pocket, logger)
{
    /// <summary>Flow steps per frame (the model's own default: one step).</summary>
    public const int Steps = 1;

    /// <inheritdoc />
    public override string Code => SpeechEngines.Pocket;

    /// <inheritdoc />
    public override string Name => "Pocket TTS";

    /// <inheritdoc />
    public override bool Supports(string language) => language == ContentLanguages.English;

    /// <inheritdoc />
    public override IReadOnlyList<SpeechVoice> Voices(string language) =>
        samples.Samples.Select(s => new SpeechVoice(VoiceSampleLibrary.VoiceId(s.Id),
            s.Name + " (" + (s.DurationMs / 1000.0).ToString("0", CultureInfo.InvariantCulture) + " s)")).ToList();

    /// <inheritdoc />
    protected override SpeechWorkerRequest Request(SpeechRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        return new SpeechWorkerRequest(request.Text, ContentLanguages.English, 0, 1, Steps, Path.GetFullPath(request.OutputWav));
    }

    /// <inheritdoc />
    protected override async Task<SpeechWorkerRequest> RequestAsync(SpeechRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var id = VoiceSampleLibrary.SampleOf(request.Voice)
            ?? throw new SpeechException("Pocket TTS speaks with your own voice: add a voice sample in Settings → Voice and pronunciation and choose it.")
            {
                Code = "voice",
            };
        var reference = await samples.WriteAsync(id, cancellationToken).ConfigureAwait(false);
        return Request(request) with { ReferenceWav = reference };
    }

    /// <inheritdoc />
    protected override void Done(SpeechWorkerRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.ReferenceWav is { Length: > 0 } reference)
        {
            samples.Delete(reference);
        }
    }
}
