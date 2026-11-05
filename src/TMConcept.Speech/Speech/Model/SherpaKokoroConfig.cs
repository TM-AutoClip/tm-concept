using System.Runtime.InteropServices;

namespace TMConcept.Speech.Speech;

/// <summary>sherpa-onnx 2.0.1 <c>SherpaOnnxOfflineTtsKokoroModelConfig</c> (unused; zero).</summary>
[StructLayout(LayoutKind.Sequential)]
internal struct SherpaKokoroConfig
{
    public IntPtr Model;
    public IntPtr Voices;
    public IntPtr Tokens;
    public IntPtr DataDir;
    public float LengthScale;
    public IntPtr DictDir;
    public IntPtr Lexicon;
    public IntPtr Lang;
}
