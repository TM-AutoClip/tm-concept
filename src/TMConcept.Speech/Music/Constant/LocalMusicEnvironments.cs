using TMConcept.Speech.Python;

namespace TMConcept.Speech.Music;

/// <summary>
/// The Python environments of the local music families (28 Nov 2026), installed like the voices' environments (<see cref="PythonInstaller"/>:
/// <c>uv venv</c> with the app's Python 3.11.18, then <c>uv pip sync</c> of the lock shipped with the app, <c>Python/Files/Locks</c>). Each is
/// card-only: there is no processor lock (far too slow to offer). They cannot share an environment: MusicGen runs on transformers 5.22.1,
/// AudioLDM 2's pipeline breaks on any transformers newer than 4.47 (diffusers issue #12630), and MAGNeT's audiocraft needs PyTorch 2.1.0
/// with xformers 0.0.22.post7 (its restricted attention mask only works through xformers), built for CUDA 12.1. Download sizes are added up
/// from the Windows wheels of the locks (PyTorch with CUDA is most of each).
/// </summary>
public static class LocalMusicEnvironments
{
    /// <summary>MusicGen through Hugging Face transformers 5.22.1 (Apache-2.0); torchaudio for MusicGen Melody's feature extractor.</summary>
    public static readonly PythonEnvironment MusicGen = new("musicgen", "MusicGen", ["torch==2.8.0", "torchaudio==2.8.0", "transformers==5.22.1", "soundfile", "numpy"],
        3_700_000_000, 0);

    /// <summary>AudioLDM 2 through diffusers 0.32.2 with transformers 4.47.1 (both Apache-2.0) and accelerate (the model offload on small cards).</summary>
    public static readonly PythonEnvironment AudioLdm2 = new("audioldm2", "AudioLDM 2", ["torch==2.8.0", "diffusers==0.32.2", "transformers==4.47.1", "accelerate",
        "safetensors", "sentencepiece", "protobuf", "soundfile", "scipy", "numpy"], 3_700_000_000, 0);

    /// <summary>
    /// MAGNeT: what audiocraft's model code imports (its own <c>requirements.txt</c> is not installed: av 11 and pesq have no Windows builds,
    /// gradio and torchvision are never used); spaCy 3.7.5 because 3.7.6 was withdrawn from PyPI.
    /// </summary>
    public static readonly PythonEnvironment Magnet = new("magnet", "MAGNeT", ["torch==2.1.0", "torchaudio==2.1.0", "xformers==0.0.22.post7", "numpy==1.26.4",
        "av==12.3.0", "spacy==3.7.5", "transformers==4.46.3", "sentencepiece", "einops", "omegaconf", "num2words", "soundfile", "librosa==0.10.2.post1",
        "torchdiffeq", "julius", "flashy", "dora-search"], 3_100_000_000, 0, CudaBackend: PythonPins.Cuda121Backend);

    /// <summary>
    /// DiffRhythm (1.0 and 1.2 share it): PyTorch 2.6.0 for CUDA 12.4 (its own pin), transformers exactly 4.49.0 (its transformer unpacks the
    /// Llama layers' output; a newer transformers returns a bare tensor and the piece breaks silently), MuQ-MuLan's own package and what the
    /// inference code imports; the training packages and the lyrics front end (phonemizer and espeak-ng, onnxruntime, the Chinese and Japanese
    /// text tools) are left out, because the app makes instrumental pieces only.
    /// </summary>
    public static readonly PythonEnvironment DiffRhythm = new("diffrhythm", "DiffRhythm", ["torch==2.6.0", "torchaudio==2.6.0", "transformers==4.49.0",
        "torchdiffeq==0.2.5", "x-transformers==2.1.2", "librosa==0.10.2.post1", "muq==0.1.0", "mutagen==1.47.0", "safetensors", "numpy", "soundfile", "einops"],
        3_400_000_000, 0, CudaBackend: PythonPins.Cuda124Backend);

    /// <summary>
    /// SongGeneration: its own <c>requirements.txt</c> (PyTorch 2.6.0 for CUDA 12.4, transformers 4.37.2, diffusers 0.27.2…) without the build
    /// tools ninja and wheel, plus soundfile, and its <c>requirements_nodeps.txt</c> added to the lock by hand at its own versions (hydra-core
    /// 1.0.7 and omegaconf 2.2.0 would not resolve together; upstream installs them without their dependencies the same way).
    /// </summary>
    public static readonly PythonEnvironment SongGeneration = new("songgeneration", "SongGeneration", ["torch==2.6.0", "torchaudio==2.6.0", "torchvision==0.21.0",
        "transformers==4.37.2", "diffusers==0.27.2", "peft==0.10.0", "huggingface-hub==0.25.2", "k-diffusion==0.1.1", "einops-exts==0.0.4", "alias-free-torch",
        "descript-audio-codec", "einops", "flashy", "julius", "kaldiio", "lameenc", "librosa", "lightning", "nnAudio", "openunmix", "vector-quantize-pytorch",
        "x-transformers", "soundfile"], 4_200_000_000, 0, CudaBackend: PythonPins.Cuda124Backend);

    /// <summary>
    /// YuE2: the exact pins of its <c>pyproject.toml</c> (PyTorch 2.10.0, here the CUDA 12.8 build; transformers 4.57.6, accelerate 1.13.0…).
    /// No flash-attn: it runs on PyTorch's own attention.
    /// </summary>
    public static readonly PythonEnvironment Yue2 = new("yue2", "YuE2", ["torch==2.10.0", "transformers==4.57.6", "huggingface-hub==0.36.2", "safetensors==0.7.0",
        "tiktoken==0.12.0", "numpy==2.2.6", "soundfile==0.13.1", "accelerate==1.13.0"], 3_600_000_000, 0);

    /// <summary>
    /// YuE 1 through YuE-exllamav2: its <c>requirements.txt</c> (PyTorch 2.6.0 for CUDA 12.4, scipy 1.10.1, the official exllamav2 0.2.8 wheels
    /// from its GitHub release and the flash-attn 2.7.4.post1 wheels it names: kingbri1's build for Windows, the official one for Linux) with
    /// transformers pinned at 4.49.0 (it names none; YuE's code is from the 4.4x days) and soundfile.
    /// </summary>
    public static readonly PythonEnvironment Yue = new("yue", "YuE", ["torch==2.6.0", "torchaudio==2.6.0", "transformers==4.49.0", "omegaconf", "einops", "numpy",
        "sentencepiece", "tqdm", "tensorboard", "descript-audiotools", "descript-audio-codec", "scipy==1.10.1", "accelerate", "soundfile", "exllamav2",
        "flash-attn"], 3_900_000_000, 0, CudaBackend: PythonPins.Cuda124Backend);
}
