using Microsoft.Extensions.Logging;
using TMConcept.Core.Components;
using TMConcept.Core.Paths;
using TMConcept.Core.Speech;
using TMConcept.Core.Tools;
using TMConcept.Speech.Chatterbox;

namespace TMConcept.Speech.Speech;

/// <summary>
/// Chatterbox Multilingual V3 (Resemble AI, MIT): Turkish and nine more output languages (<see cref="ChatterboxModel.Languages"/>), speaking
/// with the user's own voice sample (<see cref="VoiceSampleLibrary"/>; none = nothing to choose, the step says where to add one; no voice
/// comes with the app). Its ten files (<see cref="ChatterboxModel.Files"/>, about 1.56 GB) are downloaded one by one into
/// <c>&lt;tools&gt;/speech/chatterboxvoice/&lt;version&gt;/</c> (a file is there only after its SHA-256 matched) and run in the speech worker
/// with the ONNX Runtime of the sherpa-onnx libraries, on the processor: that build has no graphics card support, so the card is never used
/// and its memory does not matter; it is much slower than Supertonic (measured on 8 Nov 2026, see the release notes). No speed setting.
/// </summary>
public sealed class ChatterboxSpeechEngine(IAppPaths paths, IArchiveDownloader downloader, IToolLocator tools, SpeechWorkerClient worker,
    VoiceSampleLibrary samples, ILogger<ChatterboxSpeechEngine> logger)
    : LocalSpeechEngineBase(tools, worker, logger)
{
    /// <summary>The display name.</summary>
    public const string DisplayName = "Chatterbox Multilingual V3";

    /// <inheritdoc />
    public override string Code => SpeechEngines.Chatterbox;

    /// <inheritdoc />
    public override string Name => DisplayName;

    /// <inheritdoc />
    public override string InstallFolder => Path.Combine(paths.ToolsDirectory, "speech", nameof(ToolKind.ChatterboxVoice).ToLowerInvariant());

    /// <inheritdoc />
    public override string ModelFolder => Path.Combine(InstallFolder, ChatterboxModel.Version);

    /// <summary>Files that are not there yet.</summary>
    public IReadOnlyList<ToolArchive> Missing => ChatterboxModel.Files.Where(f => !File.Exists(Path.Combine(ModelFolder, f.ExecutablePath))).ToList();

    /// <inheritdoc />
    public override bool ModelInstalled => Missing.Count == 0;

    /// <summary>
    /// Processor threads: the language model takes one step per 40 ms of speech, so it gets more threads than the small voices (still two
    /// cores left free, at most 8).
    /// </summary>
    protected override int Threads => Math.Clamp(Environment.ProcessorCount - 2, 1, 8);

    /// <inheritdoc />
    public override IReadOnlyList<ComponentDownload> Downloads()
    {
        var missing = Missing;
        return missing.Count == 0
            ? []
            : [new ComponentDownload(Name + " " + ChatterboxModel.Version, missing[0].Url.Host, missing.Sum(f => f.ApproximateBytes))];
    }

    /// <inheritdoc />
    public override bool Supports(string language) => ChatterboxModel.Languages.Contains(language);

    /// <inheritdoc />
    public override IReadOnlyList<SpeechVoice> Voices(string language) => samples.Voices();

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
            logger.LogInformation("Chatterbox: {File} downloaded and verified", file.ExecutablePath);
        }
    }

    /// <inheritdoc />
    protected override SpeechWorkerRequest Request(SpeechRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        return new SpeechWorkerRequest(request.Text, request.Language, 0, 1, 0, Path.GetFullPath(request.OutputWav));
    }

    /// <inheritdoc />
    protected override async Task<SpeechWorkerRequest> RequestAsync(SpeechRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var reference = await samples.WriteForAsync(request.Voice, Name, cancellationToken).ConfigureAwait(false);
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
