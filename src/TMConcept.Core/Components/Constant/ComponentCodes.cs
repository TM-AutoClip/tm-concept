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
}
