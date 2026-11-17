using TMConcept.Core.Tools;

namespace TMConcept.Documents.Embeddings;

/// <summary>
/// The documents' meaning model: intfloat/multilingual-e5-small (MIT, not gated; about 100 languages, Turkish and English among them; 384
/// dimensions; reads 512 tokens; every input needs "query: " or "passage: "), its int8 ONNX file and its tokenizer, fetched at a fixed commit
/// with their SHA-256 (checked on Hugging Face on 17 Nov 2026). About 135 MB; downloaded only after a confirmation, not part of the app.
/// </summary>
public static class E5Model
{
    /// <summary>The model id (vectors are stored under it).</summary>
    public const string Id = "multilingual-e5-small";

    /// <summary>Its display name.</summary>
    public const string Name = "Multilingual E5 small";

    /// <summary>The model file's name on disk.</summary>
    public const string ModelFile = "model.onnx";

    /// <summary>The tokenizer file's name on disk.</summary>
    public const string TokenizerFile = "tokenizer.json";

    /// <summary>Most tokens it reads.</summary>
    public const int MaxTokens = 512;

    private const string Repository = "https://huggingface.co/intfloat/multilingual-e5-small/resolve/614241f622f53c4eeff9890bdc4f31cfecc418b3/";

    /// <summary>The int8 model (the repository's only int8 build).</summary>
    public static readonly ToolArchive Model = new(ToolKind.EmbeddingWeights, "1", "any", new Uri(Repository + "onnx/model_qint8_avx512_vnni.onnx"),
        "dd476dd0c2514e9b9be83aeb3853fac0763e0bdf4a71645407587d77c48a2d88", string.Empty, ModelFile, 118_346_824);

    /// <summary>The tokenizer.</summary>
    public static readonly ToolArchive Tokenizer = new(ToolKind.EmbeddingWeights, "1", "any", new Uri(Repository + "onnx/tokenizer.json"),
        "0b44a9d7b51c3c62626640cda0e2c2f70fdacdc25bbbd68038369d14ebdf4c39", string.Empty, TokenizerFile, 17_082_730); // SHA-256 of the public file

    /// <summary>Both files.</summary>
    public static readonly IReadOnlyList<ToolArchive> Files = [Model, Tokenizer];
}
