using Microsoft.Extensions.Logging;
using TMConcept.Core.Components;
using TMConcept.Core.Paths;
using TMConcept.Core.Profiles;
using TMConcept.Core.Speech;
using TMConcept.Core.Tools;
using TMConcept.Speech.AhenkCep;

namespace TMConcept.Speech.Speech;

/// <summary>
/// Ahenk Cep (ozgurguler/ahenk-cep-tr, CC BY 4.0; trained on EMA Lightning): a small Turkish-only voice, one designed voice in seven speaking styles
/// (the voices of the step), 48 kHz. Its four ONNX graphs (<see cref="AhenkCepModel.Files"/>, about 13 MB) are downloaded one by one into
/// <c>&lt;tools&gt;/speech/ahenkcepvoice/&lt;version&gt;/</c> and run in the speech worker with the ONNX Runtime of the sherpa-onnx
/// libraries, on the processor (the graphics card is never used). It reads only the spoken form in lower case: every Turkish narration goes
/// through the Turkish normalizer first (numbers, dates, abbreviations, foreign names). The licence asks for attribution (the release's
/// THIRD-PARTY-NOTICES) and for saying that the voice is AI-generated.
/// </summary>
public sealed class AhenkCepSpeechEngine(IAppPaths paths, IArchiveDownloader downloader, IToolLocator tools, SpeechWorkerClient worker,
    ILogger<AhenkCepSpeechEngine> logger)
    : LocalSpeechEngineBase(tools, worker, logger)
{
    /// <summary>The display name.</summary>
    public const string DisplayName = "Ahenk Cep";

    /// <summary>The offered styles in the order of the list (id = the manifest's style name).</summary>
    private static readonly (string Id, string Label)[] StyleLabels =
    [
        ("neutral", "Düz (neutral)"), ("calm", "Sakin (calm)"), ("cheerful", "Neşeli (cheerful)"), ("empathetic", "Şefkatli (empathetic)"),
        ("sad", "Hüzünlü (sad)"), ("angry", "Kızgın (angry)"), ("surprised", "Şaşkın (surprised)"),
    ];

    /// <inheritdoc />
    public override string Code => SpeechEngines.AhenkCep;

    /// <inheritdoc />
    public override string Name => DisplayName;

    /// <inheritdoc />
    public override string InstallFolder => Path.Combine(paths.ToolsDirectory, "speech", nameof(ToolKind.AhenkCepVoice).ToLowerInvariant());

    /// <inheritdoc />
    public override string ModelFolder => Path.Combine(InstallFolder, AhenkCepModel.Version);

    /// <summary>Files that are not there yet.</summary>
    public IReadOnlyList<ToolArchive> Missing => AhenkCepModel.Files.Where(f => !File.Exists(Path.Combine(ModelFolder, f.ExecutablePath))).ToList();

    /// <inheritdoc />
    public override bool ModelInstalled => Missing.Count == 0;

    /// <summary>Processor threads: the flow takes 16 steps per piece, so up to 8 (still two cores left free).</summary>
    protected override int Threads => Math.Clamp(Environment.ProcessorCount - 2, 1, 8);

    /// <inheritdoc />
    public override IReadOnlyList<ComponentDownload> Downloads()
    {
        var missing = Missing;
        return missing.Count == 0
            ? []
            : [new ComponentDownload(Name + " " + AhenkCepModel.Version, missing[0].Url.Host, missing.Sum(f => f.ApproximateBytes))];
    }

    /// <inheritdoc />
    public override bool Supports(string language) => language == ContentLanguages.Turkish;

    /// <inheritdoc />
    public override IReadOnlyList<SpeechVoice> Voices(string language) =>
        StyleLabels.Select(s => new SpeechVoice(s.Id, s.Label)).ToList();

    /// <inheritdoc />
    public override async Task InstallAsync(bool agreed, IProgress<ToolProgress>? progress, CancellationToken cancellationToken)
    {
        if (!agreed)
        {
            throw new InvalidOperationException("A voice model is installed only after the user agreed.");
        }

        var missing = Missing;
        var total = Math.Max(1, missing.Sum(f => f.ApproximateBytes));
        var done = 0L;
        foreach (var file in missing)
        {
            var target = Path.Combine(ModelFolder, file.ExecutablePath);
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            var before = done;
            var inner = new Progress<ToolProgress>(p => progress?.Report(new ToolProgress((before + p.Share * file.ApproximateBytes) / total, p.Text)));
            await downloader.DownloadAsync(file, target, inner, cancellationToken).ConfigureAwait(false);
            done += file.ApproximateBytes;
            logger.LogInformation("Ahenk Cep: {File} downloaded and verified", file.ExecutablePath);
        }
    }

    /// <summary>The style index of a voice id (an unknown one: the neutral style).</summary>
    public static int StyleOf(string voice)
    {
        var index = AhenkCepModel.Styles.ToList().IndexOf(voice ?? string.Empty);
        return index < 0 || !StyleLabels.Any(s => s.Id == voice) ? 0 : index;
    }

    /// <inheritdoc />
    protected override SpeechWorkerRequest Request(SpeechRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        var speed = (float)Math.Clamp(request.Speed, ProfileLimits.SpeedMin, ProfileLimits.SpeedMax);
        return new SpeechWorkerRequest(request.Text, request.Language, StyleOf(request.Voice), speed, AhenkCepModel.Steps,
            Path.GetFullPath(request.OutputWav));
    }
}
