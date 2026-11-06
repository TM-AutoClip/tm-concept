using TMConcept.Core.Tools;

namespace TMConcept.Speech.Embeddings;

/// <summary>
/// The local model of the semantic duplicate check: sentence-transformers/paraphrase-multilingual-MiniLM-L12-v2 (Apache-2.0, not gated, 50+
/// languages, Turkish and English among them; checked on Hugging Face on 2026-11-06), its official int8 ONNX file and its tokenizer, fetched
/// at a fixed commit with their SHA-256 (the Hugging Face LFS values). Runs with the ONNX Runtime of the sherpa-onnx libraries, on the
/// processor. About 127 MB; downloaded only after a confirmation, not part of the app.
/// </summary>
public static class LocalEmbeddingModels
{
    /// <summary>The model id in the local catalog.</summary>
    public const string Id = "paraphrase-multilingual-minilm";

    /// <summary>Its display name.</summary>
    public const string Name = "Multilingual MiniLM";

    /// <summary>The model file's name on disk.</summary>
    public const string ModelFile = "model.onnx";

    /// <summary>The tokenizer file's name on disk.</summary>
    public const string TokenizerFile = "tokenizer.json";

    private const string Repository = "https://huggingface.co/sentence-transformers/paraphrase-multilingual-MiniLM-L12-v2/resolve/e8f8c211226b894fcb81acc59f3b34ba3efd5f42/";

    /// <summary>The int8 model (the same file serves every processor: the repository's arm64 and avx512 copies are identical).</summary>
    public static readonly ToolArchive Model = new(ToolKind.EmbeddingWeights, "1", "any", new Uri(Repository + "onnx/model_qint8_arm64.onnx"),
        "783fea82d71a58179b830a4dbd2d58447e640609e98eedf9ffa12622d375a672", string.Empty, ModelFile, 118_412_398);

    /// <summary>The tokenizer.</summary>
    public static readonly ToolArchive Tokenizer = new(ToolKind.EmbeddingWeights, "1", "any", new Uri(Repository + "tokenizer.json"),
        "2c3387be76557bd40970cec13153b3bbf80407865484b209e655e5e4729076b8",
        string.Empty, TokenizerFile, 9_081_518);

    /// <summary>Both files.</summary>
    public static readonly IReadOnlyList<ToolArchive> Files = [Model, Tokenizer];
}
