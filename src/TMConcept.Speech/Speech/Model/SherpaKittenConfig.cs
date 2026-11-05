using System.Runtime.InteropServices;

namespace TMConcept.Speech.Speech;

/// <summary>sherpa-onnx 2.0.1 <c>SherpaOnnxOfflineTtsKittenModelConfig</c> (unused; zero).</summary>
[StructLayout(LayoutKind.Sequential)]
internal struct SherpaKittenConfig
{
    public IntPtr Model;
    public IntPtr Voices;
    public IntPtr Tokens;
    public IntPtr DataDir;
    public float LengthScale;
    public IntPtr Lexicon;
}
