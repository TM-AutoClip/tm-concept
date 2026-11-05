using System.Runtime.InteropServices;

namespace TMConcept.Speech.Speech;

/// <summary>sherpa-onnx 2.0.1 <c>SherpaOnnxOfflineTtsConfig</c>.</summary>
[StructLayout(LayoutKind.Sequential)]
internal struct SherpaTtsConfig
{
    public SherpaTtsModelConfig Model;
    public IntPtr RuleFsts;
    public int MaxNumSentences;
    public IntPtr RuleFars;
    public float SilenceScale;
}
