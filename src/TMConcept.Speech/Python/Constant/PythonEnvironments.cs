namespace TMConcept.Speech.Python;

/// <summary>
/// The Python environments (16 Nov 2026). Every one pins torch 2.8.0 (CUDA 12.8 or processor build). VoxCPM2, Trendyol-TTS and Ahenk share
/// one environment (the same voxcpm package; Ahenk's card says PyPI's voxcpm 2.0.3 works with its LoRA), so the 4 GB of PyTorch is not
/// downloaded three times. torchcodec is left out of every lock (<c>--excludes</c>): it has no Windows CUDA build, the voices never import
/// it, and torchaudio 2.8 reads audio with soundfile. Download sizes were added up from the wheels of the Windows locks (Linux CUDA: about
/// 4.1 GB with NVIDIA's libraries).
/// </summary>
public static class PythonEnvironments
{
    /// <summary>XTTS-v2: coqui-tts 0.28.0 (MPL-2.0).</summary>
    public static readonly PythonEnvironment Xtts = new("xtts", "XTTS-v2", ["torch==2.8.0", "torchaudio==2.8.0", "coqui-tts==0.28.0", "six"],
        3_650_000_000, 800_000_000);

    /// <summary>F5-TTS: f5-tts 1.1.22 (MIT).</summary>
    public static readonly PythonEnvironment F5 = new("f5", "F5-TTS", ["torch==2.8.0", "torchaudio==2.8.0", "f5-tts==1.1.22"], 3_800_000_000, 950_000_000);

    /// <summary>VoxCPM2, Trendyol-TTS and Ahenk: voxcpm 2.0.3 (Apache-2.0) and trnorm (Apache-2.0, Ahenk's Turkish text normaliser, pinned commit).</summary>
    public static readonly PythonEnvironment VoxCpm = new("voxcpm", "VoxCPM2", ["torch==2.8.0", "torchaudio==2.8.0", "voxcpm==2.0.3", "soundfile", "scipy",
        "trnorm @ https://github.com/ozguradmin/ahenk-turkish-tts/archive/8981df9354a6dc95a3a2b4730b7fbff196dda219.zip"], 3_800_000_000, 950_000_000);

    /// <summary>MMS-TTS: transformers 4.56.2 (Apache-2.0); processor only.</summary>
    public static readonly PythonEnvironment Mms = new("mms", "MMS-TTS", ["torch==2.8.0", "transformers==4.56.2"], 660_000_000, 660_000_000, ProcessorOnly: true);

    /// <summary>Every environment.</summary>
    /// <summary>yazgı-tts-1 (0.1.35): PyTorch on the processor and the Turkish text front end its package needs.</summary>
    public static readonly PythonEnvironment Yazgi = new("yazgi", "yazgı-tts", ["torch==2.8.0", "numpy", "soundfile", "normalizer-tr==0.7.0"], 700_000_000,
        700_000_000, ProcessorOnly: true);

    public static readonly IReadOnlyList<PythonEnvironment> All = [Xtts, F5, VoxCpm, Mms, Yazgi];
}
