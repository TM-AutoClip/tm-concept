using System.Runtime.InteropServices;

namespace TMConcept.Speech.Speech;

/// <summary>sherpa-onnx 2.0.1 <c>SherpaOnnxGenerationConfig</c> (reference audio only for Pocket TTS; phonemes and tokens stay empty).</summary>
[StructLayout(LayoutKind.Sequential)]
internal struct SherpaGenerationConfig
{
    public float SilenceScale;
    public float Speed;
    public int Sid;
    public IntPtr ReferenceAudio;
    public int ReferenceAudioLen;
    public int ReferenceSampleRate;
    public IntPtr ReferenceText;
    public int NumSteps;
    public IntPtr Extra;
    public IntPtr PhonemeCodepoints;
    public IntPtr PhonemeCodepointsLens;
    public int PhonemeCodepointsNumSentences;
    public IntPtr Tokens;
    public IntPtr TokensLens;
    public int TokensNumSentences;
}
