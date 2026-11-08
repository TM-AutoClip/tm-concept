using TMConcept.Core.Profiles;
using TMConcept.Core.Tools;

namespace TMConcept.Speech.Chatterbox;

/// <summary>
/// Chatterbox Multilingual V3 (Resemble AI, MIT, 0.5B, 23 languages) as ONNX graphs: the export Masterx/chatterbox-multilingual-v3-ONNX (MIT,
/// a format conversion of t3_mtl23ls_v3 + s3gen; the same four graphs and contract as onnx-community/chatterbox-multilingual-ONNX), fetched
/// file by file at a fixed commit with their SHA-256 (Hugging Face LFS values; the tokenizer's computed on 2026-11-08). The 4-bit language
/// model is used (354 MB instead of 2.1 GB fp32; the export's own check: English 2.5 %, German 4.4 % WER). Run in the speech worker with the
/// ONNX Runtime of the sherpa-onnx libraries, on the processor (that build has no graphics card support). The export holds no watermark
/// (Resemble's Python package adds a Perth watermark; the graphs do not) and no reference voice is kept: it speaks with the user's own samples.
/// </summary>
public static class ChatterboxModel
{
    /// <summary>The version shown in the install window (the commit of the export).</summary>
    public const string Version = "v3-onnx-19b94af";

    /// <summary>Output sample rate.</summary>
    public const int SampleRate = 24_000;

    /// <summary>Samples per speech token (25 tokens per second).</summary>
    public const int SamplesPerToken = 960;

    /// <summary>First speech token id (start of speech); ids from here up are not sounds.</summary>
    public const long StartSpeech = 6561;

    /// <summary>The stop token.</summary>
    public const long StopSpeech = 6562;

    /// <summary>Layers of the language model (each with a key and a value cache).</summary>
    public const int Layers = 30;

    /// <summary>Key/value heads.</summary>
    public const int Heads = 16;

    /// <summary>Size of a head.</summary>
    public const int HeadSize = 64;

    /// <summary>
    /// Most speech tokens per part (40 s; upstream's max_new_tokens = 1000). A part is at most <see cref="MaxPartCharacters"/> long, which is
    /// about 20 s of speech.
    /// </summary>
    public const int MaxNewTokens = 1000;

    /// <summary>
    /// Longest text spoken in one go: longer scenes are spoken sentence by sentence and joined. Upstream's own demo takes at most 300
    /// characters; above about 350 the multilingual model was reported to make up words (research of 7 Nov 2026).
    /// </summary>
    public const int MaxPartCharacters = 300;

    /// <summary>Silence between the parts of a scene, in milliseconds.</summary>
    public const int PartGapMs = 150;

    /// <summary>Expressiveness (upstream default; 0.25–2).</summary>
    public const float Exaggeration = 0.5f;

    /// <summary>Sampling as upstream's multilingual generate() does it: repetition penalty, temperature, min-p and top-p.</summary>
    public const float RepetitionPenalty = 1.2f;

    /// <summary>Sampling temperature.</summary>
    public const float Temperature = 0.8f;

    /// <summary>Min-p filter.</summary>
    public const float MinP = 0.05f;

    /// <summary>Top-p filter (1 = off, as upstream's multilingual default).</summary>
    public const float TopP = 1f;

    /// <summary>The fixed seed of the sampling: the same text gets the same speech tokens.</summary>
    public const int Seed = 42;

    /// <summary>The tokenizer file.</summary>
    public const string TokenizerFile = "tokenizer.json";

    /// <summary>Graph files (each with its external data file next to it).</summary>
    public const string SpeechEncoderFile = "onnx/speech_encoder.onnx";

    /// <summary>The token embedding graph.</summary>
    public const string EmbedFile = "onnx/embed_tokens.onnx";

    /// <summary>The 4-bit language model graph.</summary>
    public const string LanguageModelFile = "onnx/language_model_q4.onnx";

    /// <summary>The flow matching decoder and vocoder graph.</summary>
    public const string DecoderFile = "onnx/conditional_decoder.onnx";

    /// <summary>
    /// Output languages it speaks (of the app's twelve): its 23 languages without Indonesian (it has none) and Russian (upstream marks the
    /// stresses of Russian text with a Python library the export does not include; Supertonic 3 speaks Russian).
    /// </summary>
    public static readonly IReadOnlyList<string> Languages =
    [
        ContentLanguages.Turkish, ContentLanguages.English, "de", "es", "fr", "it", "pt", "nl", "pl", "sv",
    ];

    private const string Repository = "https://huggingface.co/Masterx/chatterbox-multilingual-v3-ONNX/resolve/19b94afe41de738624b2f3afc75b4cd56221fbeb/";

    /// <summary>The files, each pinned (relative path on disk = path in the repository).</summary>
    public static readonly IReadOnlyList<ToolArchive> Files = new (string Path, string Sha256, long Bytes)[]
    {
        (TokenizerFile, "8763bb615f5e0ad49fbb53b65d371e37961f64bb0a55b96c0205f4a59ba5be13", 72_765), // SHA-256 computed on 2026-11-08 (not an LFS file)
        ("LICENSE", "4543f6777abede354f09bc2ba6580bd475c364e7d6f39315bf9d315d033b48f4", 1_068),
        (SpeechEncoderFile, "012a9583c0a9560afa11307b585f1e799fca0e1701bdae53fa34766da78f8eae", 1_204_600),
        (SpeechEncoderFile + "_data", "f1f663a1a03aa885f8f0fcaa730dd8dfbce8caad514093b13c9682a4a2842990", 591_274_880),
        (EmbedFile, "c8dd692ee8dfc9099baf39c8074e9bcc0a760e8c25ae1cac4a8c62c7cf4536bc", 17_616),
        (EmbedFile + "_data", "d263116bec8ae582953c99bdffa9ecd53914d1ae2732da8455e361ed5baa2a9a", 68_808_704),
        (LanguageModelFile, "91846c24706116f87b39aac5b13aa1a67243b6f973bc433327d143b8eff9fde6", 228_633),
        (LanguageModelFile + "_data", "621a963492f871ccc841592c6ca117bf88dbea4c0c25d61be4391197c8b878b0", 353_621_248),
        (DecoderFile, "f494d8fed14f269e969a58ccbd627cdfb5eb889d0816c7fde09621fdcb160128", 6_371_670),
        (DecoderFile + "_data", "f41f149c1a30c273133739e873294e28dbc3c4ae6376b0a501e9cd21de286835", 533_970_816),
    }.Select(f => new ToolArchive(ToolKind.ChatterboxVoice, Version, "any", new Uri(Repository + f.Path), f.Sha256, string.Empty, f.Path, f.Bytes))
        .ToList();
}
