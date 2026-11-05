using System.Runtime.InteropServices;

namespace TMConcept.Speech.Speech;

/// <summary>sherpa-onnx 2.0.1 <c>SherpaOnnxOfflineTtsVitsModelConfig</c> (unused here; kept zero so the layout matches the C header).</summary>
[StructLayout(LayoutKind.Sequential)]
internal struct SherpaVitsConfig
{
    public IntPtr Model;
    public IntPtr Lexicon;
    public IntPtr Tokens;
    public IntPtr DataDir;
    public float NoiseScale;
    public float NoiseScaleW;
    public float LengthScale;
    public IntPtr DictDir;
}
