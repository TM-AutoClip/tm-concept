using TMConcept.Core.Tools;

namespace TMConcept.Speech.AhenkCep;

/// <summary>
/// Ahenk Cep (ozgurguler/ahenk-cep-tr, CC BY 4.0; a small Turkish voice trained on EMA Lightning, Apache-2.0): four ONNX graphs of the
/// model's own export (onnx/ahenk-cep.json at a fixed commit; the int8 set of its web demo: int8 text, condition and step graphs with the
/// half-precision decoder, about 13.3 MB), fetched file by file with the SHA-256 of that manifest (equal to the Hugging Face LFS values,
/// checked on 16 Nov 2026). Turkish only, one designed voice with seven speaking styles, 48 kHz. Its text must be the spoken form in
/// lower case (numbers and abbreviations already read out: the app's Turkish normalizer does it before every Turkish voice). Run in the
/// speech worker with the ONNX Runtime of the sherpa-onnx libraries, on the processor.
/// </summary>
public static class AhenkCepModel
{
    /// <summary>The version shown in the install window (the commit of the model and the variant).</summary>
    public const string Version = "345c4cb-int8";

    /// <summary>Output sample rate.</summary>
    public const int SampleRate = 48_000;

    /// <summary>Samples per latent frame (25 frames per second).</summary>
    public const int Hop = 1920;

    /// <summary>Size of a latent frame.</summary>
    public const int LatentSize = 64;

    /// <summary>Euler steps of the flow (the manifest's default).</summary>
    public const int Steps = 16;

    /// <summary>Classifier-free guidance (the manifest's default).</summary>
    public const float Guidance = 2.0f;

    /// <summary>Most frames of one word.</summary>
    public const int MaxWordFrames = 250;

    /// <summary>Most frames of one piece (two minutes).</summary>
    public const int MaxFrames = 3000;

    /// <summary>Frames the decoder turns into sound at once.</summary>
    public const int Window = 100;

    /// <summary>Frames of context on each side of a decoder window.</summary>
    public const int Context = 8;

    /// <summary>The text graph.</summary>
    public const string TextFile = "onnx/text.int8.onnx";

    /// <summary>The condition graph.</summary>
    public const string ConditionFile = "onnx/cond.int8.onnx";

    /// <summary>The flow step graph.</summary>
    public const string StepFile = "onnx/step.int8.onnx";

    /// <summary>The decoder graph (half precision; the int8 set uses it).</summary>
    public const string DecoderFile = "onnx/decoder.f16.onnx";

    /// <summary>The characters the model knows, in id order (from the manifest; anything else is read as a space).</summary>
    public static readonly IReadOnlyList<string> Vocabulary =
    [
        "<pad>", "<unk>", " ", "!", "\"", "%", "&", "'", "(", ")", ",", "-", ".", "/", ":", ";", "?",
        "a", "b", "c", "d", "e", "f", "g", "h", "i", "j", "k", "l", "m", "n", "o", "p", "q", "r", "s", "t", "u", "v", "w", "x", "y", "z",
        "ç", "ö", "ü", "ğ", "ı", "ş",
    ];

    /// <summary>The speaking styles, in id order (from the manifest). "laughter" is not trained and is not offered.</summary>
    public static readonly IReadOnlyList<string> Styles = ["neutral", "angry", "calm", "cheerful", "empathetic", "sad", "surprised", "laughter"];

    private const string Repository = "https://huggingface.co/ozgurguler/ahenk-cep-tr/resolve/345c4cbb934815fa96ffd0d5061fd913237544bc/";

    /// <summary>The files, each pinned (relative path on disk = path in the repository).</summary>
    public static readonly IReadOnlyList<ToolArchive> Files = new (string Path, string Sha256, long Bytes)[]
    {
        (TextFile, "8fbf3c55d881c9c7135eeef0213142687e051c1d8c69a465040ae6fa15b94946", 2_437_675),
        (ConditionFile, "1da9566b339771ca7fcd26a1fecc13379c0e363595f39682567331b566bd8796", 246_779),
        (StepFile, "dfdfd0f0c4d4f049338a64d31362f6c9c3afb8b1cd9250a15d4e080c65b1fb76", 4_536_282),
        (DecoderFile, "94dd75f2c8a1da6f7a6cb86d1974caf11f6aa73e965777192c208e9d1aa32d52", 6_092_374),
    }.Select(f => new ToolArchive(ToolKind.AhenkCepVoice, Version, "any", new Uri(Repository + f.Path), f.Sha256, string.Empty, f.Path, f.Bytes))
        .ToList();
}
