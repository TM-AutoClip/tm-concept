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
}
