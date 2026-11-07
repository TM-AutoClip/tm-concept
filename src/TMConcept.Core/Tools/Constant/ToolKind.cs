namespace TMConcept.Core.Tools;

/// <summary>Programs the app downloads into its own tools folder after the user agrees (the system PATH is never changed).</summary>
public enum ToolKind
{
    /// <summary>Node.js (runs the video engines).</summary>
    Node = 0,

    /// <summary>ffmpeg + ffprobe (mux, subtitles, thumbnails).</summary>
    Ffmpeg = 1,

    /// <summary>Supertonic 3 voice model (sherpa-onnx).</summary>
    SupertonicVoice = 2,

    /// <summary>sherpa-onnx with ONNX Runtime: the native libraries the speech worker loads for the local voice (not in the app itself).</summary>
    SpeechRuntime = 3,

    /// <summary>stable-diffusion.cpp (sd-cli) that runs the local image models.</summary>
    ImageRuntime = 4,

    /// <summary>The CUDA runtime libraries next to the Windows CUDA build of stable-diffusion.cpp (NVIDIA graphics cards).</summary>
    ImageRuntimeCuda = 5,

    /// <summary>A weights file of a local image model (one file, not an archive).</summary>
    ImageWeights = 6,

    /// <summary>Kokoro 82M voice model (sherpa-onnx, English).</summary>
    KokoroVoice = 7,

    /// <summary>A file of the local embedding model (one file, not an archive; run with the ONNX Runtime of the speech libraries).</summary>
    EmbeddingWeights = 8,

    /// <summary>Pocket TTS voice model (sherpa-onnx, English, copies a voice from a sample).</summary>
    PocketVoice = 9,
}
