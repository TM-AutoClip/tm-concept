using System.Runtime.InteropServices;

namespace TMConcept.Speech.Speech;

/// <summary>sherpa-onnx 2.0.1 <c>SherpaOnnxOfflineTtsMatchaModelConfig</c> (unused; zero).</summary>
[StructLayout(LayoutKind.Sequential)]
internal struct SherpaMatchaConfig
{
    public IntPtr AcousticModel;
    public IntPtr Vocoder;
    public IntPtr Lexicon;
    public IntPtr Tokens;
    public IntPtr DataDir;
    public float NoiseScale;
    public float LengthScale;
    public IntPtr DictDir;
}
