using System.Runtime.InteropServices;

namespace TMConcept.Speech.Speech;

/// <summary>sherpa-onnx 2.0.1 <c>SherpaOnnxOfflineTtsZipvoiceModelConfig</c> (unused; zero).</summary>
[StructLayout(LayoutKind.Sequential)]
internal struct SherpaZipvoiceConfig
{
    public IntPtr Tokens;
    public IntPtr Encoder;
    public IntPtr Decoder;
    public IntPtr Vocoder;
    public IntPtr DataDir;
    public IntPtr Lexicon;
    public float FeatScale;
    public float TShift;
    public float TargetRms;
    public float GuidanceScale;
}
