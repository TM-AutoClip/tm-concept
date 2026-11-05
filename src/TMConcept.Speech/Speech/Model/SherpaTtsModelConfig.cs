using System.Runtime.InteropServices;

namespace TMConcept.Speech.Speech;

/// <summary>sherpa-onnx 2.0.1 <c>SherpaOnnxOfflineTtsModelConfig</c>: one block per model family, only the used one is filled.</summary>
[StructLayout(LayoutKind.Sequential)]
internal struct SherpaTtsModelConfig
{
    public SherpaVitsConfig Vits;
    public int NumThreads;
    public int Debug;
    public IntPtr Provider;
    public SherpaMatchaConfig Matcha;
    public SherpaKokoroConfig Kokoro;
    public SherpaKittenConfig Kitten;
    public SherpaZipvoiceConfig Zipvoice;
    public SherpaPocketConfig Pocket;
    public SherpaSupertonicConfig Supertonic;
}
