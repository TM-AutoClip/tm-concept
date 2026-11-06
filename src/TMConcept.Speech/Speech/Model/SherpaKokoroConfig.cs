using System.Runtime.InteropServices;

namespace TMConcept.Speech.Speech;

/// <summary>sherpa-onnx 2.0.1 <c>SherpaOnnxOfflineTtsKokoroModelConfig</c>: UTF-8 file names of the Kokoro model (the data and dict folders stay empty).</summary>
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
