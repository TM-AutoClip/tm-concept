using System.Runtime.InteropServices;

namespace TMConcept.Speech.Speech;

/// <summary>sherpa-onnx 2.0.1 <c>SherpaOnnxOfflineTtsPocketModelConfig</c> (unused; zero).</summary>
[StructLayout(LayoutKind.Sequential)]
internal struct SherpaPocketConfig
{
    public IntPtr LmFlow;
    public IntPtr LmMain;
    public IntPtr Encoder;
    public IntPtr Decoder;
    public IntPtr TextConditioner;
    public IntPtr VocabJson;
    public IntPtr TokenScoresJson;
    public int VoiceEmbeddingCacheCapacity;
}
