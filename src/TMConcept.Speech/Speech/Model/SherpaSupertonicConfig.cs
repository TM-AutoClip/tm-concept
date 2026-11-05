using System.Runtime.InteropServices;

namespace TMConcept.Speech.Speech;

/// <summary>sherpa-onnx 2.0.1 <c>SherpaOnnxOfflineTtsSupertonicModelConfig</c>: UTF-8 file names of the Supertonic model.</summary>
[StructLayout(LayoutKind.Sequential)]
internal struct SherpaSupertonicConfig
{
    public IntPtr DurationPredictor;
    public IntPtr TextEncoder;
    public IntPtr VectorEstimator;
    public IntPtr Vocoder;
    public IntPtr TtsJson;
    public IntPtr UnicodeIndexer;
    public IntPtr VoiceStyle;
}
