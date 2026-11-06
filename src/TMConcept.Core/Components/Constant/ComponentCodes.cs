namespace TMConcept.Core.Components;

/// <summary>Component codes (an engine is <see cref="EnginePrefix"/> + its kind in lower case, an image model <see cref="ImagePrefix"/> + its id).</summary>
public static class ComponentCodes
{
    /// <summary>Node.js.</summary>
    public const string Node = "node";

    /// <summary>ffmpeg + ffprobe.</summary>
    public const string Ffmpeg = "ffmpeg";

    /// <summary>sherpa-onnx libraries of the local voice.</summary>
    public const string SpeechRuntime = "sherpa-onnx";

    /// <summary>stable-diffusion.cpp (sd-cli) of the local image models.</summary>
    public const string ImageRuntime = "sd-cpp";

    /// <summary>Prefix of engine components ("engine.hyperframes").</summary>
    public const string EnginePrefix = "engine.";

    /// <summary>Prefix of voice components ("voice.supertonic").</summary>
    public const string VoicePrefix = "voice.";

    /// <summary>Prefix of image model components ("image.z-image-turbo").</summary>
    public const string ImagePrefix = "image.";
}
