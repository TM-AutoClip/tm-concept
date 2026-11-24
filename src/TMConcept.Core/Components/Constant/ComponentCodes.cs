namespace TMConcept.Core.Components;

/// <summary>Component codes (an engine is <see cref="EnginePrefix"/> + its kind in lower case, an image model <see cref="ImagePrefix"/> + its id).</summary>
public static class ComponentCodes
{
    /// <summary>Node.js.</summary>
    public const string Node = "node";

    /// <summary>The shared Chrome Headless Shell of HyperFrames and Revideo (installed with the engine).</summary>
    public const string Browser = "browser";

    /// <summary>ffmpeg + ffprobe.</summary>
    public const string Ffmpeg = "ffmpeg";

    /// <summary>sherpa-onnx libraries of the local voice.</summary>
    public const string SpeechRuntime = "sherpa-onnx";

    /// <summary>stable-diffusion.cpp (sd-cli) of the local image models.</summary>
    public const string ImageRuntime = "sd-cpp";

    /// <summary>uv, which installs Python and the packages of the Python voices.</summary>
    public const string Uv = "uv";

    /// <summary>The pinned Python of the Python voices (installed by uv into the tools folder).</summary>
    public const string Python = "speech-python";

    /// <summary>Prefix of a Python voice environment (its packages, torch included: "speech-env.xtts").</summary>
    public const string PythonEnvironmentPrefix = "speech-env.";

    /// <summary>The app's own Ollama copy ("Ollama (TM Concept)": the official release package, only the graphics backend this computer needs).</summary>
    public const string AppOllama = "ollama-app";

    /// <summary>Prefix of a model of the app's own Ollama copy ("ollama-model.qwen3.5:4b-q4_K_M"; pulled through the app's own server).</summary>
    public const string AppOllamaModelPrefix = "ollama-model.";

    /// <summary>Prefix of engine components ("engine.hyperframes").</summary>
    public const string EnginePrefix = "engine.";

    /// <summary>Prefix of voice components ("voice.supertonic").</summary>
    public const string VoicePrefix = "voice.";

    /// <summary>Prefix of image model components ("image.z-image-turbo").</summary>
    public const string ImagePrefix = "image.";

    /// <summary>Prefix of local embedding model components ("embedding.paraphrase-multilingual-minilm").</summary>
    public const string EmbeddingPrefix = "embedding.";

    /// <summary>The ready music library (0.1.45: Incompetech tracks of <c>catalog/music.json</c>, downloaded after the user agreed).</summary>
    public const string MusicLibrary = "music-library";

    /// <summary>
    /// The Python environment of the local music model (0.1.46, C5): rakkase and ACE-Step 1.5 at pinned commits, their packages installed by
    /// uv (PyTorch for NVIDIA CUDA included). A component of its own, apart from the model's weights (rule 18).
    /// </summary>
    public const string MusicEnvironment = PythonEnvironmentPrefix + "ace-step";

    /// <summary>Prefix of local music model weight components ("music.ace-step-1.5").</summary>
    public const string MusicModelPrefix = "music.";

    /// <summary>The weights of ACE-Step 1.5 with its Qwen3-Embedding text encoder (0.1.46, C5; about 6 GB, each file pinned by its SHA-256).</summary>
    public const string MusicModel = MusicModelPrefix + "ace-step-1.5";

    /// <summary>
    /// whisper.cpp (0.1.48, S3): the program that hears the narration and gives each subtitle word its real time. A component of its own,
    /// apart from its model (rule 18); never downloaded without the user's yes.
    /// </summary>
    public const string SpeechRecognizer = "whisper-cpp";

    /// <summary>Prefix of a whisper.cpp model component ("whisper-model.base").</summary>
    public const string RecognizerModelPrefix = "whisper-model.";

    /// <summary>The multilingual base model of whisper.cpp (about 148 MB; the default, a small multilingual model would be "whisper-model.small").</summary>
    public const string RecognizerModel = RecognizerModelPrefix + "base";

    /// <summary>The start of the code of the text recognition's model and runtime (kept like a model by the clean-ups).</summary>
    public const string OcrPrefix = "ocr.";
}
