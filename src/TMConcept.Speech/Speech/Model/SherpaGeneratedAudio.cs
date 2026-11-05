using System.Runtime.InteropServices;

namespace TMConcept.Speech.Speech;

/// <summary>sherpa-onnx 2.0.1 <c>SherpaOnnxGeneratedAudio</c>: mono float samples in [-1, 1].</summary>
[StructLayout(LayoutKind.Sequential)]
internal struct SherpaGeneratedAudio
{
    public IntPtr Samples;
    public int Count;
    public int SampleRate;
}
