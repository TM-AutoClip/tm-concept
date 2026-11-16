using System.Globalization;
using Microsoft.Extensions.Logging;
using TMConcept.Core.Hardware;
using TMConcept.Core.Models;
using TMConcept.Core.Speech;
using TMConcept.Core.Tools;

namespace TMConcept.Speech.Python;

/// <summary>
/// A Python voice (<see cref="PythonVoices"/>) as a narration engine: ready when the pinned Python, its environment (for this computer's
/// PyTorch build) and its model files are installed (each a component, installed after one confirmation that lists the large downloads).
/// It speaks in the Python worker (<see cref="PythonWorkerClient"/>), on an NVIDIA card through CUDA when there is one (<see cref="Device"/>;
/// the narration step holds the card's lease, so no other local model is on it), otherwise on the processor. The worker checks the card's
/// free memory itself and uses the processor when it is short. A voice that copies a voice gets the user's sample as a work file per request.
/// </summary>
public sealed class PythonSpeechEngine(PythonVoice voice, PythonLayout layout, PythonInstaller installer, PythonModelStore models, PythonWorkerClient worker,
    VoiceSampleLibrary samples, IGpuInfo gpu, ILogger<PythonSpeechEngine> logger) : ISpeechEngine
{
    /// <summary>The voice.</summary>
    public PythonVoice Voice { get; } = voice;

    /// <summary>Its environment.</summary>
    public PythonEnvironment Environment { get; } = PythonEnvironments.All.First(e => e.Key == voice.Environment);

    /// <inheritdoc />
    public string Code => Voice.Code;

    /// <inheritdoc />
    public string Name => Voice.Name;

    /// <inheritdoc />
    public bool IsLocal => true;

    /// <summary>Where it runs on this computer: the NVIDIA card (CUDA) or the processor.</summary>
    public ModelDevice Device => Voice.OnCard && SpeechDevices.CudaCard(gpu.Detect()) is not null ? ModelDevice.Gpu : ModelDevice.Cpu;

    /// <summary>The voice whose model folder this one builds on (Ahenk on VoxCPM2); null when none.</summary>
    public PythonVoice? BaseVoice => Voice.BaseVoice is { } code ? PythonVoices.All.First(v => v.Code == code) : null;

    /// <summary>Whether its model files (and those of <see cref="BaseVoice"/>) are installed.</summary>
    public bool ModelInstalled => models.Installed(Voice) && (BaseVoice is not { } basis || models.Installed(basis));

    /// <inheritdoc />
    public bool Ready => ModelInstalled && installer.PythonReady && installer.EnvironmentReady(Environment);

    /// <summary>Processor threads (two cores left free, at most 8).</summary>
    public static int Threads => Math.Clamp(System.Environment.ProcessorCount - 2, 1, 8);

    /// <inheritdoc />
    public bool Supports(string language) => Voice.Languages.Contains(language);

    /// <inheritdoc />
    public IReadOnlyList<SpeechVoice> Voices(string language) => Voice.CopiesVoice ? [.. Voice.Voices, .. samples.Voices()] : Voice.Voices;

    /// <inheritdoc />
    public async Task InstallAsync(bool agreed, IProgress<ToolProgress>? progress, CancellationToken cancellationToken)
    {
        if (!agreed)
        {
            throw new InvalidOperationException("A voice is installed only after the user agreed.");
        }

        if (!installer.PythonReady)
        {
            await installer.InstallPythonAsync(progress, cancellationToken).ConfigureAwait(false);
        }

        if (!installer.EnvironmentReady(Environment))
        {
            await installer.InstallEnvironmentAsync(Environment, progress, cancellationToken).ConfigureAwait(false);
        }

        if (BaseVoice is { } basis)
        {
            await models.InstallAsync(basis, progress, cancellationToken).ConfigureAwait(false);
        }

        await models.InstallAsync(Voice, progress, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<SpeechClip> SynthesizeAsync(SpeechRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (!Ready)
        {
            throw new SpeechException($"{Name} is not installed. It is installed when a process that uses it starts (after a confirmation).") { Code = "missing" };
        }

        await PythonFiles.WriteScriptsAsync(layout.ScriptsFolder, cancellationToken).ConfigureAwait(false);
        var reference = Voice.CopiesVoice && VoiceSampleLibrary.SampleOf(request.Voice) is not null
            ? await samples.WriteForAsync(request.Voice, Name, cancellationToken).ConfigureAwait(false)
            : string.Empty;
        PythonWorkerReply reply;
        try
        {
            var output = Path.GetFullPath(request.OutputWav);
            reply = await worker.SpeakAsync(Start(Device), new PythonWorkerRequest("synth")
            {
                Text = request.Text,
                Language = request.Language,
                Voice = reference.Length > 0 ? string.Empty : request.Voice,
                Reference = reference,
                Speed = request.Speed,
                Out = output,
            }, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            if (reference.Length > 0)
            {
                samples.Delete(reference);
            }
        }

        if (!reply.Ok || !File.Exists(request.OutputWav))
        {
            logger.LogError("{Voice} failed ({Code}): {Error}", Name, reply.Code, reply.Error);
            throw new SpeechException($"{Name} failed: {(reply.Error.Length > 0 ? reply.Error : "no sound file was written")}") { Code = "engine" };
        }

        return new SpeechClip(request.OutputWav, WaveFile.DurationMs(request.OutputWav), request.Text.Length, 0, Code);
    }

    /// <summary>Ends its worker (when it runs for this voice); never throws.</summary>
    public Task UnloadAsync(CancellationToken cancellationToken) => worker.StopAsync(Code, cancellationToken);

    /// <summary>How its worker starts on <paramref name="device"/>.</summary>
    public PythonWorkerStart Start(ModelDevice device)
    {
        var cuda = device == ModelDevice.Gpu;
        var options = new Dictionary<string, string>(Voice.Options);
        if (BaseVoice is { } basis)
        {
            options["base"] = models.Folder(basis);
        }

        var load = new PythonWorkerRequest("load")
        {
            Adapter = Voice.Adapter,
            Model = models.Folder(Voice),
            Device = cuda ? "cuda" : "cpu",
            NeedMb = cuda ? Voice.NeedMegabytes : 0,
            Threads = Threads,
            Options = options,
        };
        return new PythonWorkerStart(Code + "|" + load.Device, layout.EnvironmentPython(Environment.Key),
            ["-I", "-u", Path.Combine(layout.ScriptsFolder, PythonFiles.Worker)], layout.ScriptsFolder, WorkerEnvironment(Voice, layout, cuda, Threads), load);
    }

    /// <summary>
    /// The variables of a worker: nothing is downloaded (Hugging Face offline, the model folder holds everything), every library writes into the
    /// tools folder (not the user's home), the processor gets <paramref name="threads"/> threads, UTF-8 everywhere, no telemetry. XTTS-v2's package asks once
    /// for its licence on the console; the user accepted it in the app before the download, so it is told so.
    /// </summary>
    public static IReadOnlyDictionary<string, string> WorkerEnvironment(PythonVoice voice, PythonLayout layout, bool cuda, int threads)
    {
        ArgumentNullException.ThrowIfNull(voice);
        ArgumentNullException.ThrowIfNull(layout);
        var home = layout.HomeFolder;
        var count = threads.ToString(CultureInfo.InvariantCulture);
        var variables = new Dictionary<string, string>
        {
            ["HF_HOME"] = Path.Combine(home, "huggingface"),
            ["HF_HUB_OFFLINE"] = "1",
            ["TRANSFORMERS_OFFLINE"] = "1",
            ["HF_DATASETS_OFFLINE"] = "1",
            ["HF_HUB_DISABLE_TELEMETRY"] = "1",
            ["DO_NOT_TRACK"] = "1",
            ["TORCH_HOME"] = Path.Combine(home, "torch"),
            ["TTS_HOME"] = Path.Combine(home, "tts"),
            ["MPLCONFIGDIR"] = Path.Combine(home, "matplotlib"),
            ["NUMBA_CACHE_DIR"] = Path.Combine(home, "numba"),
            ["XDG_CACHE_HOME"] = Path.Combine(home, "cache"),
            ["MODELSCOPE_CACHE"] = Path.Combine(home, "modelscope"),
            ["WANDB_MODE"] = "disabled",
            ["GRADIO_ANALYTICS_ENABLED"] = "False",
            ["TOKENIZERS_PARALLELISM"] = "false",
            ["PYTHONUTF8"] = "1",
            ["PYTHONIOENCODING"] = "utf-8",
            ["PYTHONDONTWRITEBYTECODE"] = "1",
            ["OMP_NUM_THREADS"] = count,
            ["MKL_NUM_THREADS"] = count,
        };
        if (!cuda)
        {
            // On the processor the card is not even looked at (CUDA sees no device), so it stays free for the next model.
            variables["CUDA_VISIBLE_DEVICES"] = string.Empty;
        }

        if (voice.Code == SpeechEngines.XttsV2)
        {
            variables["COQUI_TOS_AGREED"] = "1";
        }

        return variables;
    }
}
